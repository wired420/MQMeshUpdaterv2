using System.Net;
using System.Text;
using MQMeshUpdater.Core.Api;
using MQMeshUpdater.Core.Local;
using MQMeshUpdater.Core.Sync;

namespace MQMeshUpdater.Core.Tests;

public class SettingsTests
{
    [Fact]
    public void Choices_survive_a_save_and_load()
    {
        using var temp = new TempFolder();
        var paths = new AppPaths(temp.Path);
        var pinned = Samples.File(900, "abysmal", [1, 2, 3], version: 2, site: false, by: "Someone");

        var settings = new AppSettings { MqFolder = @"C:\MQ", BackupBeforeOverwrite = false };
        settings.SetChoice("abysmal", new ZoneChoice { Mode = ZoneMode.Version, Pinned = pinned, Doors = false });
        settings.SetChoice("akanon", new ZoneChoice { Mode = ZoneMode.Unmanaged });
        settings.Save(paths);

        var loaded = AppSettings.Load(paths);

        Assert.Equal(@"C:\MQ", loaded.MqFolder);
        Assert.False(loaded.BackupBeforeOverwrite);
        Assert.Equal(ZoneMode.Version, loaded.ChoiceFor("ABYSMAL").Mode);
        Assert.Equal(900, loaded.ChoiceFor("abysmal").Pinned!.Id);
        Assert.Equal("Someone", loaded.ChoiceFor("abysmal").Pinned!.UploadedBy);
        Assert.False(loaded.ChoiceFor("abysmal").Doors);
        Assert.Equal(ZoneMode.Unmanaged, loaded.ChoiceFor("akanon").Mode);
        Assert.True(loaded.ChoiceFor("poknowledge").IsDefault);
    }

    [Fact]
    public void Default_choices_are_not_stored()
    {
        var settings = new AppSettings();
        settings.SetChoice("akanon", new ZoneChoice { Mode = ZoneMode.Unmanaged });
        settings.SetChoice("akanon", ZoneChoice.Default);

        Assert.Empty(settings.Zones);
    }

    [Fact]
    public void Settings_file_uses_readable_names()
    {
        using var temp = new TempFolder();
        var paths = new AppPaths(temp.Path);
        var settings = new AppSettings();
        settings.SetChoice("akanon", new ZoneChoice { Mode = ZoneMode.Unmanaged });
        settings.Save(paths);

        var json = File.ReadAllText(paths.Settings);

        Assert.Contains("\"mode\": \"unmanaged\"", json);
        Assert.DoesNotContain("is_default", json);
    }

    [Fact]
    public void Broken_settings_file_falls_back_to_defaults()
    {
        using var temp = new TempFolder();
        var paths = new AppPaths(temp.Path);
        File.WriteAllText(paths.Settings, "{ not json");

        var settings = AppSettings.Load(paths);

        Assert.Empty(settings.Zones);
        Assert.True(settings.BackupBeforeOverwrite);
    }

    [Fact]
    public void Mesh_folder_prefers_an_existing_folder()
    {
        using var temp = new TempFolder();

        Assert.Equal(Path.Combine(temp.Path, "MQ2Nav"), AppPaths.ResolveMeshFolder(temp.Path, null));

        Directory.CreateDirectory(Path.Combine(temp.Path, "resources"));
        Assert.Equal(Path.Combine(temp.Path, "resources", "MQ2Nav"), AppPaths.ResolveMeshFolder(temp.Path, null));

        Directory.CreateDirectory(Path.Combine(temp.Path, "MQ2Nav"));
        Assert.Equal(Path.Combine(temp.Path, "MQ2Nav"), AppPaths.ResolveMeshFolder(temp.Path, null));

        Assert.Equal(@"D:\Meshes", AppPaths.ResolveMeshFolder(temp.Path, @"D:\Meshes"));
    }
}

public class HashTests
{
    [Fact]
    public async Task Hashes_are_lowercase_hex()
    {
        using var temp = new TempFolder();
        var path = temp.File("a.navmesh");
        await File.WriteAllTextAsync(path, "abc");

        var (md5, sha256) = await Hashing.HashFileAsync(path);

        Assert.Equal("900150983cd24fb0d6963f7d28e17f72", md5);
        Assert.Equal("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", sha256);
    }

    [Fact]
    public async Task Cache_is_reused_until_the_file_changes()
    {
        using var temp = new TempFolder();
        var paths = new AppPaths(temp.Path);
        var path = temp.File("a.navmesh");
        await File.WriteAllTextAsync(path, "abc");

        var cache = new HashCache();
        var first = await cache.GetAsync(path);
        cache.Save(paths);

        var loaded = HashCache.Load(paths);
        Assert.Equal(1, loaded.Count);
        Assert.Equal(first, await loaded.GetAsync(path));

        await File.WriteAllTextAsync(path, "abcd");
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(1));
        var changed = await loaded.GetAsync(path);

