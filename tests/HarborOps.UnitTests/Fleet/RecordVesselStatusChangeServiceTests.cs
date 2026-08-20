using System;
using System.Collections.Generic;
using System.Text;
using HarborOps.Infrastructure.Persistence;
using HarborOps.Modules.Fleet.Application;
using HarborOps.Modules.Fleet.Contracts;
using HarborOps.Modules.Fleet.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.UnitTests.Fleet;

public sealed class RecordVesselStatusChangeServiceTests
{
    [Fact]
    public async Task RecordAsync_rejects_missing_status()
    {
        await using var dbContext = CreateDbContext();
        var service = new RecordVesselStatusChangeService(dbContext);

        var exception = await Assert.ThrowsAsync<RecordVesselStatusChangeValidationException>(
            () => service.RecordAsync(
                Guid.NewGuid(),
                "operator@harborops.test",
                new RecordVesselStatusChangeRequest { Status = " " },
                CancellationToken.None));

        Assert.Contains(RecordVesselStatusChangeValidator.StatusField, exception.Errors);
    }

    [Fact]
    public async Task RecordAsync_rejects_status_outside_the_vocabulary()
    {
        await using var dbContext = CreateDbContext();
        var service = new RecordVesselStatusChangeService(dbContext);

        var exception = await Assert.ThrowsAsync<RecordVesselStatusChangeValidationException>(
            () => service.RecordAsync(
                Guid.NewGuid(),
                "operator@harborops.test",
                new RecordVesselStatusChangeRequest { Status = "Sinking" },
                CancellationToken.None));

        Assert.Contains(RecordVesselStatusChangeValidator.StatusField, exception.Errors);
    }

    [Fact]
    public async Task RecordAsync_rejects_notes_exceeding_max_length()
    {
        await using var dbContext = CreateDbContext();
        var service = new RecordVesselStatusChangeService(dbContext);
        var notes = new string('n', VesselStatusHistory.NotesMaxLength + 1);

        var exception = await Assert.ThrowsAsync<RecordVesselStatusChangeValidationException>(
            () => service.RecordAsync(
                Guid.NewGuid(),
                "operator@harborops.test",
                new RecordVesselStatusChangeRequest { Status = "Active", Notes = notes },
                CancellationToken.None));

        Assert.Contains(RecordVesselStatusChangeValidator.NotesField, exception.Errors);
    }

    [Fact]
    public async Task RecordAsync_validates_before_checking_vessel_existence()
    {
        await using var dbContext = CreateDbContext();
        var service = new RecordVesselStatusChangeService(dbContext);

        await Assert.ThrowsAsync<RecordVesselStatusChangeValidationException>(
            () => service.RecordAsync(
                Guid.NewGuid(),
                "operator@harborops.test",
                new RecordVesselStatusChangeRequest { Status = string.Empty },
                CancellationToken.None));

        Assert.Equal(0, await dbContext.Set<VesselStatusHistory>().CountAsync());
    }

    [Fact]
    public async Task RecordAsync_throws_not_found_for_unknown_vessel()
    {
        await using var dbContext = CreateDbContext();
        var service = new RecordVesselStatusChangeService(dbContext);
        var vesselId = Guid.NewGuid();

        var exception = await Assert.ThrowsAsync<VesselNotFoundException>(
            () => service.RecordAsync(
                vesselId,
                "operator@harborops.test",
                new RecordVesselStatusChangeRequest { Status = "Active" },
                CancellationToken.None));

        Assert.Equal(vesselId, exception.VesselId);
    }

    [Fact]
    public async Task RecordAsync_throws_conflict_when_status_is_unchanged()
    {
        await using var dbContext = CreateDbContext();
        var vesselTypeId = await SeedVesselTypeAsync(dbContext);
        var vessel = await SeedVesselAsync(dbContext, vesselTypeId, "Active");
        var service = new RecordVesselStatusChangeService(dbContext);

        var exception = await Assert.ThrowsAsync<VesselStatusUnchangedException>(
            () => service.RecordAsync(
                vessel.Id,
                "operator@harborops.test",
                new RecordVesselStatusChangeRequest { Status = "Active" },
                CancellationToken.None));

        Assert.Equal(vessel.Id, exception.VesselId);
        Assert.Equal("Active", exception.Status);
    }

