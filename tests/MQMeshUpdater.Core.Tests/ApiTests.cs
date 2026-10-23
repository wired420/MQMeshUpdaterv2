using System.Net;
using MQMeshUpdater.Core.Api;

namespace MQMeshUpdater.Core.Tests;

public class JsonParsingTests
{
    [Fact]
    public void Manifest_parses_files_doors_and_community()
    {
        var manifest = Json.Deserialize<Manifest>(Samples.ManifestJson);

        Assert.Equal(2, manifest.Count);
        Assert.True(manifest.IncludesCommunity);

        var abysmal = manifest.Zones[1];
        Assert.Equal("abysmal", abysmal.ShortName);
        Assert.Equal(279, abysmal.EqZoneId);
        Assert.Equal("god", abysmal.Expansion);
        Assert.Equal(1743292, abysmal.Files.Mesh!.Size);
        Assert.Equal("abysmal_doors.json", abysmal.Files.Doors!.FileName);
        Assert.True(abysmal.Files.Mesh.SiteFile);
        Assert.Equal("https://mqmesh.com/api/v1/files/2/download", abysmal.Files.Mesh.DownloadUrl);

        var community = Assert.Single(abysmal.Community!);
        Assert.Equal("Someone", community.UploadedBy);
        Assert.Equal("Fixes the docks", community.Notes);
        Assert.False(community.SiteFile);
        Assert.Null(manifest.Zones[0].Files.Doors);
    }

    [Fact]
    public void Empty_files_array_reads_as_no_files()
    {
        var page = Json.Deserialize<ZonesPage>("""
            {"data":[{"short_name":"nofiles","name":"No Files","eq_zone_id":null,"expansion":"other","updated_at":null,"url":"x","files":[]}],
             "meta":{"total":1,"page":1,"last_page":1},"links":{"next":null,"prev":null}}
            """);

        var zone = Assert.Single(page.Data);
        Assert.Null(zone.Files.Mesh);
        Assert.Null(zone.Files.Doors);
        Assert.Null(zone.EqZoneId);
    }

    [Fact]
    public void Manifest_without_community_field_is_detected()
    {
        var manifest = Json.Deserialize<Manifest>("""
            {"generated_at":"2026-10-23T07:22:48+00:00","count":1,"zones":[{"short_name":"a","name":"A","expansion":"classic","files":{"mesh":{"id":1,"type":"mesh","version":1,"site_file":true,"file_name":"a.navmesh","size":1,"md5":"x","sha256":"y","created_at":"2020-01-01T00:00:00+00:00","download_url":"u"}}}]}
            """);

        Assert.False(manifest.IncludesCommunity);
        Assert.Null(manifest.Zones[0].Community);
    }

    [Fact]
    public void Zone_files_round_trip()
    {
        var manifest = Json.Deserialize<Manifest>(Samples.ManifestJson);
        var again = Json.Deserialize<Manifest>(Json.Serialize(manifest));

        Assert.Equal(manifest.Zones[1].Files.Doors!.Sha256, again.Zones[1].Files.Doors!.Sha256);
    }

    [Fact]
    public void Download_url_drops_the_api_prefix()
    {
        var file = new FileSummary { DownloadUrl = "https://mqmesh.com/api/v1/files/412/download" };

        Assert.Equal("https://mqmesh.com/files/412/download", FileUrls.ForDownload(file).ToString());
    }

    [Fact]
    public void Other_download_urls_are_used_as_given()
    {
        var file = new FileSummary { DownloadUrl = "https://example.com/x/412" };

        Assert.Equal("https://example.com/x/412", FileUrls.ForDownload(file).ToString());
    }
}

public class MeshApiClientTests
{
    [Fact]
    public async Task Sends_the_etag_back_exactly_and_reads_304()
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.NotModified));
        var api = new MeshApiClient(new HttpClient(handler));

        var result = await api.GetManifestAsync("\"013c9e8c48090dfd57db6514442f82c2\"");

        Assert.True(result.NotModified);
        Assert.Null(result.Json);

        var request = Assert.Single(handler.Requests);
        Assert.Equal("\"013c9e8c48090dfd57db6514442f82c2\"", request.Headers.GetValues("If-None-Match").Single());
        Assert.Equal("https://mqmesh.com/api/v1/manifest?include=community", request.RequestUri!.ToString());
    }

    [Fact]
    public async Task Returns_new_json_and_etag()
    {
        var handler = new FakeHandler(_ =>
        {
            var response = FakeHandler.Json(Samples.ManifestJson);
            response.Headers.TryAddWithoutValidation("ETag", "\"abc\"");
            return response;
        });
        var api = new MeshApiClient(new HttpClient(handler));

        var result = await api.GetManifestAsync(null);

        Assert.False(result.NotModified);
        Assert.Equal("\"abc\"", result.ETag);
        Assert.Equal(2, result.Manifest!.Zones.Count);
        Assert.False(handler.Requests[0].Headers.Contains("If-None-Match"));
    }

    [Fact]
    public async Task Waits_out_429_using_retry_after()
    {
        var calls = 0;
        var delays = new List<TimeSpan>();
        var handler = new FakeHandler(_ =>
        {
            if (calls++ == 0)
            {
                var limited = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
                limited.Headers.TryAddWithoutValidation("Retry-After", "7");
                return limited;
            }

            return FakeHandler.Json(Samples.ExpansionsJson);
        });
        var api = new MeshApiClient(new HttpClient(handler), delay: (d, _) => { delays.Add(d); return Task.CompletedTask; });

        var json = await api.GetExpansionsJsonAsync();

        Assert.Equal(3, MeshApiClient.ParseExpansions(json).Count);
        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal([TimeSpan.FromSeconds(7)], delays);
    }

    [Fact]
    public async Task Slows_down_when_close_to_the_limit()
    {
        var delays = new List<TimeSpan>();
        var handler = new FakeHandler(_ =>
        {
            var response = FakeHandler.Json(Samples.ExpansionsJson);
            response.Headers.TryAddWithoutValidation("X-RateLimit-Remaining", "1");
            return response;
        });
        var api = new MeshApiClient(new HttpClient(handler), delay: (d, _) => { delays.Add(d); return Task.CompletedTask; });

        await api.GetExpansionsJsonAsync();

        Assert.Equal(1, api.RateLimitRemaining);
        Assert.Single(delays);
    }

    [Fact]
    public async Task Pages_through_zones_with_community_versions()
    {
        var handler = new FakeHandler(request =>
        {
            var page = request.RequestUri!.Query.Contains("page=2") ? 2 : 1;
            var zone = page == 1 ? "a" : "b";
            return FakeHandler.Json($$"""
                {"data":[{"short_name":"{{zone}}","name":"{{zone}}","expansion":"classic","files":[]}],"meta":{"total":2,"page":{{page}},"last_page":2},"links":{ } }
                """);
        });
        var api = new MeshApiClient(new HttpClient(handler));

        var zones = await api.GetZonesWithCommunityAsync();

        Assert.Equal(["a", "b"], zones);
        Assert.All(handler.Requests, r => Assert.Contains("community=1", r.RequestUri!.Query));
    }

    [Fact]
    public async Task Zone_details_always_have_a_community_list()
    {
        var handler = new FakeHandler(_ => FakeHandler.Json("""{"short_name":"a","name":"A","expansion":"classic","files":[]}"""));
        var api = new MeshApiClient(new HttpClient(handler));

        var zone = await api.GetZoneAsync("a");

        Assert.NotNull(zone.Community);
        Assert.Empty(zone.Community);
    }
}
