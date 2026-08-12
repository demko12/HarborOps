using HarborOps.Api.Composition.Operations.FleetVessel;
using HarborOps.Modules.Fleet.Contracts;
using HarborOps.Modules.Maintenance.Contracts;

namespace HarborOps.UnitTests.Operations;

public sealed class FleetVesselOverviewServiceTests
{
    private static readonly Guid FleetId = Guid.NewGuid();
    private static readonly Guid NorthStarId = Guid.NewGuid();
    private static readonly Guid AuroraId = Guid.NewGuid();

    [Fact]
    public async Task Query_calls_fleet_and_maintenance_once_each()
    {
        var searchVessels = new FakeSearchVesselsQuery(CreatePage(NorthStar(), Aurora()));
        var maintenanceOverview = new FakeVesselMaintenanceOverviewQuery();
        var service = new FleetVesselOverviewService(searchVessels, maintenanceOverview);

        await service.QueryAsync(FleetId, 1, 25, null, CancellationToken.None);

        Assert.Equal(1, searchVessels.CallCount);
        Assert.Equal(1, maintenanceOverview.CallCount);
    }

    [Fact]
    public async Task Query_sends_only_vessel_ids_from_the_returned_page()
    {
        var searchVessels = new FakeSearchVesselsQuery(CreatePage(NorthStar()));
        var maintenanceOverview = new FakeVesselMaintenanceOverviewQuery();
        var service = new FleetVesselOverviewService(searchVessels, maintenanceOverview);

        await service.QueryAsync(FleetId, 2, 1, "star", CancellationToken.None);

        Assert.NotNull(maintenanceOverview.LastVesselIds);
        Assert.Equal([NorthStarId], maintenanceOverview.LastVesselIds);

        Assert.NotNull(searchVessels.LastRequest);
        Assert.Equal(FleetId, searchVessels.LastRequest.FleetId);
        Assert.Equal(2, searchVessels.LastRequest.PageNumber);
        Assert.Equal(1, searchVessels.LastRequest.PageSize);
        Assert.Equal("star", searchVessels.LastRequest.Search);
    }

    [Fact]
    public async Task Query_enriches_rows_and_preserves_fleet_order()
    {
        var nextDueUtc = new DateTime(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc);
        var searchVessels = new FakeSearchVesselsQuery(CreatePage(NorthStar(), Aurora()));
        var maintenanceOverview = new FakeVesselMaintenanceOverviewQuery(
            new VesselMaintenanceOverviewSummary(AuroraId, 5, 2, 1, nextDueUtc));
        var service = new FleetVesselOverviewService(searchVessels, maintenanceOverview);

        var page = await service.QueryAsync(FleetId, 1, 25, null, CancellationToken.None);

        Assert.Equal([NorthStarId, AuroraId], page.Items.Select(item => item.Id));
        Assert.Equal(1, page.PageNumber);
        Assert.Equal(25, page.PageSize);
        Assert.Equal(2, page.TotalCount);

        var aurora = page.Items[1];
        Assert.Equal("Aurora", aurora.Name);
        Assert.Equal("Tanker", aurora.VesselTypeName);
        Assert.Equal(5, aurora.OpenWorkOrderCount);
        Assert.Equal(2, aurora.OverdueWorkOrderCount);
        Assert.Equal(1, aurora.CriticalOpenWorkOrderCount);
        Assert.Equal(nextDueUtc, aurora.NextWorkOrderDueUtc);
    }

    [Fact]
    public async Task Query_returns_zero_counts_when_maintenance_summary_is_missing()
    {
        var searchVessels = new FakeSearchVesselsQuery(CreatePage(NorthStar()));
        var maintenanceOverview = new FakeVesselMaintenanceOverviewQuery();
        var service = new FleetVesselOverviewService(searchVessels, maintenanceOverview);

        var page = await service.QueryAsync(FleetId, 1, 25, null, CancellationToken.None);

        var vessel = Assert.Single(page.Items);
        Assert.Equal(0, vessel.OpenWorkOrderCount);
        Assert.Equal(0, vessel.OverdueWorkOrderCount);
        Assert.Equal(0, vessel.CriticalOpenWorkOrderCount);
        Assert.Null(vessel.NextWorkOrderDueUtc);
    }

    [Fact]
    public async Task Query_does_not_call_maintenance_for_an_empty_page()
    {
        var searchVessels = new FakeSearchVesselsQuery(
            new SearchVesselsResponse([], 3, 25, 0));
        var maintenanceOverview = new FakeVesselMaintenanceOverviewQuery();
        var service = new FleetVesselOverviewService(searchVessels, maintenanceOverview);

        var page = await service.QueryAsync(FleetId, 3, 25, null, CancellationToken.None);

        Assert.Equal(1, searchVessels.CallCount);
        Assert.Equal(0, maintenanceOverview.CallCount);
        Assert.Empty(page.Items);
        Assert.Equal(3, page.PageNumber);
        Assert.Equal(25, page.PageSize);
        Assert.Equal(0, page.TotalCount);
    }

    private static SearchVesselsResponse CreatePage(params VesselListItem[] vessels)
    {
        return new SearchVesselsResponse(vessels, 1, 25, vessels.Length);
    }

    private static VesselListItem NorthStar()
    {
        return new VesselListItem(
            NorthStarId,
            "North Star",
            "1234567",
            "Malta",
            "Active",
            Guid.NewGuid(),
            "Bulk Carrier",
            Guid.NewGuid(),
            35_000m,
            DateTime.UtcNow,
            "InService",
            FleetId,
            "Atlantic Fleet");
    }

    private static VesselListItem Aurora()
    {
        return new VesselListItem(
            AuroraId,
            "Aurora",
            "7654321",
            "Panama",
            "Active",
            Guid.NewGuid(),
            "Tanker",
            Guid.NewGuid(),
            22_500m,
            DateTime.UtcNow,
            null,
            FleetId,
            "Atlantic Fleet");
    }
}