    [Fact]
    public async Task RecordAsync_updates_vessel_status_and_creates_one_history_row_atomically()
    {
        await using var dbContext = CreateDbContext();
        var vesselTypeId = await SeedVesselTypeAsync(dbContext);
        var vessel = await SeedVesselAsync(dbContext, vesselTypeId, "Active");
        var service = new RecordVesselStatusChangeService(dbContext);
        var startedUtc = DateTime.UtcNow;

        var response = await service.RecordAsync(
            vessel.Id,
            "operator@harborops.test",
            new RecordVesselStatusChangeRequest { Status = " At Port ", Notes = " Docked for inspection " },
            CancellationToken.None);

        var updatedVessel = await dbContext.Set<Vessel>().SingleAsync(v => v.Id == vessel.Id);
        var historyRows = await dbContext.Set<VesselStatusHistory>()
            .Where(entry => entry.VesselId == vessel.Id)
            .ToListAsync();

        Assert.Equal("At Port", updatedVessel.CurrentStatus);
        Assert.InRange(updatedVessel.UpdatedAtUtc, startedUtc, DateTime.UtcNow);

        var history = Assert.Single(historyRows);
        Assert.Equal("At Port", history.Status);
        Assert.Equal("operator@harborops.test", history.ChangedBy);
        Assert.Equal("Docked for inspection", history.Notes);
        Assert.InRange(history.ChangedAtUtc, startedUtc, DateTime.UtcNow);

        Assert.Equal(history.Id, response.Id);
        Assert.Equal(vessel.Id, response.VesselId);
        Assert.Equal("At Port", response.Status);
        Assert.Equal("operator@harborops.test", response.ChangedBy);
        Assert.Equal("Docked for inspection", response.Notes);
        Assert.Equal(history.ChangedAtUtc, response.ChangedAtUtc);
    }

    [Fact]
    public async Task RecordAsync_treats_blank_notes_as_null()
    {
        await using var dbContext = CreateDbContext();
        var vesselTypeId = await SeedVesselTypeAsync(dbContext);
        var vessel = await SeedVesselAsync(dbContext, vesselTypeId, "Active");
        var service = new RecordVesselStatusChangeService(dbContext);

        var response = await service.RecordAsync(
            vessel.Id,
            "operator@harborops.test",
            new RecordVesselStatusChangeRequest { Status = "En Route", Notes = "   " },
            CancellationToken.None);

        Assert.Null(response.Notes);

        var history = await dbContext.Set<VesselStatusHistory>().SingleAsync();
        Assert.Null(history.Notes);
    }

    private static async Task<Guid> SeedVesselTypeAsync(HarborOpsDbContext dbContext)
    {
        var vesselType = VesselType.Register("Container Ship", "CNTR", "Container carrying vessel");

        await dbContext.Set<VesselType>().AddAsync(vesselType);
        await dbContext.SaveChangesAsync();

        return vesselType.Id;
    }

    private static async Task<Vessel> SeedVesselAsync(
        HarborOpsDbContext dbContext,
        Guid vesselTypeId,
        string currentStatus)
    {
        var vessel = Vessel.Register(
            Guid.NewGuid(),
            vesselTypeId,
            "North Star",
            "1234567",
            "Malta",
            currentStatus);

        await dbContext.Set<Vessel>().AddAsync(vessel);
        await dbContext.SaveChangesAsync();

        return vessel;
    }

    private static HarborOpsDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<HarborOpsDbContext>()
            .UseInMemoryDatabase($"HarborOpsUnitTests-{Guid.NewGuid()}")
            .Options;

        return new HarborOpsDbContext(options);
    }
}
