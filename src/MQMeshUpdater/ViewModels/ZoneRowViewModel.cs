using System.Collections.ObjectModel;
using System.Net.Http;
using CommunityToolkit.Mvvm.ComponentModel;
using MQMeshUpdater.Core.Api;
using MQMeshUpdater.Core.Local;
using MQMeshUpdater.Core.Sync;

namespace MQMeshUpdater.ViewModels;

public enum MeshOptionKind
{
    Site,
    Version,
    Unmanaged,
}

/// <summary>
/// One entry in a zone's mesh dropdown.
/// </summary>
public sealed class MeshOption
{
    public required MeshOptionKind Kind { get; init; }

    public FileSummary? File { get; init; }

    public required string Label { get; init; }

    public string? ToolTip { get; init; }

    public static MeshOption Site(FileSummary? site) => new()
    {
        Kind = MeshOptionKind.Site,
        File = site,
        Label = "Site mesh (default)",
        ToolTip = site is null ? null : $"The mesh mqmesh.com recommends: version {site.Version}, {site.CreatedAt:yyyy-MM-dd}, {Bytes.Format(site.Size)}. Follows site updates.",
    };

    public static MeshOption Version(FileSummary file, bool unlisted = false) => new()
    {
        Kind = MeshOptionKind.Version,
        File = file,
        Label = $"v{file.Version} by {file.UploadedBy ?? "unknown"} · {file.CreatedAt:yyyy-MM-dd}" + (unlisted ? " (no longer listed)" : ""),
        ToolTip = string.IsNullOrWhiteSpace(file.Notes) ? $"Community version, {Bytes.Format(file.Size)}." : file.Notes,
    };

    public static readonly MeshOption Unmanaged = new()
    {
        Kind = MeshOptionKind.Unmanaged,
        Label = "Don't manage (keep my local file)",
        ToolTip = "The updater leaves this zone's files alone.",
    };
}

public sealed partial class ZoneRowViewModel : ObservableObject
{
    private readonly MainViewModel _owner;
    private bool _updating;

    public ZoneRowViewModel(MainViewModel owner, ZoneSummary zone, ExpansionGroupViewModel group)
    {
        _owner = owner;
        Zone = zone;
        Group = group;
        RebuildOptions();
    }

    public ZoneSummary Zone { get; }

    public ExpansionGroupViewModel Group { get; }

    public string Name => Zone.Name;

    public string ShortName => Zone.ShortName;

    public bool HasDoors => Zone.Files.Doors is not null;

    public ObservableCollection<MeshOption> MeshOptions { get; } = [];

    public bool CommunityLoaded => _owner.Engine.CommunityFor(ShortName) is not null;

    public int CommunityCount => _owner.Engine.CommunityFor(ShortName)?.Count ?? 0;

    public string CommunityText => CommunityLoaded
        ? CommunityCount == 0 ? "" : $"+{CommunityCount}"
        : "";

    [ObservableProperty]
    private ZonePlan? _plan;

    [ObservableProperty]
    private MeshOption? _selectedOption;

    [ObservableProperty]
    private bool _isLoadingCommunity;

    public ZoneChoice Choice => _owner.Engine.Settings.ChoiceFor(ShortName);

    public ZoneStatus Status => Plan?.Status ?? ZoneStatus.Unavailable;

    public string StatusText => Status switch
    {
        ZoneStatus.UpToDate => "Up to date",
        ZoneStatus.UpdateAvailable => "Update available",
        ZoneStatus.Missing => "Missing",
        ZoneStatus.Custom => "Local file differs",
        ZoneStatus.NotManaged => "Not managed",
        _ => "No file",
    };

    public string StatusToolTip => Status switch
    {
        ZoneStatus.Custom => "The local file matches no version on mqmesh.com, so it may be your own mesh. You'll be asked before it is replaced.",
        ZoneStatus.UpdateAvailable => $"Download {Bytes.Format(Plan?.DownloadBytes ?? 0)} to update.",
        ZoneStatus.Missing => $"Download {Bytes.Format(Plan?.DownloadBytes ?? 0)}.",
        _ => StatusText,
    };

    public long Size => ZonePlanner.TargetMesh(Zone, Choice)?.Size ?? Zone.Files.Mesh?.Size ?? 0;

