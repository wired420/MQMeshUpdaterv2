using MQMeshUpdater.Core.Api;
using MQMeshUpdater.Core.Local;

namespace MQMeshUpdater.Core.Sync;

public enum CatalogSource
{
    /// <summary>Downloaded just now.</summary>
    Fresh,

    /// <summary>The server said nothing changed since the cached copy.</summary>
    NotModified,

    /// <summary>The server could not be reached; this is the cached copy.</summary>
    Offline,
}

public sealed record ZoneGroup(string Code, string Name, int Order, IReadOnlyList<ZoneSummary> Zones);

/// <summary>
/// The zones and expansions, from mqmesh.com or the local cache.
/// </summary>
public sealed class Catalog
{
    public Catalog(Manifest manifest, IReadOnlyList<Expansion> expansions, CatalogSource source, string? warning = null)
    {
        Manifest = manifest;
        Expansions = expansions;
        Source = source;
        Warning = warning;
    }

    public Manifest Manifest { get; }

    public IReadOnlyList<Expansion> Expansions { get; }

    public CatalogSource Source { get; }

    public string? Warning { get; }

    /// <summary>
    /// The expansion order for grouping: release order, unknown codes after
    /// the known ones, "other" last.
    /// </summary>
    public int SortOrder(string code)
    {
        if (code == "other")
        {
            return int.MaxValue;
        }

        for (var i = 0; i < Expansions.Count; i++)
        {
            if (Expansions[i].Code == code)
            {
                return i;
            }
        }

        return int.MaxValue - 1;
    }

    public string ExpansionName(string code) =>
        Expansions.FirstOrDefault(e => e.Code == code)?.Name ?? (code.Length > 0 ? code : "Unknown");

    /// <summary>
    /// The group a zone belongs in: its expansion, or "other" when the code is
    /// not in the expansion list. Without a list at all, codes are kept as they are.
    /// </summary>
    public string GroupCode(string code) =>
        Expansions.Count == 0 || Expansions.Any(e => e.Code == code) ? code : "other";

    /// <summary>
    /// The zones grouped by expansion in release order. Expansions without
    /// zones are left out.
    /// </summary>
    public IReadOnlyList<ZoneGroup> Groups() =>
        Manifest.Zones
            .GroupBy(z => GroupCode(z.Expansion))
            .Select(g => new ZoneGroup(g.Key, ExpansionName(g.Key), SortOrder(g.Key), g.ToList()))
            .OrderBy(g => g.Order)
            .ThenBy(g => g.Code, StringComparer.Ordinal)
            .ToList();

    /// <summary>
    /// Fetch the manifest and expansions, using the cache when nothing changed
    /// or the site cannot be reached.
    /// </summary>
    public static async Task<Catalog> LoadAsync(MeshApiClient api, AppPaths paths, CancellationToken cancellationToken = default)
    {
        paths.EnsureExists();

        var cachedJson = AtomicFile.TryReadAllText(paths.Manifest);
        var cachedEtag = cachedJson is null ? null : AtomicFile.TryReadAllText(paths.ManifestETag)?.Trim();

        Manifest manifest;
        CatalogSource source;
        string? warning = null;

        try
        {
            var response = await api.GetManifestAsync(cachedEtag, cancellationToken).ConfigureAwait(false);

            if (response.NotModified && cachedJson is not null)
            {
                manifest = Json.Deserialize<Manifest>(cachedJson);
                source = CatalogSource.NotModified;
            }
            else if (response.Json is not null)
            {
                manifest = response.Manifest!;
                source = CatalogSource.Fresh;
                AtomicFile.WriteAllText(paths.Manifest, response.Json);

                if (response.ETag is not null)
                {
                    AtomicFile.WriteAllText(paths.ManifestETag, response.ETag);
                }
            }
            else
            {
                // 304 with no cache to fall back on: ask again without the ETag.
                File.Delete(paths.ManifestETag);
                return await LoadAsync(api, paths, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            if (cachedJson is null)
            {
                throw new InvalidOperationException("Could not reach mqmesh.com, and there is no saved copy of the zone list yet.", ex);
            }

            manifest = Json.Deserialize<Manifest>(cachedJson);
            source = CatalogSource.Offline;
            warning = "Could not reach mqmesh.com, showing the zone list saved earlier.";
        }

        return new Catalog(manifest, await LoadExpansionsAsync(api, paths, source, cancellationToken).ConfigureAwait(false), source, warning);
    }

    private static async Task<List<Expansion>> LoadExpansionsAsync(MeshApiClient api, AppPaths paths, CatalogSource source, CancellationToken cancellationToken)
    {
        var cached = AtomicFile.TryReadAllText(paths.Expansions);

        if (source != CatalogSource.Offline || cached is null)
        {
            try
            {
                var json = await api.GetExpansionsJsonAsync(cancellationToken).ConfigureAwait(false);
                var expansions = MeshApiClient.ParseExpansions(json);
                AtomicFile.WriteAllText(paths.Expansions, json);

                return expansions;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
            {
                // Fall through to the cached copy, or group by code alone.
            }
        }

        return cached is null ? [] : MeshApiClient.ParseExpansions(cached);
    }
}
