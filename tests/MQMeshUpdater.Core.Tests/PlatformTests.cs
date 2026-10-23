using System.Text;
using MQMeshUpdater.Core.Api;
using MQMeshUpdater.Core.Local;
using MQMeshUpdater.Core.Sync;
using MQMeshUpdater.ViewModels;

namespace MQMeshUpdater.Core.Tests;

/// <summary>
/// Behaviour that must match on Windows and on case-sensitive Linux file systems.
/// </summary>
public class CaseInsensitiveTests
{
    [Fact]
    public void Mesh_folder_is_found_whatever_its_case()
    {
        using var temp = new TempFolder();
        var nav = Path.Combine(temp.Path, "Resources", "mq2nav");
        Directory.CreateDirectory(nav);

        Assert.Equal(nav, AppPaths.ResolveMeshFolder(temp.Path, null));
    }

    [Fact]
    public void Legacy_mesh_folder_is_found_whatever_its_case()
    {
        using var temp = new TempFolder();
        var nav = Path.Combine(temp.Path, "mq2nav");
        Directory.CreateDirectory(nav);

        Assert.Equal(nav, AppPaths.ResolveMeshFolder(temp.Path, null));
    }

    [Fact]
    public void New_mesh_folder_goes_under_an_existing_resources_folder()
    {
        using var temp = new TempFolder();
        Directory.CreateDirectory(Path.Combine(temp.Path, "RESOURCES"));

        Assert.Equal(Path.Combine(temp.Path, "RESOURCES", "MQ2Nav"), AppPaths.ResolveMeshFolder(temp.Path, null));
    }

    [Fact]
    public void Missing_parent_finds_nothing()
    {
        using var temp = new TempFolder();

        Assert.Null(CaseInsensitive.FindDirectory(temp.File("nope"), "MQ2Nav"));
        Assert.Empty(CaseInsensitive.FilesByName(temp.File("nope")));
    }

    [Fact]
    public void Files_are_listed_by_name_ignoring_case()
    {
        using var temp = new TempFolder();
        File.WriteAllText(temp.File("PoKnowledge.navmesh"), "x");

        var files = CaseInsensitive.FilesByName(temp.Path);

        Assert.Equal(temp.File("PoKnowledge.navmesh"), files["poknowledge.navmesh"]);
    }

    [Fact]
    public async Task A_file_whose_name_differs_only_by_case_is_updated_in_place()
    {
        using var temp = new TempFolder();
        var contents = Encoding.UTF8.GetBytes("mesh");
        var meshFolder = Path.Combine(temp.Path, "MQ2Nav");
        Directory.CreateDirectory(meshFolder);
        var existing = Path.Combine(meshFolder, "PoKnowledge.navmesh");
        File.WriteAllBytes(existing, contents);

        var zone = Samples.Zone("poknowledge", Samples.File(1, "poknowledge", contents));
        var engine = EngineWith(temp, new Manifest { Zones = [zone] });
        await engine.LoadCatalogAsync();
        await engine.ScanAsync();

        var plan = engine.Plan(zone);

        Assert.Equal(ZoneStatus.UpToDate, plan.Status);
        Assert.Equal(existing, plan.Files[0].LocalPath);
    }

    internal static SyncEngine EngineWith(TempFolder temp, Manifest manifest)
    {
        var manifestJson = Json.Serialize(manifest);
        var handler = new FakeHandler(request => request.RequestUri!.AbsolutePath.EndsWith("expansions")
            ? FakeHandler.Json(Samples.ExpansionsJson)
            : FakeHandler.Json(manifestJson));
        var http = new HttpClient(handler);

        return new SyncEngine(new AppPaths(temp.File("data")), http, new MeshApiClient(http), temp.Path);
    }
}

public class UsageTests
{
    [Fact]
    public void Usage_names_the_running_program_and_version()
    {
        Assert.Contains(UpdaterHost.Version, CommandLineOptions.Usage);
        Assert.Contains(Path.GetFileName(Environment.ProcessPath)!, CommandLineOptions.Usage);
    }

