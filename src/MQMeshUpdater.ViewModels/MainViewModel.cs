using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MQMeshUpdater.Core.Sync;

namespace MQMeshUpdater.ViewModels;

public enum ZoneFilter
{
    All,
    NeedsAttention,
    Managed,
    NotManaged,
    Community,
}

public sealed partial class MainViewModel : ObservableObject
{
    public const string WebsiteUrl = "https://mqmesh.com";
    public const string IssuesUrl = "https://github.com/wired420/MQMeshUpdaterv2/issues";

    /// <summary>Above this many changes the visible list is rebuilt rather than edited.</summary>
    private const int MaxIncrementalChanges = 100;

    private readonly Dictionary<object, int> _positions = new(ReferenceEqualityComparer.Instance);
    private CancellationTokenSource? _cancel;
    private List<DownloadJob> _failed = [];
    private int _saveGeneration;

    public MainViewModel(SyncEngine engine, IUiServices ui)
    {
        Engine = engine;
        Ui = ui;
        _backupBeforeOverwrite = engine.Settings.BackupBeforeOverwrite;
    }

    public SyncEngine Engine { get; }

    public IUiServices Ui { get; }

    /// <summary>Every zone, grouped by expansion in release order and sorted by name within each.</summary>
    public RangeObservableCollection<ZoneRowViewModel> Rows { get; } = [];

    public List<ExpansionGroupViewModel> Groups { get; private set; } = [];

    /// <summary>
    /// The expansion headings and matching zones in display order, skipping
    /// collapsed groups, for lists that cannot group on their own.
    /// </summary>
    public RangeObservableCollection<object> VisibleItems { get; } = [];

    /// <summary>Raised when the search, filter or a zone's status changes which zones match.</summary>
    public event EventHandler? ViewChanged;

    public IReadOnlyList<KeyValuePair<ZoneFilter, string>> Filters { get; } =
    [
        new(ZoneFilter.All, "All zones"),
        new(ZoneFilter.NeedsAttention, "Updates and missing"),
        new(ZoneFilter.Managed, "Managed zones"),
        new(ZoneFilter.NotManaged, "Not managed"),
        new(ZoneFilter.Community, "With community meshes"),
    ];

    /// <summary>The <see cref="Filter"/> as one of <see cref="Filters"/>, for lists that select items rather than values.</summary>
    public KeyValuePair<ZoneFilter, string> SelectedFilter
    {
        get => Filters.First(f => f.Key == Filter);
        set => Filter = value.Key;
    }

    public string MqFolder => Engine.MqFolder;

    public string MeshFolder => Engine.MeshFolder;

