using System.Text.Json.Serialization;

namespace MQMeshUpdater.Core.Api;

/// <summary>
/// One downloadable file on mqmesh.com: a zone's mesh or doors file.
/// </summary>
public sealed class FileSummary
{
    public long Id { get; set; }

    /// <summary>"mesh" or "doors".</summary>
    public string Type { get; set; } = "";

    public int Version { get; set; }

    /// <summary>Whether this is the zone's Site file, the site's default pick.</summary>
    public bool SiteFile { get; set; }

    /// <summary>The name MQ2Nav expects on disk, such as poknowledge.navmesh.</summary>
    public string FileName { get; set; } = "";

    public long Size { get; set; }

    public string Md5 { get; set; } = "";

    public string Sha256 { get; set; } = "";

    public DateTimeOffset CreatedAt { get; set; }

    public string DownloadUrl { get; set; } = "";

    /// <summary>Set on community versions only.</summary>
    public string? UploadedBy { get; set; }

    /// <summary>Set on community versions only.</summary>
    public string? Notes { get; set; }

    [JsonIgnore]
    public bool IsMesh => Type == FileTypes.Mesh;
}

public static class FileTypes
{
    public const string Mesh = "mesh";
    public const string Doors = "doors";
}

/// <summary>
/// A zone's Site files. The API sends an empty array instead of an object when
/// a zone has none, which <see cref="ZoneFilesConverter"/> reads as empty.
/// </summary>
[JsonConverter(typeof(ZoneFilesConverter))]
public sealed class ZoneFiles
{
    public FileSummary? Mesh { get; set; }

    public FileSummary? Doors { get; set; }

    public FileSummary? Get(string type) => type == FileTypes.Doors ? Doors : Mesh;
}

public class ZoneSummary
{
    public string ShortName { get; set; } = "";

    public string Name { get; set; } = "";

    public int? EqZoneId { get; set; }

    /// <summary>The expansion code, such as "kunark".</summary>
    public string Expansion { get; set; } = "";

    public DateTimeOffset? UpdatedAt { get; set; }

    public string Url { get; set; } = "";

    public ZoneFiles Files { get; set; } = new();

    /// <summary>
    /// Approved community versions. Null when the response did not include
    /// them (an older manifest), empty when the zone has none.
    /// </summary>
    public List<FileSummary>? Community { get; set; }
}

public sealed class Manifest
{
    public DateTimeOffset GeneratedAt { get; set; }

    public int Count { get; set; }

    public List<ZoneSummary> Zones { get; set; } = [];

    /// <summary>Whether every zone came with its community versions.</summary>
    [JsonIgnore]
    public bool IncludesCommunity => Zones.Count > 0 && Zones.All(z => z.Community is not null);
}

public sealed class Expansion
{
    public string Code { get; set; } = "";

    public string Name { get; set; } = "";

    public int? Year { get; set; }

    public int Zones { get; set; }
}

internal sealed class DataEnvelope<T>
{
    public List<T> Data { get; set; } = [];
}

internal sealed class ZonesPage
{
    public List<ZoneSummary> Data { get; set; } = [];

    public PageMeta Meta { get; set; } = new();

    public PageLinks Links { get; set; } = new();

    internal sealed class PageMeta
    {
        public int Total { get; set; }

        public int Page { get; set; }

        public int LastPage { get; set; }
    }

    internal sealed class PageLinks
    {
        public string? Next { get; set; }

        public string? Prev { get; set; }
    }
}