    [Fact]
    public void User_agent_names_the_platform()
    {
        using var http = UpdaterHost.CreateHttpClient();
        var agent = http.DefaultRequestHeaders.UserAgent.ToString();

        Assert.StartsWith($"MQMeshUpdater/{UpdaterHost.Version}", agent);
        Assert.Contains(OperatingSystem.IsWindows() ? "Windows" : "Linux", agent);
    }
}

public class ViewModelTests
{
    private sealed class NoUi : IUiServices
    {
        public Task<ReplaceCustomAnswer> AskReplaceCustomAsync(string title, string message) => Task.FromResult(ReplaceCustomAnswer.Cancel);

        public Task<string?> PickFolderAsync(string title, string? initialFolder) => Task.FromResult<string?>(null);

        public Task ShowMessageAsync(string title, string message) => Task.CompletedTask;

        public void Open(string target)
        {
        }
    }

    private static async Task<MainViewModel> LoadedViewModel(TempFolder temp)
    {
        var manifest = new Manifest
        {
            Zones =
            [
                Samples.Zone("qeynos", Samples.File(1, "qeynos", [1]), expansion: "classic"),
                Samples.Zone("akanon", Samples.File(2, "akanon", [2]), expansion: "classic"),
                Samples.Zone("kaesora", Samples.File(3, "kaesora", [3]), expansion: "kunark"),
            ],
        };

        var viewModel = new MainViewModel(CaseInsensitiveTests.EngineWith(temp, manifest), new NoUi());
        await viewModel.RefreshAsync();

        return viewModel;
    }

    private static List<string> Visible(MainViewModel viewModel) =>
        viewModel.VisibleItems.Select(i => i is ExpansionGroupViewModel g ? "#" + g.Code : ((ZoneRowViewModel)i).ShortName).ToList();

    [Fact]
    public async Task Visible_items_are_grouped_and_sorted_by_name()
    {
        using var temp = new TempFolder();
        var viewModel = await LoadedViewModel(temp);

        Assert.Equal(["#classic", "akanon", "qeynos", "#kunark", "kaesora"], Visible(viewModel));
    }

    [Fact]
    public async Task Search_hides_empty_groups_and_clearing_it_restores_them()
    {
        using var temp = new TempFolder();
        var viewModel = await LoadedViewModel(temp);
        var changed = 0;
        viewModel.ViewChanged += (_, _) => changed++;

        viewModel.SearchText = "kae";
        Assert.Equal(["#kunark", "kaesora"], Visible(viewModel));

        viewModel.SearchText = "";
        Assert.Equal(["#classic", "akanon", "qeynos", "#kunark", "kaesora"], Visible(viewModel));
        Assert.Equal(2, changed);
    }

    [Fact]
    public async Task Collapsed_groups_keep_their_heading()
    {
        using var temp = new TempFolder();
        var viewModel = await LoadedViewModel(temp);

        viewModel.Groups[0].IsExpanded = false;

        Assert.Equal(["#classic", "#kunark", "kaesora"], Visible(viewModel));
    }

    [Fact]
    public async Task Filter_and_selected_filter_stay_in_step()
    {
        using var temp = new TempFolder();
        var viewModel = await LoadedViewModel(temp);

        viewModel.SelectedFilter = viewModel.Filters.Single(f => f.Key == ZoneFilter.NotManaged);
        Assert.Equal(ZoneFilter.NotManaged, viewModel.Filter);
        Assert.Empty(viewModel.VisibleItems);

        viewModel.Filter = ZoneFilter.NeedsAttention;
        Assert.Equal(ZoneFilter.NeedsAttention, viewModel.SelectedFilter.Key);
        Assert.Equal(5, viewModel.VisibleItems.Count);
    }

    [Fact]
    public async Task Missing_zones_are_counted_for_the_update_button()
    {
        using var temp = new TempFolder();
        var viewModel = await LoadedViewModel(temp);

        Assert.Equal(3, viewModel.PendingFiles);
        Assert.StartsWith("Update 3 files", viewModel.UpdateButtonText);
        Assert.All(viewModel.Rows, r => Assert.Equal(ZoneStatus.Missing, r.Status));
    }
}