    public string SizeText => Bytes.Format(Size);

    public bool NeedsAttention => Status is ZoneStatus.UpdateAvailable or ZoneStatus.Missing or ZoneStatus.Custom;

    /// <summary>Whether the updater manages this zone; unticking means "Don't manage".</summary>
    public bool IsManaged
    {
        get => Choice.Mode != ZoneMode.Unmanaged;
        set
        {
            if (value == IsManaged)
            {
                return;
            }

            var choice = Choice.Clone();
            choice.Mode = value ? (choice.Pinned is null ? ZoneMode.Site : ZoneMode.Version) : ZoneMode.Unmanaged;
            Apply(choice);
        }
    }

    public bool IncludeDoors
    {
        get => Choice.Doors;
        set
        {
            if (value == Choice.Doors)
            {
                return;
            }

            var choice = Choice.Clone();
            choice.Doors = value;
            Apply(choice);
        }
    }

    partial void OnSelectedOptionChanged(MeshOption? value)
    {
        // Ignore the dropdown resetting itself while its items are rebuilt.
        if (_updating || value is null)
        {
            return;
        }

        var choice = Choice.Clone();

        switch (value.Kind)
        {
            case MeshOptionKind.Site:
                choice.Mode = ZoneMode.Site;
                choice.Pinned = null;
                break;
            case MeshOptionKind.Version:
                choice.Mode = ZoneMode.Version;
                choice.Pinned = value.File;
                break;
            default:
                choice.Mode = ZoneMode.Unmanaged;
                break;
        }

        Apply(choice);
    }

    partial void OnPlanChanged(ZonePlan? value)
    {
        OnPropertyChanged(nameof(Status));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(StatusToolTip));
        OnPropertyChanged(nameof(NeedsAttention));
        OnPropertyChanged(nameof(Size));
        OnPropertyChanged(nameof(SizeText));
        OnPropertyChanged(nameof(IsManaged));
        OnPropertyChanged(nameof(IncludeDoors));
    }

    /// <summary>
    /// Fill the dropdown with the zone's community versions, asking the site if needed.
    /// </summary>
    public async Task EnsureCommunityAsync()
    {
        if (CommunityLoaded || IsLoadingCommunity)
        {
            return;
        }

        IsLoadingCommunity = true;

        try
        {
            await _owner.Engine.EnsureCommunityAsync(ShortName);
            RebuildOptions();
            Replan();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _owner.StatusText = $"Could not load community meshes for {ShortName}: {ex.Message}";
        }
        finally
        {
            IsLoadingCommunity = false;
        }
    }

    public void RebuildOptions()
    {
        _updating = true;

        try
        {
            var choice = Choice;
            MeshOptions.Clear();
            MeshOptions.Add(MeshOption.Site(Zone.Files.Mesh));

            var community = _owner.Engine.CommunityFor(ShortName) ?? [];

            foreach (var file in community.Where(f => f.IsMesh))
            {
                MeshOptions.Add(MeshOption.Version(file));
            }

            if (choice.Pinned is { } pinned && community.All(f => f.Id != pinned.Id))
            {
                MeshOptions.Add(MeshOption.Version(pinned, unlisted: CommunityLoaded));
            }

            MeshOptions.Add(MeshOption.Unmanaged);

            SelectedOption = choice.Mode switch
            {
                ZoneMode.Unmanaged => MeshOption.Unmanaged,
                ZoneMode.Version => MeshOptions.FirstOrDefault(o => o.Kind == MeshOptionKind.Version && o.File?.Id == choice.Pinned?.Id) ?? MeshOptions[0],
                _ => MeshOptions[0],
            };
        }
        finally
        {
            _updating = false;
        }

        OnPropertyChanged(nameof(CommunityLoaded));
        OnPropertyChanged(nameof(CommunityCount));
        OnPropertyChanged(nameof(CommunityText));
    }

    public void Replan() => Plan = _owner.Engine.Plan(Zone);

    private void Apply(ZoneChoice choice)
    {
        _owner.Engine.Settings.SetChoice(ShortName, choice);
        _owner.SettingsChanged();
        RebuildOptions();
        Replan();
        Group.Refresh();
        _owner.RefreshSummary();
    }
}
