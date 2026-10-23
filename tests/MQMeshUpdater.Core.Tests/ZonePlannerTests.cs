using MQMeshUpdater.Core.Api;
using MQMeshUpdater.Core.Local;
using MQMeshUpdater.Core.Sync;

namespace MQMeshUpdater.Core.Tests;

public class ZonePlannerTests
{
    private const string Folder = @"C:\MQ\resources\MQ2Nav";

    private static readonly byte[] SiteMesh = [1, 1, 1];
    private static readonly byte[] OldMesh = [2, 2, 2];
    private static readonly byte[] CommunityMesh = [3, 3, 3];
    private static readonly byte[] SiteDoors = [4, 4];
    private static readonly byte[] MyMesh = [9, 9, 9];

    private static readonly string MeshPath = Path.Combine(Folder, "zone.navmesh");
    private static readonly string DoorsPath = Path.Combine(Folder, "zone_doors.json");

    private static ZoneSummary Zone() =>
        Samples.Zone("zone", Samples.File(1, "zone", SiteMesh), Samples.File(2, "zone", SiteDoors, type: "doors"));

    private static ZonePlan Plan(ZoneChoice choice, InstalledLedger? ledger = null, IEnumerable<FileSummary>? community = null, params (string Path, byte[] Contents)[] local) =>
        ZonePlanner.Plan(Zone(), choice, Folder, local.ToDictionary(l => l.Path, l => Samples.Local(l.Path, l.Contents)), ledger ?? new InstalledLedger(), community);

    [Fact]
    public void Nothing_local_is_missing()
    {
        var plan = Plan(ZoneChoice.Default);

        Assert.Equal(ZoneStatus.Missing, plan.Status);
        Assert.Equal(2, plan.Files.Count);
        Assert.Equal(SiteMesh.Length + SiteDoors.Length, plan.DownloadBytes);
        Assert.Equal(MeshPath, plan.Files[0].LocalPath);
    }

    [Fact]
    public void Matching_files_are_up_to_date()
    {
        var plan = Plan(ZoneChoice.Default, local: [(MeshPath, SiteMesh), (DoorsPath, SiteDoors)]);

        Assert.Equal(ZoneStatus.UpToDate, plan.Status);
        Assert.False(plan.NeedsDownload);
    }

    [Fact]
    public void Missing_doors_alone_is_an_update()
    {
        var plan = Plan(ZoneChoice.Default, local: [(MeshPath, SiteMesh)]);

        Assert.Equal(ZoneStatus.UpdateAvailable, plan.Status);
        Assert.Equal(SiteDoors.Length, plan.DownloadBytes);
    }

    [Fact]
    public void Doors_can_be_left_out()
    {
        var plan = Plan(new ZoneChoice { Doors = false }, local: [(MeshPath, SiteMesh)]);

        Assert.Equal(ZoneStatus.UpToDate, plan.Status);
        Assert.Single(plan.Files);
    }

    [Fact]
    public void A_file_we_wrote_earlier_is_safe_to_update()
    {
        var ledger = new InstalledLedger();
        ledger.Record(MeshPath, Samples.Local(MeshPath, OldMesh).Sha256);

        var plan = Plan(ZoneChoice.Default, ledger, local: [(MeshPath, OldMesh), (DoorsPath, SiteDoors)]);

        Assert.Equal(ZoneStatus.UpdateAvailable, plan.Status);
        Assert.Equal(FileState.Outdated, plan.Files[0].State);
    }

    [Fact]
    public void An_unknown_local_file_is_custom()
    {
        var plan = Plan(ZoneChoice.Default, local: [(MeshPath, MyMesh), (DoorsPath, SiteDoors)]);

        Assert.Equal(ZoneStatus.Custom, plan.Status);
        Assert.Equal(FileState.Custom, plan.Files[0].State);
    }

    [Fact]
    public void A_known_community_version_is_not_custom()
    {
        var community = Samples.File(50, "zone", CommunityMesh, version: 2, site: false, by: "Someone");

        var plan = Plan(ZoneChoice.Default, community: [community], local: [(MeshPath, CommunityMesh), (DoorsPath, SiteDoors)]);

        Assert.Equal(ZoneStatus.UpdateAvailable, plan.Status);
    }

    [Fact]
    public void A_pinned_community_version_is_the_target()
    {
        var community = Samples.File(50, "zone", CommunityMesh, version: 2, site: false, by: "Someone");
        var choice = new ZoneChoice { Mode = ZoneMode.Version, Pinned = community };

        var upToDate = Plan(choice, local: [(MeshPath, CommunityMesh), (DoorsPath, SiteDoors)]);
        var onSite = Plan(choice, local: [(MeshPath, SiteMesh), (DoorsPath, SiteDoors)]);

        Assert.Equal(ZoneStatus.UpToDate, upToDate.Status);
        Assert.Equal(ZoneStatus.UpdateAvailable, onSite.Status);
        Assert.Equal(50, onSite.Files[0].File.Id);
    }

    [Fact]
    public void Unmanaged_zones_need_nothing()
    {
        var plan = Plan(new ZoneChoice { Mode = ZoneMode.Unmanaged }, local: [(MeshPath, MyMesh)]);

        Assert.Equal(ZoneStatus.NotManaged, plan.Status);
        Assert.Empty(plan.Files);
        Assert.Empty(SyncEngine.JobsFor([plan], overwriteCustom: true));
    }

    [Fact]
    public void Zones_without_a_mesh_are_unavailable()
    {
        var plan = ZonePlanner.Plan(Samples.Zone("empty", null), ZoneChoice.Default, Folder, new Dictionary<string, LocalFile>(), new InstalledLedger());

        Assert.Equal(ZoneStatus.Unavailable, plan.Status);
    }

    [Fact]
    public void Custom_files_are_skipped_unless_asked()
    {
        var custom = Plan(ZoneChoice.Default, local: [(MeshPath, MyMesh)]);
        var missing = ZonePlanner.Plan(Samples.Zone("other", Samples.File(3, "other", SiteMesh)), ZoneChoice.Default, Folder, new Dictionary<string, LocalFile>(), new InstalledLedger());

        var safe = SyncEngine.JobsFor([custom, missing], overwriteCustom: false);
        var all = SyncEngine.JobsFor([custom, missing], overwriteCustom: true);

        // The custom zone's missing doors file is still safe to add.
        Assert.Equal(["zone_doors.json", "other.navmesh"], safe.Select(j => j.File.FileName));
        Assert.Equal(3, all.Count);
    }
}
