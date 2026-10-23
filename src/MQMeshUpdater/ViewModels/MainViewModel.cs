using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Data;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using MQMeshUpdater.Core.Local;
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

    private readonly DispatcherTimer _saveTimer;
    private CancellationTokenSource? _cancel;
    private List<DownloadJob> _failed = [];

    public MainViewModel(SyncEngine engine)
    {
        Engine = engine;
        _backupBeforeOverwrite = engine.Settings.BackupBeforeOverwrite;

        ZonesView = CollectionViewSource.GetDefaultView(Rows);
        ZonesView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(ZoneRowViewModel.Group)));
        ZonesView.SortDescriptions.Add(new SortDescription("Group.Order", ListSortDirection.Ascending));
        ZonesView.SortDescriptions.Add(new SortDescription(nameof(ZoneRowViewModel.Name), ListSortDirection.Ascending));
        ZonesView.Filter = Matches;

        // Save choices shortly after the last change rather than on every click.
        _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _saveTimer.Tick += (_, _) => SaveSettings();
    }

    public SyncEngine Engine { get; }

    public RangeObservableCollection<ZoneRowViewModel> Rows { get; } = [];

    public ICollectionView ZonesView { get; }

    public IReadOnlyList<KeyValuePair<ZoneFilter, string>> Filters { get; } =
    [
        new(ZoneFilter.All, "All zones"),
        new(ZoneFilter.NeedsAttention, "Updates and missing"),
        new(ZoneFilter.Managed, "Managed zones"),
        new(ZoneFilter.NotManaged, "Not managed"),
        new(ZoneFilter.Community, "With community meshes"),
    ];

    public string MqFolder => Engine.MqFolder;

    public string MeshFolder => Engine.MeshFolder;

    public string Version => typeof(MainViewModel).Assembly.GetName().Version?.ToString(3) ?? "2.0.0";

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

    partial void OnSearchTextChanged(string value) => ZonesView.Refresh();

    partial void OnFilterChanged(ZoneFilter value) => ZonesView.Refresh();

    partial void OnBackupBeforeOverwriteChanged(bool value)
    {
        Engine.Settings.BackupBeforeOverwrite = value;
        SettingsChanged();
    }

    public void SettingsChanged()
    {
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    public void SaveSettings()
    {
        _saveTimer.Stop();
        Engine.Settings.Save(Engine.Paths);
    }

    private bool Matches(object item)
    {
        if (item is not ZoneRowViewModel row)
        {
            return false;
        }

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

        foreach (var zoneGroup in catalog.Groups())
        {
            var group = new ExpansionGroupViewModel(zoneGroup.Code, zoneGroup.Name, zoneGroup.Order);
            groups.Add(group);

            foreach (var zone in zoneGroup.Zones)
            {
                var row = new ZoneRowViewModel(this, zone, group);
                row.Replan();
                group.Rows.Add(row);
                rows.Add(row);
            }
        }

        Rows.ReplaceAll(rows);

        foreach (var group in groups)
        {
            group.Refresh();
        }

        RefreshSummary();
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
            ZonesView.Refresh();
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
            var answer = MessageBox.Show(
                $"{custom.Count} zone(s) have local files that match no version on mqmesh.com, so they may be your own meshes:\n\n{names}\n\n"
                + $"Replace them too? {backup}\n\nYes: replace them.  No: skip them, update the rest.  Cancel: do nothing.",
                "Replace custom meshes?",
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Warning,
                MessageBoxResult.No);

            if (answer == MessageBoxResult.Cancel)
            {
                return;
            }

            overwriteCustom = answer == MessageBoxResult.Yes;
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
        var dialog = new OpenFolderDialog
        {
            Title = "Choose your MacroQuest folder",
            InitialDirectory = Directory.Exists(MqFolder) ? MqFolder : null,
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        Engine.Settings.MqFolder = string.Equals(dialog.FolderName, Engine.DefaultMqFolder, StringComparison.OrdinalIgnoreCase) ? null : dialog.FolderName;
        SaveSettings();
        await RefreshAsync();
    }

    [RelayCommand]
    private void OpenMeshFolder()
    {
        Directory.CreateDirectory(MeshFolder);
        Open(MeshFolder);
    }

    [RelayCommand]
    private static void OpenWebsite() => Open(WebsiteUrl);

    [RelayCommand]
    private static void ReportBug() => Open(IssuesUrl);

    [RelayCommand]
    private void ShowAbout()
    {
        MessageBox.Show(
            $"MQMesh Updater {Version}\n\nKeeps your MQ2Nav meshes in step with mqmesh.com.\n\n{WebsiteUrl}\n\nSettings: {Engine.Paths.DataFolder}",
            "About MQMesh Updater",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private static void Open(string target) => Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
}