        Assert.NotEqual(first!.Sha256, changed!.Sha256);
        Assert.Equal(4, changed.Size);
    }

    [Fact]
    public async Task Missing_files_have_no_hash()
    {
        using var temp = new TempFolder();

        Assert.Null(await new HashCache().GetAsync(temp.File("nope.navmesh")));
    }
}

public class DownloaderTests
{
    private static readonly byte[] NewContents = Encoding.UTF8.GetBytes("new mesh contents");

    [Fact]
    public async Task Downloads_verifies_and_backs_up()
    {
        using var temp = new TempFolder();
        var target = temp.File("zone.navmesh");
        await File.WriteAllTextAsync(target, "old");

        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(NewContents) });
        var file = Samples.File(7, "zone", NewContents);
        var backup = temp.File("backup");

        var report = await new Downloader(new HttpClient(handler)).RunAsync(
            [new DownloadJob("zone", file, target)],
            new DownloadOptions { BackupFolder = backup });

        Assert.Equal(1, report.Succeeded);
        Assert.Equal(NewContents, await File.ReadAllBytesAsync(target));
        Assert.Equal("old", await File.ReadAllTextAsync(Path.Combine(backup, "zone.navmesh")));
        Assert.False(File.Exists(target + ".part"));
        Assert.Equal("https://mqmesh.com/files/7/download", handler.Requests[0].RequestUri!.ToString());
    }

    [Fact]
    public async Task Damaged_download_leaves_the_old_file()
    {
        using var temp = new TempFolder();
        var target = temp.File("zone.navmesh");
        await File.WriteAllTextAsync(target, "old");

        var damaged = Encoding.UTF8.GetBytes("new mesh contentX");
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(damaged) });

        var report = await new Downloader(new HttpClient(handler)).RunAsync(
            [new DownloadJob("zone", Samples.File(7, "zone", NewContents), target)],
            new DownloadOptions { Retries = 1 });

        var result = Assert.Single(report.Results);
        Assert.False(result.Success);
        Assert.Contains("SHA-256", result.Error);
        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal("old", await File.ReadAllTextAsync(target));
        Assert.False(File.Exists(target + ".part"));
    }

    [Fact]
    public async Task Wrong_size_fails()
    {
        using var temp = new TempFolder();
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([1, 2]) });

        var report = await new Downloader(new HttpClient(handler)).RunAsync(
            [new DownloadJob("zone", Samples.File(7, "zone", NewContents), temp.File("zone.navmesh"))],
            new DownloadOptions { Retries = 0 });

        Assert.Equal(1, report.Failed);
        Assert.False(File.Exists(temp.File("zone.navmesh")));
    }
}

public class CatalogTests
{
    [Fact]
    public async Task Uses_the_cached_manifest_on_304()
    {
        using var temp = new TempFolder();
        var paths = new AppPaths(temp.Path);
        var etag = "\"v1\"";

        var handler = new FakeHandler(request =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("expansions"))
            {
                return FakeHandler.Json(Samples.ExpansionsJson);
            }

            if (request.Headers.TryGetValues("If-None-Match", out var values) && values.Single() == etag)
            {
                return new HttpResponseMessage(HttpStatusCode.NotModified);
            }

            var response = FakeHandler.Json(Samples.ManifestJson);
            response.Headers.TryAddWithoutValidation("ETag", etag);
            return response;
        });
        var api = new MeshApiClient(new HttpClient(handler));

        var first = await Catalog.LoadAsync(api, paths);
        var second = await Catalog.LoadAsync(api, paths);

        Assert.Equal(CatalogSource.Fresh, first.Source);
        Assert.Equal(CatalogSource.NotModified, second.Source);
        Assert.Equal(2, second.Manifest.Zones.Count);
        Assert.Equal(etag, File.ReadAllText(paths.ManifestETag));
    }

    [Fact]
    public async Task Falls_back_to_the_cache_when_offline()
    {
        using var temp = new TempFolder();
        var paths = new AppPaths(temp.Path);
        File.WriteAllText(paths.Manifest, Samples.ManifestJson);
        File.WriteAllText(paths.Expansions, Samples.ExpansionsJson);

        var api = new MeshApiClient(new HttpClient(new FakeHandler(_ => throw new HttpRequestException("offline"))));

        var catalog = await Catalog.LoadAsync(api, paths);

        Assert.Equal(CatalogSource.Offline, catalog.Source);
        Assert.NotNull(catalog.Warning);
        Assert.Equal(3, catalog.Expansions.Count);
    }

    [Fact]
    public async Task Offline_with_no_cache_is_an_error()
    {
        using var temp = new TempFolder();
        var api = new MeshApiClient(new HttpClient(new FakeHandler(_ => throw new HttpRequestException("offline"))));

        await Assert.ThrowsAsync<InvalidOperationException>(() => Catalog.LoadAsync(api, new AppPaths(temp.Path)));
    }

    [Fact]
    public void Groups_sort_in_release_order_with_other_last()
    {
        var catalog = new Catalog(new Manifest(), MeshApiClient.ParseExpansions(Samples.ExpansionsJson), CatalogSource.Fresh);

        var order = new[] { "other", "brandnew", "kunark", "classic" }.OrderBy(catalog.SortOrder).ToList();

        Assert.Equal(["classic", "kunark", "brandnew", "other"], order);
        Assert.Equal("The Ruins of Kunark", catalog.ExpansionName("kunark"));
        Assert.Equal("brandnew", catalog.ExpansionName("brandnew"));
    }
}

