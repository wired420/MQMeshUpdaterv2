using System.Net;
using System.Security.Cryptography;
using System.Text;
using MQMeshUpdater.Core.Api;
using MQMeshUpdater.Core.Local;

namespace MQMeshUpdater.Core.Tests;

/// <summary>
/// An HTTP handler that answers from a function and records requests.
/// </summary>
internal sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
{
    public List<HttpRequestMessage> Requests { get; } = [];

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        return Task.FromResult(respond(request));
    }

    public static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
}

/// <summary>
/// A temporary folder deleted after the test.
/// </summary>
internal sealed class TempFolder : IDisposable
{
    public TempFolder()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "mqmesh-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public string File(string name) => System.IO.Path.Combine(Path, name);

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}

internal static class Samples
{
    public static FileSummary File(long id, string zone, byte[] contents, string type = "mesh", int version = 1, bool site = true, string? by = null) => new()
    {
        Id = id,
        Type = type,
        Version = version,
        SiteFile = site,
        FileName = type == "doors" ? $"{zone}_doors.json" : $"{zone}.navmesh",
        Size = contents.Length,
        Md5 = Convert.ToHexStringLower(MD5.HashData(contents)),
        Sha256 = Convert.ToHexStringLower(SHA256.HashData(contents)),
        CreatedAt = DateTimeOffset.Parse("2026-01-02T03:04:05Z"),
        DownloadUrl = $"https://mqmesh.com/api/v1/files/{id}/download",
        UploadedBy = by,
    };

    public static ZoneSummary Zone(string shortName, FileSummary? mesh, FileSummary? doors = null, string expansion = "classic") => new()
    {
        ShortName = shortName,
        Name = shortName,
        Expansion = expansion,
        Files = new ZoneFiles { Mesh = mesh, Doors = doors },
    };

    public static LocalFile Local(string path, byte[] contents) =>
        new(path, contents.Length, Convert.ToHexStringLower(MD5.HashData(contents)), Convert.ToHexStringLower(SHA256.HashData(contents)));

    public const string ManifestJson = """
        {"generated_at":"2026-10-23T07:27:31+00:00","count":2,"zones":[
          {"short_name":"aalishai","name":"Aalishai: Palace of Embers","eq_zone_id":819,"expansion":"tbl","updated_at":"2020-05-25T21:28:07+00:00","url":"https:\/\/mqmesh.com\/zones\/aalishai",
           "files":{"mesh":{"id":1,"type":"mesh","version":1,"site_file":true,"file_name":"aalishai.navmesh","size":5795510,"md5":"13cc11c68560055a61b71a25c895269e","sha256":"b4c8b4a5938340575f05ce27c8cf3d7bd0ec1f0b9d815f6b7767b2133179e13d","created_at":"2020-05-25T21:28:07+00:00","download_url":"https:\/\/mqmesh.com\/api\/v1\/files\/1\/download"}},
           "community":[]},
          {"short_name":"abysmal","name":"The Abysmal Sea","eq_zone_id":279,"expansion":"god","updated_at":"2019-01-18T14:10:47+00:00","url":"https:\/\/mqmesh.com\/zones\/abysmal",
           "files":{"mesh":{"id":2,"type":"mesh","version":1,"site_file":true,"file_name":"abysmal.navmesh","size":1743292,"md5":"5528e798ca13d258e387fe34d70f4375","sha256":"332fc3ad90ddd7810f0e2b626aa62e8aaaf7de87b0ed2cee8c9fd2237ed14d78","created_at":"2024-04-15T13:56:20+00:00","download_url":"https:\/\/mqmesh.com\/api\/v1\/files\/2\/download"},
                    "doors":{"id":575,"type":"doors","version":1,"site_file":true,"file_name":"abysmal_doors.json","size":25283,"md5":"10c7b5eef933e02aa696010d51d9ec62","sha256":"72e75190d91df739543b2d02665bfdade51e3dc52c99d72c7467b5bcaf05bd1d","created_at":"2019-01-18T14:10:47+00:00","download_url":"https:\/\/mqmesh.com\/api\/v1\/files\/575\/download"}},
           "community":[{"id":900,"type":"mesh","version":2,"site_file":false,"file_name":"abysmal.navmesh","size":10,"md5":"aa","sha256":"bb","created_at":"2026-10-01T00:00:00+00:00","download_url":"https:\/\/mqmesh.com\/api\/v1\/files\/900\/download","uploaded_by":"Someone","notes":"Fixes the docks"}]}
        ]}
        """;

    public const string ExpansionsJson = """
        {"data":[{"code":"classic","name":"Classic","year":1999,"zones":81},{"code":"kunark","name":"The Ruins of Kunark","year":2000,"zones":31},{"code":"other","name":"Other","year":null,"zones":32}]}
        """;
}
