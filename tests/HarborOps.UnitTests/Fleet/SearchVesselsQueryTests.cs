using HarborOps.Infrastructure.Persistence;
using HarborOps.Modules.Fleet.Application;
using HarborOps.Modules.Fleet.Contracts;
using HarborOps.Modules.Fleet.Domain;
using Microsoft.EntityFrameworkCore;
using FleetEntity = HarborOps.Modules.Fleet.Domain.Fleet;

namespace HarborOps.UnitTests.Fleet;

public sealed class SearchVesselsQueryTests
{
    [Fact]
    public async Task Search_returns_first_page_using_default_paging()
    {
        await using var dbContext = CreateDbContext();
        var vesselTypeId = await SeedVesselTypeAsync(dbContext, "Container Ship");

        for (var index = 1; index <= 26; index++)
        {
            await SeedVesselAsync(
                dbContext,
                vesselTypeId,
                $"Vessel {index:D2}",
                $"{1000000 + index}");
        }

        var query = new SearchVesselsQuery(dbContext);

        var response = await query.SearchAsync(
            new SearchVesselsRequest(),
            CancellationToken.None);

        Assert.Equal(1, response.PageNumber);
        Assert.Equal(25, response.PageSize);
        Assert.Equal(26, response.TotalCount);
        Assert.Equal(25, response.Items.Count);
        Assert.Equal("Vessel 01", response.Items[0].Name);
        Assert.Equal("Vessel 25", response.Items[^1].Name);
    }

    [Fact]
    public async Task Search_orders_by_name_then_id()
    {
        await using var dbContext = CreateDbContext();
        var vesselTypeId = await SeedVesselTypeAsync(dbContext, "Container Ship");
        var firstAurora = await SeedVesselAsync(dbContext, vesselTypeId, "Aurora", "1000001");
        var secondAurora = await SeedVesselAsync(dbContext, vesselTypeId, "Aurora", "1000002");
        await SeedVesselAsync(dbContext, vesselTypeId, "Borealis", "1000003");

        var query = new SearchVesselsQuery(dbContext);

        var response = await query.SearchAsync(
            new SearchVesselsRequest(),
            CancellationToken.None);

        var expectedAuroraIds = new[] { firstAurora.Id, secondAurora.Id }
            .OrderBy(id => id)
            .ToArray();

        Assert.Equal(3, response.Items.Count);
        Assert.Equal(expectedAuroraIds[0], response.Items[0].Id);
        Assert.Equal(expectedAuroraIds[1], response.Items[1].Id);
        Assert.Equal("Borealis", response.Items[2].Name);
    }

    [Fact]
    public async Task Search_matches_vessel_name()
    {
        await using var dbContext = CreateDbContext();
        var vesselTypeId = await SeedVesselTypeAsync(dbContext, "Container Ship");
        await SeedVesselAsync(dbContext, vesselTypeId, "North Star", "1000001");
        await SeedVesselAsync(dbContext, vesselTypeId, "Southern Cross", "1000002");

        var query = new SearchVesselsQuery(dbContext);

        var response = await query.SearchAsync(
            new SearchVesselsRequest { Search = "North" },
            CancellationToken.None);

        Assert.Equal(1, response.TotalCount);
        Assert.Equal("North Star", Assert.Single(response.Items).Name);
    }

    [Fact]
    public async Task Search_matches_imo_number()
    {
        await using var dbContext = CreateDbContext();
        var vesselTypeId = await SeedVesselTypeAsync(dbContext, "Container Ship");
        await SeedVesselAsync(dbContext, vesselTypeId, "North Star", "1000001");
        await SeedVesselAsync(dbContext, vesselTypeId, "Southern Cross", "9876543");

        var query = new SearchVesselsQuery(dbContext);

        var response = await query.SearchAsync(
            new SearchVesselsRequest { Search = "9876543" },
            CancellationToken.None);

        Assert.Equal(1, response.TotalCount);
        Assert.Equal("Southern Cross", Assert.Single(response.Items).Name);
    }

    [Fact]
    public async Task Search_projects_vessel_type_and_active_fleet()
    {
        await using var dbContext = CreateDbContext();
        var vesselTypeId = await SeedVesselTypeAsync(dbContext, "Container Ship");
        var vessel = await SeedVesselAsync(dbContext, vesselTypeId, "North Star", "1000001");
        var fleet = await SeedFleetAsync(dbContext, "Atlantic Fleet");
        await SeedActiveAssignmentAsync(dbContext, vessel.Id, fleet.Id);

        var query = new SearchVesselsQuery(dbContext);

        var response = await query.SearchAsync(
            new SearchVesselsRequest(),
            CancellationToken.None);

        var item = Assert.Single(response.Items);

        Assert.Equal(vessel.Id, item.Id);
        Assert.Equal("North Star", item.Name);
        Assert.Equal("1000001", item.ImoNumber);
        Assert.Equal("Malta", item.FlagState);
        Assert.Equal("Active", item.CurrentStatus);
        Assert.Null(item.LifecycleStage);
        Assert.Equal(vesselTypeId, item.VesselTypeId);
        Assert.Equal("Container Ship", item.VesselTypeName);
        Assert.Equal(fleet.Id, item.FleetId);
        Assert.Equal("Atlantic Fleet", item.FleetName);
    }

