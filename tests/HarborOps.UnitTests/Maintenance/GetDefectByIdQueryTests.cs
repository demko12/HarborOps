using HarborOps.Infrastructure.Persistence;
using HarborOps.Modules.Maintenance.Application;
using HarborOps.Modules.Maintenance.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.UnitTests.Maintenance;

public sealed class GetDefectByIdQueryTests
{
    private static readonly DateTime ObservedAtUtc = new(2026, 7, 31, 8, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime ReportedAtUtc = new(2026, 8, 1, 8, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime TriagedAtUtc = new(2026, 8, 2, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime ConvertedAtUtc = new(2026, 8, 4, 9, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task GetByIdAsync_returns_every_scalar_field_for_a_reported_defect()
    {
        await using var dbContext = CreateDbContext();
        var vesselId = Guid.NewGuid();
        var equipmentId = Guid.NewGuid();
        var reportedByUserId = Guid.NewGuid();
        var defect = VesselDefect.Register(
            vesselId,
            equipmentId,
            "Hydraulic leak on crane boom",
            "Visible oil leak near the boom cylinder",
            DefectSeverity.Medium,
            ObservedAtUtc,
            reportedByUserId,
            ReportedAtUtc);
        await SeedAsync(dbContext, defect);

        var details = await CreateQuery(dbContext).GetByIdAsync(defect.Id, CancellationToken.None);

        Assert.Equal(defect.Id, details.Id);
        Assert.Equal(vesselId, details.VesselId);
        Assert.Equal(equipmentId, details.EquipmentId);
        Assert.Equal("Hydraulic leak on crane boom", details.Title);
        Assert.Equal("Visible oil leak near the boom cylinder", details.Description);
        Assert.Equal("Medium", details.ReportedSeverity);
        Assert.Equal(ObservedAtUtc, details.ObservedAtUtc);
        Assert.Equal("Reported", details.Status);
        Assert.Equal(reportedByUserId, details.ReportedByUserId);
        Assert.Equal(ReportedAtUtc, details.ReportedAtUtc);
        Assert.Null(details.ConfirmedSeverity);
        Assert.Null(details.Priority);
        Assert.Null(details.TriageNotes);
        Assert.Null(details.TriagedByUserId);
        Assert.Null(details.TriagedAtUtc);
        Assert.Null(details.RejectionReason);
        Assert.Null(details.RejectedByUserId);
        Assert.Null(details.RejectedAtUtc);
        Assert.Null(details.WorkOrderId);
        Assert.Null(details.ConvertedByUserId);
        Assert.Null(details.ConvertedAtUtc);
    }

    [Fact]
    public async Task GetByIdAsync_returns_the_triage_fields_for_a_triaged_defect()
    {
        await using var dbContext = CreateDbContext();
        var actorUserId = Guid.NewGuid();
        var defect = CreateReported(Guid.NewGuid());
        defect.Triage(DefectSeverity.Critical, WorkOrderPriority.Critical, "Escalated on inspection", actorUserId, TriagedAtUtc);
        await SeedAsync(dbContext, defect);

        var details = await CreateQuery(dbContext).GetByIdAsync(defect.Id, CancellationToken.None);

        Assert.Equal("Triaged", details.Status);
        Assert.Equal("Critical", details.ConfirmedSeverity);
        Assert.Equal("Critical", details.Priority);
        Assert.Equal("Escalated on inspection", details.TriageNotes);
        Assert.Equal(actorUserId, details.TriagedByUserId);
        Assert.Equal(TriagedAtUtc, details.TriagedAtUtc);
    }

    [Fact]
    public async Task GetByIdAsync_returns_the_linked_work_order_for_a_converted_defect()
    {
        await using var dbContext = CreateDbContext();
        var workOrderId = Guid.NewGuid();
        var convertedByUserId = Guid.NewGuid();
        var defect = CreateReported(Guid.NewGuid());
        defect.Triage(DefectSeverity.High, WorkOrderPriority.High, null, Guid.NewGuid(), TriagedAtUtc);
        defect.ConvertToWorkOrder(workOrderId, convertedByUserId, ConvertedAtUtc);
        await SeedAsync(dbContext, defect);

        var details = await CreateQuery(dbContext).GetByIdAsync(defect.Id, CancellationToken.None);

        Assert.Equal("Converted", details.Status);
        Assert.Equal(workOrderId, details.WorkOrderId);
        Assert.Equal(convertedByUserId, details.ConvertedByUserId);
        Assert.Equal(ConvertedAtUtc, details.ConvertedAtUtc);
    }

    [Fact]
    public async Task GetByIdAsync_maps_a_null_equipment_id_without_error()
    {
        await using var dbContext = CreateDbContext();
        var defect = CreateReported(Guid.NewGuid(), equipmentId: null);
        await SeedAsync(dbContext, defect);

        var details = await CreateQuery(dbContext).GetByIdAsync(defect.Id, CancellationToken.None);

        Assert.Null(details.EquipmentId);
    }

    [Fact]
    public async Task GetByIdAsync_rejects_an_unknown_defect()
    {
        await using var dbContext = CreateDbContext();
        var missingId = Guid.NewGuid();

        var exception = await Assert.ThrowsAsync<DefectNotFoundException>(
            () => CreateQuery(dbContext).GetByIdAsync(missingId, CancellationToken.None));

        Assert.Equal(missingId, exception.DefectId);
    }

    [Fact]
    public async Task GetByIdAsync_does_not_track_the_defect()
    {
        await using var dbContext = CreateDbContext();
        var defect = CreateReported(Guid.NewGuid());
        await SeedAsync(dbContext, defect);
        dbContext.ChangeTracker.Clear();

        await CreateQuery(dbContext).GetByIdAsync(defect.Id, CancellationToken.None);

        Assert.Empty(dbContext.ChangeTracker.Entries());
    }

    private static VesselDefect CreateReported(Guid vesselId, Guid? equipmentId = null)
    {
        return VesselDefect.Register(
            vesselId,
            equipmentId,
            "Hydraulic leak on crane boom",
            "Visible oil leak near the boom cylinder",
            DefectSeverity.Medium,
            ObservedAtUtc,
            Guid.NewGuid(),
            ReportedAtUtc);
    }

    private static async Task SeedAsync(HarborOpsDbContext dbContext, VesselDefect defect)
    {
        await dbContext.Set<VesselDefect>().AddAsync(defect);
        await dbContext.SaveChangesAsync();
    }

    private static GetDefectByIdQuery CreateQuery(HarborOpsDbContext dbContext)
    {
        return new GetDefectByIdQuery(dbContext);
    }

    private static HarborOpsDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<HarborOpsDbContext>()
            .UseInMemoryDatabase($"HarborOpsUnitTests-{Guid.NewGuid()}")
            .Options;

        return new HarborOpsDbContext(options);
    }
}