public class GroupingTests
{
    private const string WithNewExpansion = """
        {"data":[{"code":"classic","name":"Classic","year":1999,"zones":1},{"code":"sor","name":"Shattering of Ro","year":2025,"zones":1},
                 {"code":"fof","name":"Favors of Fortune","year":2026,"zones":0},{"code":"other","name":"Other","year":null,"zones":1}]}
        """;

    private static Catalog CatalogWith(string expansionsJson, params string[] zoneExpansions)
    {
        var manifest = new Manifest
        {
            Zones = zoneExpansions.Select((code, i) => Samples.Zone($"zone{i}", Samples.File(i + 1, $"zone{i}", [1]), expansion: code)).ToList(),
        };

        return new Catalog(manifest, MeshApiClient.ParseExpansions(expansionsJson), CatalogSource.Fresh);
    }

    [Fact]
    public void Expansions_without_zones_get_no_group()
    {
        var groups = CatalogWith(WithNewExpansion, "classic", "sor", "other").Groups();

        Assert.Equal(["classic", "sor", "other"], groups.Select(g => g.Code));
        Assert.DoesNotContain(groups, g => g.Zones.Count == 0);
    }

    [Fact]
    public void A_new_expansion_with_zones_sorts_before_other()
    {
        var groups = CatalogWith(WithNewExpansion, "other", "fof", "classic").Groups();

        Assert.Equal(["classic", "fof", "other"], groups.Select(g => g.Code));
        Assert.Equal("Favors of Fortune", groups[1].Name);
    }

    [Fact]
    public void Unknown_codes_fall_into_other_without_losing_zones()
    {
        var groups = CatalogWith(Samples.ExpansionsJson, "classic", "notlisted", "other").Groups();

        Assert.Equal(["classic", "other"], groups.Select(g => g.Code));
        Assert.Equal(["zone1", "zone2"], groups[1].Zones.Select(z => z.ShortName));
        Assert.Equal(3, groups.Sum(g => g.Zones.Count));
    }

    [Fact]
    public void Without_an_expansion_list_codes_are_kept()
    {
        var catalog = new Catalog(
            new Manifest { Zones = [Samples.Zone("a", null, expansion: "kunark"), Samples.Zone("b", null, expansion: "classic")] },
            [],
            CatalogSource.Offline);

        Assert.Equal(["classic", "kunark"], catalog.Groups().Select(g => g.Code));
    }
}

public class CommandLineTests
{
    [Fact]
    public void Parses_update_options()
    {
        var options = CommandLineOptions.Parse(["--update", "--mq", @"C:\MQ", "--quiet", "--force"]);

        Assert.True(options.Update);
        Assert.True(options.Headless);
        Assert.True(options.Quiet);
        Assert.True(options.Force);
        Assert.Equal(@"C:\MQ", options.MqFolder);
        Assert.Null(options.Error);
    }

    [Fact]
    public void No_arguments_opens_the_window()
    {
        Assert.False(CommandLineOptions.Parse([]).Headless);
    }

    [Theory]
    [InlineData("--bogus")]
    [InlineData("--mq")]
    public void Bad_arguments_are_errors(string arg)
    {
        var options = CommandLineOptions.Parse([arg]);

        Assert.NotNull(options.Error);
        Assert.True(options.Headless);
    }

    [Fact]
    public void Formats_sizes()
    {
        Assert.Equal("512 B", Bytes.Format(512));
        Assert.Equal("1.5 KB", Bytes.Format(1536));
        Assert.Equal("8.3 MB", Bytes.Format(8700850));
    }
}
