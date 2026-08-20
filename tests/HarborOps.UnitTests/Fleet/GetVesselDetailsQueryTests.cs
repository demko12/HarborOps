using System;
using System.Collections.Generic;
using System.Text;
using HarborOps.Infrastructure.Persistence;
using HarborOps.Modules.Fleet.Application;
using HarborOps.Modules.Fleet.Contracts;
using HarborOps.Modules.Fleet.Domain;
using Microsoft.EntityFrameworkCore;
using FleetEntity = HarborOps.Modules.Fleet.Domain.Fleet;
namespace HarborOps.UnitTests.Fleet;

public sealed class GetVesselDetailsQueryTests
{
    [Fact]
    public async Task GetAsync_returns_null_for_unknown_vessel()
    {
        await using var dbContext = CreateDbContext();
        var query = new GetVesselDetailsQuery(dbContext);

        var response = await query.GetAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.Null(response);
    }

    [Fact]
    public async Task GetAsync_projects_fleet_owned_fields_and_specifications()
    {
        await using var dbContext = CreateDbContext();
        var vesselTypeId = await SeedVesselTypeAsync(dbContext, "Container Ship");
        var vessel = await SeedVesselAsync(dbContext, vesselTypeId, "North Star", "1234567");
        vessel.UpdateSpecifications(75000m, 45000m, 250.5m, 32.2m, 14.1m);
        await dbContext.SaveChangesAsync();

        var query = new GetVesselDetailsQuery(dbContext);

        var response = await query.GetAsync(vessel.Id, CancellationToken.None);

        Assert.NotNull(response);
        Assert.Equal(vessel.Id, response.VesselId);
        Assert.Equal(vessel.CompanyId, response.CompanyId);
        Assert.Equal("North Star", response.Name);
        Assert.Equal("1234567", response.ImoNumber);
        Assert.Equal("Malta", response.FlagState);
        Assert.Equal(vesselTypeId, response.VesselTypeId);
        Assert.Equal("Container Ship", response.VesselTypeName);
        Assert.Equal("Active", response.CurrentStatus);
        Assert.Equal(75000m, response.Dwt);
        Assert.Equal(45000m, response.Gt);
        Assert.Equal(250.5m, response.Loa);
        Assert.Equal(32.2m, response.Beam);
        Assert.Equal(14.1m, response.Draft);
        Assert.Null(response.Mmsi);
        Assert.Null(response.CallSign);
        Assert.Null(response.PortOfRegistry);
        Assert.Null(response.ClassificationSociety);
        Assert.Null(response.LifecycleStage);
        Assert.Null(response.FleetId);
        Assert.Null(response.FleetName);
        Assert.Empty(response.StatusHistory);
    }

    [Fact]
    public async Task GetAsync_returns_active_fleet_assignment()
    {
        await using var dbContext = CreateDbContext();
        var vesselTypeId = await SeedVesselTypeAsync(dbContext, "Container Ship");
        var vessel = await SeedVesselAsync(dbContext, vesselTypeId, "North Star", "1234567");
        var fleet = await SeedFleetAsync(dbContext, "Atlantic Fleet");
        await SeedActiveAssignmentAsync(dbContext, vessel.Id, fleet.Id);

        var query = new GetVesselDetailsQuery(dbContext);

        var response = await query.GetAsync(vessel.Id, CancellationToken.None);

        Assert.NotNull(response);
        Assert.Equal(fleet.Id, response.FleetId);
        Assert.Equal("Atlantic Fleet", response.FleetName);
    }

    [Fact]
    public async Task GetAsync_excludes_ended_fleet_assignments()
    {
        await using var dbContext = CreateDbContext();
        var vesselTypeId = await SeedVesselTypeAsync(dbContext, "Container Ship");
        var vessel = await SeedVesselAsync(dbContext, vesselTypeId, "North Star", "1234567");
        var fleet = await SeedFleetAsync(dbContext, "Atlantic Fleet");
        await SeedEndedAssignmentAsync(dbContext, vessel.Id, fleet.Id);

        var query = new GetVesselDetailsQuery(dbContext);

        var response = await query.GetAsync(vessel.Id, CancellationToken.None);

        Assert.NotNull(response);
        Assert.Null(response.FleetId);
        Assert.Null(response.FleetName);
    }

    [Fact]
    public async Task GetAsync_orders_status_history_by_changed_at_descending()
    {
        await using var dbContext = CreateDbContext();
        var vesselTypeId = await SeedVesselTypeAsync(dbContext, "Container Ship");
        var vessel = await SeedVesselAsync(dbContext, vesselTypeId, "North Star", "1234567");
        var service = new RecordVesselStatusChangeService(dbContext);

        await service.RecordAsync(
            vessel.Id,
            "first@harborops.test",
            new RecordVesselStatusChangeRequest { Status = "En Route" },
            CancellationToken.None);
        await service.RecordAsync(
            vessel.Id,
            "second@harborops.test",
            new RecordVesselStatusChangeRequest { Status = "At Port" },
            CancellationToken.None);
        await service.RecordAsync(
            vessel.Id,
            "third@harborops.test",
            new RecordVesselStatusChangeRequest { Status = "Maintenance" },
            CancellationToken.None);

        var query = new GetVesselDetailsQuery(dbContext);

        var response = await query.GetAsync(vessel.Id, CancellationToken.None);

        Assert.NotNull(response);
        Assert.Equal(3, response.StatusHistory.Count);
        Assert.Equal("Maintenance", response.StatusHistory[0].Status);
        Assert.Equal("At Port", response.StatusHistory[1].Status);
        Assert.Equal("En Route", response.StatusHistory[2].Status);
        Assert.True(response.StatusHistory[0].ChangedAtUtc >= response.StatusHistory[1].ChangedAtUtc);
        Assert.True(response.StatusHistory[1].ChangedAtUtc >= response.StatusHistory[2].ChangedAtUtc);
        Assert.Equal("Maintenance", response.CurrentStatus);
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
        string imoNumber,
        string currentStatus = "Active")
    {
        var vessel = Vessel.Register(
            Guid.NewGuid(),
            vesselTypeId,
            name,
            imoNumber,
            "Malta",
            currentStatus);

        await dbContext.Set<Vessel>().AddAsync(vessel);
        await dbContext.SaveChangesAsync();

        return vessel;
    }

    private static async Task<FleetEntity> SeedFleetAsync(HarborOpsDbContext dbContext, string name)
    {
        var fleet = FleetEntity.Register(name, "Fleet used by details tests.");

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