    [Fact]
    public async Task Search_by_fleet_excludes_ended_assignments()
    {
        await using var dbContext = CreateDbContext();
        var vesselTypeId = await SeedVesselTypeAsync(dbContext, "Container Ship");
        var assignedVessel = await SeedVesselAsync(dbContext, vesselTypeId, "North Star", "1000001");
        var releasedVessel = await SeedVesselAsync(dbContext, vesselTypeId, "Southern Cross", "1000002");
        var fleet = await SeedFleetAsync(dbContext, "Atlantic Fleet");

        await SeedActiveAssignmentAsync(dbContext, assignedVessel.Id, fleet.Id);
        await SeedEndedAssignmentAsync(dbContext, releasedVessel.Id, fleet.Id);

        var query = new SearchVesselsQuery(dbContext);

        var filtered = await query.SearchAsync(
            new SearchVesselsRequest { FleetId = fleet.Id },
            CancellationToken.None);

        Assert.Equal(1, filtered.TotalCount);
        Assert.Equal(assignedVessel.Id, Assert.Single(filtered.Items).Id);

        var unfiltered = await query.SearchAsync(
            new SearchVesselsRequest(),
            CancellationToken.None);

        var released = unfiltered.Items.Single(item => item.Id == releasedVessel.Id);

        Assert.Equal(2, unfiltered.TotalCount);
        Assert.Null(released.FleetId);
        Assert.Null(released.FleetName);
    }

    [Fact]
    public async Task Search_rejects_page_size_above_maximum()
    {
        await using var dbContext = CreateDbContext();
        var query = new SearchVesselsQuery(dbContext);

        var exception = await Assert.ThrowsAsync<SearchVesselsValidationException>(
            () => query.SearchAsync(
                new SearchVesselsRequest { PageSize = SearchVesselsValidator.MaxPageSize + 1 },
                CancellationToken.None));

        Assert.Contains(SearchVesselsValidator.PageSizeField, exception.Errors);
    }

    [Fact]
    public async Task Search_rejects_page_size_below_one()
    {
        await using var dbContext = CreateDbContext();
        var query = new SearchVesselsQuery(dbContext);

        var exception = await Assert.ThrowsAsync<SearchVesselsValidationException>(
            () => query.SearchAsync(
                new SearchVesselsRequest { PageSize = 0 },
                CancellationToken.None));

        Assert.Contains(SearchVesselsValidator.PageSizeField, exception.Errors);
    }

    [Fact]
    public async Task Search_accepts_maximum_page_size()
    {
        await using var dbContext = CreateDbContext();
        var vesselTypeId = await SeedVesselTypeAsync(dbContext, "Container Ship");
        await SeedVesselAsync(dbContext, vesselTypeId, "North Star", "1000001");

        var query = new SearchVesselsQuery(dbContext);

        var response = await query.SearchAsync(
            new SearchVesselsRequest { PageSize = SearchVesselsValidator.MaxPageSize },
            CancellationToken.None);

        Assert.Equal(SearchVesselsValidator.MaxPageSize, response.PageSize);
        Assert.Single(response.Items);
    }

    [Fact]
    public async Task Search_rejects_page_number_below_one()
    {
        await using var dbContext = CreateDbContext();
        var query = new SearchVesselsQuery(dbContext);

        var exception = await Assert.ThrowsAsync<SearchVesselsValidationException>(
            () => query.SearchAsync(
                new SearchVesselsRequest { PageNumber = 0 },
                CancellationToken.None));

        Assert.Contains(SearchVesselsValidator.PageNumberField, exception.Errors);
    }

    [Fact]
    public async Task Search_rejects_empty_fleet_identifier()
    {
        await using var dbContext = CreateDbContext();
        var query = new SearchVesselsQuery(dbContext);

        var exception = await Assert.ThrowsAsync<SearchVesselsValidationException>(
            () => query.SearchAsync(
                new SearchVesselsRequest { FleetId = Guid.Empty },
                CancellationToken.None));

        Assert.Contains(SearchVesselsValidator.FleetIdField, exception.Errors);
    }

    private static async Task<Guid> SeedVesselTypeAsync(HarborOpsDbContext dbContext, string name)
    {
        var vesselType = VesselType.Register(name, "CNTR", "Container carrying vessel");

        await dbContext.Set<VesselType>().AddAsync(vesselType);
        await dbContext.SaveChangesAsync();

        return vesselType.Id;
    }

    private static async Task<Vessel> SeedVesselAsync(
        HarborOpsDbContext dbContext,
        Guid vesselTypeId,
        string name,
        string imoNumber)
    {
        var vessel = Vessel.Register(
            Guid.NewGuid(),
            vesselTypeId,
            name,
            imoNumber,
            "Malta",
            "Active");

        await dbContext.Set<Vessel>().AddAsync(vessel);
        await dbContext.SaveChangesAsync();

        return vessel;
    }

    private static async Task<FleetEntity> SeedFleetAsync(HarborOpsDbContext dbContext, string name)
    {
        var fleet = FleetEntity.Register(name, "Fleet used by search tests.");

        await dbContext.Set<FleetEntity>().AddAsync(fleet);
        await dbContext.SaveChangesAsync();

        return fleet;
    }

    private static async Task SeedActiveAssignmentAsync(
        HarborOpsDbContext dbContext,
        Guid vesselId,
        Guid fleetId)
    {
        var assignment = FleetAssignment.Register(vesselId, fleetId);

        await dbContext.Set<FleetAssignment>().AddAsync(assignment);
        await dbContext.SaveChangesAsync();
    }

    private static async Task SeedEndedAssignmentAsync(
        HarborOpsDbContext dbContext,
        Guid vesselId,
        Guid fleetId)
    {
        var assignment = FleetAssignment.Register(vesselId, fleetId);
        assignment.End();

        await dbContext.Set<FleetAssignment>().AddAsync(assignment);
        await dbContext.SaveChangesAsync();
    }

    private static HarborOpsDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<HarborOpsDbContext>()
            .UseInMemoryDatabase($"HarborOpsUnitTests-{Guid.NewGuid()}")
            .Options;

        return new HarborOpsDbContext(options);
    }
}