    public string Version => UpdaterHost.Version;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UpdateCommand), nameof(RefreshCommand), nameof(CheckCommunityCommand), nameof(RetryFailedCommand), nameof(ChooseMqFolderCommand))]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    private bool _isBusy;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    private bool _isDownloading;

    [ObservableProperty]
    private string _statusText = "Starting...";

    [ObservableProperty]
    private string _summaryText = "";

    [ObservableProperty]
    private string _searchText = "";

    [ObservableProperty]
    private ZoneFilter _filter = ZoneFilter.All;

    [ObservableProperty]
    private double _progressValue;

    [ObservableProperty]
    private double _progressMaximum = 1;

    [ObservableProperty]
    private string _progressText = "";

    [ObservableProperty]
    private bool _backupBeforeOverwrite;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UpdateCommand))]
    private int _pendingFiles;

    [ObservableProperty]
    private string _updateButtonText = "Update";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RetryFailedCommand))]
    [NotifyPropertyChangedFor(nameof(HasFailed))]
    private int _failedCount;

    public bool IsIdle => !IsBusy;

    public bool HasFailed => FailedCount > 0;

    partial void OnSearchTextChanged(string value) => RefreshView();

    partial void OnFilterChanged(ZoneFilter value)
    {
        OnPropertyChanged(nameof(SelectedFilter));
        RefreshView();
    }

    partial void OnBackupBeforeOverwriteChanged(bool value)
    {
        Engine.Settings.BackupBeforeOverwrite = value;
        SettingsChanged();
    }

    /// <summary>
    /// Save choices shortly after the last change rather than on every click.
    /// </summary>
    public async void SettingsChanged()
    {
        var generation = ++_saveGeneration;
        await Task.Delay(500);

        if (generation == _saveGeneration)
        {
            SaveSettings();
        }
    }

    public void SaveSettings()
    {
        _saveGeneration++;
        Engine.Settings.Save(Engine.Paths);
    }

    public bool Matches(ZoneRowViewModel row)
    {
        var search = SearchText.Trim();

        if (search.Length > 0
            && !row.Name.Contains(search, StringComparison.OrdinalIgnoreCase)
            && !row.ShortName.Contains(search, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return Filter switch
        {
            ZoneFilter.NeedsAttention => row.NeedsAttention,
            ZoneFilter.Managed => row.IsManaged,
            ZoneFilter.NotManaged => !row.IsManaged,
            ZoneFilter.Community => row.CommunityCount > 0,
            _ => true,
        };
    }

    /// <summary>
    /// Load the zone list and check the local files.
    /// </summary>
    [RelayCommand(CanExecute = nameof(IsIdle))]
    public async Task RefreshAsync()
    {
        IsBusy = true;

        try
        {
            StatusText = "Getting the zone list from mqmesh.com...";
            var catalog = await Task.Run(() => Engine.LoadCatalogAsync());

            StatusText = "Checking your mesh files...";
            var progress = new Progress<(int Done, int Total)>(p =>
            {
                ProgressMaximum = Math.Max(1, p.Total);
                ProgressValue = p.Done;
                ProgressText = $"Checking files {p.Done}/{p.Total}";
            });
            await Task.Run(() => Engine.ScanAsync(progress));

            BuildRows(catalog);

            StatusText = catalog.Warning ?? catalog.Source switch
            {
                CatalogSource.NotModified => "Zone list is current.",
                _ => $"Zone list updated {catalog.Manifest.GeneratedAt.ToLocalTime():g}.",
            };
        }
        catch (Exception ex)
        {
            StatusText = ex.Message;
        }
        finally
        {
            ProgressValue = 0;
            ProgressText = "";
            IsBusy = false;
            OnPropertyChanged(nameof(MqFolder));
            OnPropertyChanged(nameof(MeshFolder));
        }
    }

    private void BuildRows(Catalog catalog)
    {
        var groups = new List<ExpansionGroupViewModel>();
        var rows = new List<ZoneRowViewModel>();
        _positions.Clear();

        foreach (var zoneGroup in catalog.Groups())
        {
            var group = new ExpansionGroupViewModel(zoneGroup.Code, zoneGroup.Name, zoneGroup.Order);
            group.ExpandedChanged += (_, _) => RefreshView();
            groups.Add(group);
            _positions[group] = _positions.Count;

            foreach (var zone in zoneGroup.Zones.OrderBy(z => z.Name, StringComparer.CurrentCultureIgnoreCase))
            {
                var row = new ZoneRowViewModel(this, zone, group);
                row.Replan();
                group.Rows.Add(row);
                rows.Add(row);
                _positions[row] = _positions.Count;
            }
        }

        Groups = groups;
        VisibleItems.ReplaceAll([]);
        Rows.ReplaceAll(rows);

        foreach (var group in groups)
        {
            group.Refresh();
        }

        RefreshSummary();
        RefreshView();
    }

    /// <summary>
    /// Bring <see cref="VisibleItems"/> in line with the search and filter.
    /// Small changes are applied in place so a list keeps its scroll position.
    /// </summary>
    public void RefreshView()
    {
        var items = new List<object>();

        foreach (var group in Groups)
        {
            var rows = group.Rows.Where(Matches).ToList();

            if (rows.Count == 0)
            {
                continue;
            }

            items.Add(group);

            if (group.IsExpanded)
            {
                items.AddRange(rows);
            }
        }

        var changes = new HashSet<object>(VisibleItems, ReferenceEqualityComparer.Instance);
        changes.SymmetricExceptWith(items);

        if (changes.Count > MaxIncrementalChanges)
        {
            VisibleItems.ReplaceAll(items);
        }
        else if (changes.Count > 0)
        {
            // Both lists follow the same overall order, so one merge pass turns the old into the new.
            var index = 0;

            foreach (var item in items)
            {
                while (index < VisibleItems.Count && !ReferenceEquals(VisibleItems[index], item) && _positions[VisibleItems[index]] < _positions[item])
                {
                    VisibleItems.RemoveAt(index);
                }

                if (index >= VisibleItems.Count || !ReferenceEquals(VisibleItems[index], item))
                {
                    VisibleItems.Insert(index, item);
                }

                index++;
            }

            while (VisibleItems.Count > index)
            {
                VisibleItems.RemoveAt(VisibleItems.Count - 1);
            }
        }

        ViewChanged?.Invoke(this, EventArgs.Empty);
    }

    public void RefreshSummary()
    {
        var attention = Rows.Where(r => r.NeedsAttention && r.IsManaged).ToList();
        var jobs = SyncEngine.JobsFor(attention.Select(r => r.Plan!), overwriteCustom: true);
        var missing = attention.Count(r => r.Status == ZoneStatus.Missing);
        var custom = attention.Count(r => r.Status == ZoneStatus.Custom);
        var managed = Rows.Count(r => r.IsManaged);

        PendingFiles = jobs.Count;
        UpdateButtonText = jobs.Count == 0 ? "Up to date" : $"Update {jobs.Count} file{(jobs.Count == 1 ? "" : "s")} ({Bytes.Format(jobs.Sum(j => j.File.Size))})";

        var parts = new List<string> { $"{Rows.Count} zones", $"{managed} managed" };

        if (attention.Count - missing - custom > 0)
        {
            parts.Add($"{attention.Count - missing - custom} with updates");
        }

        if (missing > 0)
        {
            parts.Add($"{missing} missing");
        }

        if (custom > 0)
        {
            parts.Add($"{custom} with local changes");
        }

        SummaryText = string.Join(" · ", parts);

        if (Filter == ZoneFilter.NeedsAttention)
        {
            RefreshView();
        }
    }

    private bool CanUpdate() => IsIdle && PendingFiles > 0;

    /// <summary>
    /// Download everything the managed zones need.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanUpdate))]
    private async Task UpdateAsync()
    {
        var plans = Rows.Where(r => r.IsManaged && r.Plan is { NeedsDownload: true }).Select(r => r.Plan!).ToList();
        var custom = plans.Where(p => p.Status == ZoneStatus.Custom).ToList();
        var overwriteCustom = false;

        if (custom.Count > 0)
        {
            var names = string.Join(", ", custom.Take(12).Select(p => p.Zone.ShortName)) + (custom.Count > 12 ? ", ..." : "");
            var backup = BackupBeforeOverwrite ? "They will be backed up first." : "Backups are turned off, so they will be lost.";
            var answer = await Ui.AskReplaceCustomAsync(
                "Replace custom meshes?",
                $"{custom.Count} zone(s) have local files that match no version on mqmesh.com, so they may be your own meshes:\n\n{names}\n\n"
                + $"Replace them too? {backup}");

            if (answer == ReplaceCustomAnswer.Cancel)
            {
                return;
            }

            overwriteCustom = answer == ReplaceCustomAnswer.Replace;
        }

        await DownloadAsync(SyncEngine.JobsFor(plans, overwriteCustom));
    }

    private bool CanRetry() => IsIdle && FailedCount > 0;

    [RelayCommand(CanExecute = nameof(CanRetry))]
    private Task RetryFailedAsync() => DownloadAsync(_failed);

    private bool CanCancel() => IsDownloading;

    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void Cancel()
    {
        _cancel?.Cancel();
        StatusText = "Cancelling...";
    }

    private async Task DownloadAsync(List<DownloadJob> jobs)
    {
        if (jobs.Count == 0)
        {
            StatusText = "Nothing to download.";
            return;
        }

        IsBusy = true;
        IsDownloading = true;
        _cancel = new CancellationTokenSource();

        var total = jobs.Sum(j => j.File.Size);
        ProgressMaximum = Math.Max(1, total);
        ProgressValue = 0;
        StatusText = $"Downloading {jobs.Count} file(s), {Bytes.Format(total)}...";

        var current = new Dictionary<DownloadJob, (long Bytes, long Size)>();
        var progress = new Progress<DownloadProgress>(p =>
        {
            ProgressValue = p.OverallBytes;

            if (p.State is JobState.Started or JobState.Progress)
            {
                current[p.Job] = (p.JobBytes, p.Job.File.Size);
            }
            else
            {
                current.Remove(p.Job);
            }

            var active = string.Join(", ", current.Take(3).Select(c => $"{c.Key.File.FileName} {(c.Value.Size == 0 ? 100 : c.Value.Bytes * 100 / c.Value.Size)}%"));
            ProgressText = $"{p.Completed + p.Failed}/{p.Total} files · {Bytes.Format(p.OverallBytes)} of {Bytes.Format(p.OverallTotal)}"
                + (active.Length > 0 ? " · " + active : "");
        });

        try
        {
            var report = await Task.Run(() => Engine.DownloadAsync(jobs, progress, _cancel.Token));

            _failed = report.Results.Where(r => !r.Success).Select(r => r.Job).ToList();
            FailedCount = _failed.Count;

            var touched = jobs.Select(j => j.ZoneShortName).ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var row in Rows.Where(r => touched.Contains(r.ShortName)))
            {
                row.Replan();
            }

            foreach (var group in Rows.Select(r => r.Group).Distinct())
            {
                group.Refresh();
            }

            RefreshSummary();

            var failures = report.Results.Where(r => !r.Success).ToList();
            StatusText = (report.Cancelled ? "Cancelled. " : "")
                + $"{report.Succeeded} file(s) downloaded"
                + (failures.Count > 0 ? $", {failures.Count} failed: {failures[0].Job.File.FileName}: {failures[0].Error}" : ".");
        }
        catch (Exception ex)
        {
            StatusText = "Download failed: " + ex.Message;
        }
        finally
        {
            IsDownloading = false;
            IsBusy = false;
            ProgressValue = 0;
            ProgressText = "";
            _cancel.Dispose();
            _cancel = null;
        }
    }

    /// <summary>
    /// Find community meshes for every zone.
    /// </summary>
    [RelayCommand(CanExecute = nameof(IsIdle))]
    private async Task CheckCommunityAsync()
    {
        IsBusy = true;
        StatusText = "Looking for community meshes...";

        try
        {
            var zones = await Task.Run(() => Engine.CheckAllCommunityAsync());

            foreach (var row in Rows)
            {
                row.RebuildOptions();
                row.Replan();
            }

            RefreshView();

            StatusText = zones.Count == 0
                ? "No zones have community meshes yet."
                : $"{zones.Count} zone(s) have community meshes. Pick one from a zone's Mesh list.";

            if (zones.Count > 0)
            {
                Filter = ZoneFilter.Community;
            }
        }
        catch (Exception ex)
        {
            StatusText = "Could not check community meshes: " + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(IsIdle))]
    private async Task ChooseMqFolderAsync()
    {
        var folder = await Ui.PickFolderAsync("Choose your MacroQuest folder", Directory.Exists(MqFolder) ? MqFolder : null);

        if (folder is null)
        {
            return;
        }

        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        Engine.Settings.MqFolder = string.Equals(folder, Engine.DefaultMqFolder, comparison) ? null : folder;
        SaveSettings();
        await RefreshAsync();
    }

    [RelayCommand]
    private void OpenMeshFolder()
    {
        Directory.CreateDirectory(MeshFolder);
        Ui.Open(MeshFolder);
    }

    [RelayCommand]
    private void OpenWebsite() => Ui.Open(WebsiteUrl);

    [RelayCommand]
    private void ReportBug() => Ui.Open(IssuesUrl);

    [RelayCommand]
    private Task ShowAboutAsync() => Ui.ShowMessageAsync(
        "About MQMesh Updater",
        $"MQMesh Updater {Version}\n\nKeeps your MQ2Nav meshes in step with mqmesh.com.\n\n{WebsiteUrl}\n\nSettings: {Engine.Paths.DataFolder}");
}
