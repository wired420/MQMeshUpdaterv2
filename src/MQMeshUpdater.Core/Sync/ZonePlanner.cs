using MQMeshUpdater.Core.Api;
using MQMeshUpdater.Core.Local;

namespace MQMeshUpdater.Core.Sync;

public enum FileState
{
    UpToDate,

    /// <summary>A known older or different version: safe to replace.</summary>
    Outdated,

    Missing,

    /// <summary>Matches no version we know of, probably the user's own file.</summary>
    Custom,
}

public enum ZoneStatus
{
    UpToDate,
    UpdateAvailable,
    Missing,
    Custom,
    NotManaged,

    /// <summary>The zone has no file to download.</summary>
    Unavailable,
}

/// <summary>
/// One file the zone should have, and how the local copy compares.
/// </summary>
public sealed record FileTarget(FileSummary File, string LocalPath, LocalFile? Local, FileState State)
{
    public bool NeedsDownload => State != FileState.UpToDate;
}

public sealed record ZonePlan(ZoneSummary Zone, ZoneChoice Choice, ZoneStatus Status, IReadOnlyList<FileTarget> Files)
{
    public long DownloadBytes => Files.Where(f => f.NeedsDownload).Sum(f => f.File.Size);

    public bool NeedsDownload => Status is ZoneStatus.UpdateAvailable or ZoneStatus.Missing or ZoneStatus.Custom;
}

/// <summary>
/// Works out what each zone should have on disk and whether it does.
/// </summary>
public static class ZonePlanner
{
    /// <summary>
    /// The mesh the zone should have: the pinned version, else the Site mesh.
    /// </summary>
    public static FileSummary? TargetMesh(ZoneSummary zone, ZoneChoice choice) => choice.Mode switch
    {
        ZoneMode.Unmanaged => null,
        ZoneMode.Version when choice.Pinned is not null => choice.Pinned,
        _ => zone.Files.Mesh,
    };

    public static FileSummary? TargetDoors(ZoneSummary zone, ZoneChoice choice) =>
        choice.Mode == ZoneMode.Unmanaged || !choice.Doors ? null : zone.Files.Doors;

    /// <param name="localFiles">Local files by full path; a missing key means the file does not exist.</param>
    /// <param name="ledger">The hashes of files this updater wrote.</param>
    public static ZonePlan Plan(
        ZoneSummary zone,
        ZoneChoice choice,
        string meshFolder,
        IReadOnlyDictionary<string, LocalFile> localFiles,
        InstalledLedger ledger,
        IEnumerable<FileSummary>? community = null)
    {
        if (choice.Mode == ZoneMode.Unmanaged)
        {
            return new ZonePlan(zone, choice, ZoneStatus.NotManaged, []);
        }

        var mesh = TargetMesh(zone, choice);

        if (mesh is null)
        {
            return new ZonePlan(zone, choice, ZoneStatus.Unavailable, []);
        }

        var known = KnownHashes(zone, choice, community);
        var files = new List<FileTarget> { Target(mesh, meshFolder, localFiles, ledger, known) };

        if (TargetDoors(zone, choice) is { } doors)
        {
            files.Add(Target(doors, meshFolder, localFiles, ledger, known));
        }

        var status = files.Any(f => f.State == FileState.Custom) ? ZoneStatus.Custom
            : files[0].State == FileState.Missing ? ZoneStatus.Missing
            : files.Any(f => f.NeedsDownload) ? ZoneStatus.UpdateAvailable
            : ZoneStatus.UpToDate;

        return new ZonePlan(zone, choice, status, files);
    }

    public static string LocalPath(string meshFolder, FileSummary file) => Path.Combine(meshFolder, file.FileName);

    private static FileTarget Target(
        FileSummary file,
        string meshFolder,
        IReadOnlyDictionary<string, LocalFile> localFiles,
        InstalledLedger ledger,
        HashSet<string> known)
    {
        var path = LocalPath(meshFolder, file);
        localFiles.TryGetValue(path, out var local);

        var state = local is null ? FileState.Missing
            : Hashing.Same(local.Sha256, file.Sha256) ? FileState.UpToDate
            : known.Contains(local.Sha256) || Hashing.Same(ledger.Get(path), local.Sha256) ? FileState.Outdated
            : FileState.Custom;

        // Replace an existing file under its own name, so no second copy appears
        // beside it on a case-sensitive file system.
        return new FileTarget(file, local?.Path ?? path, local, state);
    }

    private static HashSet<string> KnownHashes(ZoneSummary zone, ZoneChoice choice, IEnumerable<FileSummary>? community)
    {
        var hashes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var file in new[] { zone.Files.Mesh, zone.Files.Doors, choice.Pinned }
            .Concat(zone.Community ?? [])
            .Concat(community ?? []))
        {
            if (file is not null && file.Sha256.Length > 0)
            {
                hashes.Add(file.Sha256);
            }
        }

        return hashes;
    }
}
