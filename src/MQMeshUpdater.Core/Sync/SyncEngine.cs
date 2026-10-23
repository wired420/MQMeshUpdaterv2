using System.Collections.Concurrent;
using MQMeshUpdater.Core.Api;
using MQMeshUpdater.Core.Local;

namespace MQMeshUpdater.Core.Sync;

/// <summary>
/// Ties the catalog, local files, saved choices and downloads together, for
/// both the window and the command line.
/// </summary>
public sealed class SyncEngine
{
    private readonly ConcurrentDictionary<string, LocalFile> _localFiles = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, List<FileSummary>> _community = new(StringComparer.OrdinalIgnoreCase);
    private readonly Downloader _downloader;

    public SyncEngine(AppPaths paths, HttpClient http, MeshApiClient api, string defaultMqFolder)
    {
        Paths = paths;
        Api = api;
        DefaultMqFolder = defaultMqFolder;
        _downloader = new Downloader(http);

        Settings = AppSettings.Load(paths);
        Hashes = HashCache.Load(paths);
        Ledger = InstalledLedger.Load(paths);
    }

    public AppPaths Paths { get; }

    public MeshApiClient Api { get; }

    public string DefaultMqFolder { get; }

    public AppSettings Settings { get; }

    public HashCache Hashes { get; }

    public InstalledLedger Ledger { get; }

    public Catalog? Catalog { get; private set; }

    public string MqFolder => string.IsNullOrWhiteSpace(Settings.MqFolder) ? DefaultMqFolder : Settings.MqFolder;

    public string MeshFolder => AppPaths.ResolveMeshFolder(MqFolder, Settings.MeshFolder);

    public async Task<Catalog> LoadCatalogAsync(CancellationToken cancellationToken = default)
    {
        Catalog = await Catalog.LoadAsync(Api, Paths, cancellationToken).ConfigureAwait(false);

        foreach (var zone in Catalog.Manifest.Zones.Where(z => z.Community is not null))
        {
            _community[zone.ShortName] = zone.Community!;
        }

        return Catalog;
    }

    /// <summary>
    /// Hash the local files every zone could use. Cached hashes make this
    /// quick after the first run.
    /// </summary>
    public async Task ScanAsync(IProgress<(int Done, int Total)>? progress = null, CancellationToken cancellationToken = default)
    {
        var catalog = Catalog ?? throw new InvalidOperationException("Load the catalog first.");
        var folder = MeshFolder;
        _localFiles.Clear();

        var names = catalog.Manifest.Zones
            .SelectMany(z => new[] { z.Files.Mesh, z.Files.Doors })
            .Where(f => f is not null)
            .Select(f => f!.FileName)
            .Concat(Settings.Zones.Values.Select(c => c.Pinned?.FileName).OfType<string>())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        // Use the file's real name when only its case differs, as Wine would.
        var onDisk = CaseInsensitive.FilesByName(folder);
        var done = 0;

        await Parallel.ForEachAsync(names, new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = cancellationToken }, async (name, token) =>
        {
            var path = onDisk.TryGetValue(name, out var existing) ? existing : Path.Combine(folder, name);

            if (await Hashes.GetAsync(path, token).ConfigureAwait(false) is { } local)
            {
                _localFiles[path] = local;
            }

            progress?.Report((Interlocked.Increment(ref done), names.Count));
        }).ConfigureAwait(false);

        Hashes.Save(Paths);
    }

    /// <summary>
    /// The community versions known for a zone, or null if not fetched yet.
    /// </summary>
    public IReadOnlyList<FileSummary>? CommunityFor(string shortName) =>
        _community.TryGetValue(shortName, out var files) ? files : null;

    /// <summary>
    /// The zone's community versions, asking the site if the manifest did not include them.
    /// </summary>
    public async Task<IReadOnlyList<FileSummary>> EnsureCommunityAsync(string shortName, CancellationToken cancellationToken = default)
    {
        if (CommunityFor(shortName) is { } known)
        {
            return known;
        }

        var zone = await Api.GetZoneAsync(shortName, cancellationToken).ConfigureAwait(false);
        var files = zone.Community ?? [];
        _community[shortName] = files;

        return files;
    }

    /// <summary>
    /// Find every zone with community versions and fetch those. Returns the
    /// zones that have any.
    /// </summary>
    public async Task<IReadOnlyList<string>> CheckAllCommunityAsync(CancellationToken cancellationToken = default)
    {
        if (Catalog?.Manifest.IncludesCommunity == true)
        {
            return _community.Where(c => c.Value.Count > 0).Select(c => c.Key).ToList();
        }

        var names = await Api.GetZonesWithCommunityAsync(cancellationToken).ConfigureAwait(false);

        foreach (var name in names)
        {
            await EnsureCommunityAsync(name, cancellationToken).ConfigureAwait(false);
        }

        return names;
    }

    public ZonePlan Plan(ZoneSummary zone) =>
        ZonePlanner.Plan(zone, Settings.ChoiceFor(zone.ShortName), MeshFolder, _localFiles, Ledger, CommunityFor(zone.ShortName));

    public IReadOnlyList<ZonePlan> PlanAll() =>
        Catalog?.Manifest.Zones.Select(Plan).ToList() ?? [];

    /// <summary>
    /// The downloads that bring the given zones up to date. Files that look
    /// like the user's own are left out unless <paramref name="overwriteCustom"/> is set.
    /// </summary>
    public static List<DownloadJob> JobsFor(IEnumerable<ZonePlan> plans, bool overwriteCustom) =>
        plans
            .SelectMany(p => p.Files.Select(f => (p.Zone.ShortName, File: f)))
            .Where(x => x.File.NeedsDownload && (overwriteCustom || x.File.State != FileState.Custom))
            .Select(x => new DownloadJob(x.ShortName, x.File.File, x.File.LocalPath))
            .ToList();

    public async Task<DownloadReport> DownloadAsync(
        IReadOnlyList<DownloadJob> jobs,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var options = new DownloadOptions
        {
            Parallel = Settings.ParallelDownloads,
            BackupFolder = Settings.BackupBeforeOverwrite
                ? Path.Combine(MeshFolder, "backup", DateTime.Now.ToString("yyyyMMdd-HHmmss"))
                : null,
        };

        var report = await _downloader.RunAsync(jobs, options, progress, cancellationToken).ConfigureAwait(false);

        foreach (var result in report.Results.Where(r => r.Success))
        {
            Ledger.Record(result.Job.TargetPath, result.Job.File.Sha256);

            if (await Hashes.GetAsync(result.Job.TargetPath, CancellationToken.None).ConfigureAwait(false) is { } local)
            {
                _localFiles[result.Job.TargetPath] = local;
            }
        }

        Ledger.Save(Paths);
        Hashes.Save(Paths);

        return report;
    }
}
