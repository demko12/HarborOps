using HarborOps.Infrastructure.Persistence;
using HarborOps.Modules.Maintenance.Application;
using HarborOps.Modules.Maintenance.Contracts;
using HarborOps.Modules.Maintenance.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.UnitTests.Maintenance;

public sealed class RejectDefectServiceTests
{
    private static readonly DateTime ObservedAtUtc = new(2026, 7, 31, 8, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime ReportedAtUtc = new(2026, 8, 1, 8, 0, 0, DateTimeKind.Utc);
    private static readonly DateTimeOffset FixedNow = new(2026, 8, 3, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTime UtcNow = FixedNow.UtcDateTime;

    [Fact]
    public async Task RejectAsync_from_reported_succeeds()
    {
        await using var dbContext = CreateDbContext();
        var defect = await SeedAtStatusAsync(dbContext, DefectStatus.Reported);
        var actorUserId = Guid.NewGuid();

        var response = await CreateService(dbContext).RejectAsync(
            defect.Id,
            new RejectDefectRequest { Reason = "Duplicate report" },
            actorUserId,
            CancellationToken.None);

        var stored = await dbContext.Set<VesselDefect>().AsNoTracking().SingleAsync();

        Assert.Equal("Rejected", response.Status);
        Assert.Equal(DefectStatus.Rejected, stored.Status);
        Assert.Equal("Duplicate report", stored.RejectionReason);
        Assert.Equal(actorUserId, stored.RejectedByUserId);
        Assert.Equal(UtcNow, stored.RejectedAtUtc);
    }

    [Fact]
    public async Task RejectAsync_from_triaged_succeeds()
    {
        await using var dbContext = CreateDbContext();
        var defect = await SeedAtStatusAsync(dbContext, DefectStatus.Triaged);

        var response = await CreateService(dbContext).RejectAsync(
            defect.Id,
            new RejectDefectRequest { Reason = "Assessed as low priority" },
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.Equal("Rejected", response.Status);
    }

    [Fact]
    public async Task RejectAsync_rejects_a_missing_reason()
    {
        await using var dbContext = CreateDbContext();
        var defect = await SeedAtStatusAsync(dbContext, DefectStatus.Reported);

        var exception = await Assert.ThrowsAsync<RejectDefectValidationException>(
            () => CreateService(dbContext).RejectAsync(
                defect.Id,
                new RejectDefectRequest { Reason = null },
                Guid.NewGuid(),
                CancellationToken.None));

        Assert.Contains(RejectDefectValidator.ReasonField, exception.Errors);
    }

    [Fact]
    public async Task RejectAsync_rejects_an_unknown_defect()
    {
        await using var dbContext = CreateDbContext();
        var missingId = Guid.NewGuid();

        var exception = await Assert.ThrowsAsync<DefectNotFoundException>(
            () => CreateService(dbContext).RejectAsync(
                missingId,
                new RejectDefectRequest { Reason = "Duplicate report" },
                Guid.NewGuid(),
                CancellationToken.None));

        Assert.Equal(missingId, exception.DefectId);
    }

    [Theory]
    [InlineData(DefectStatus.Converted)]
    [InlineData(DefectStatus.Rejected)]
    public async Task RejectAsync_propagates_the_transition_guard_when_already_settled(DefectStatus status)
    {
        await using var dbContext = CreateDbContext();
        var defect = await SeedAtStatusAsync(dbContext, status);
        dbContext.ChangeTracker.Clear();

        var exception = await Assert.ThrowsAsync<DefectTransitionNotAllowedException>(
            () => CreateService(dbContext).RejectAsync(
                defect.Id,
                new RejectDefectRequest { Reason = "Too late" },
                Guid.NewGuid(),
                CancellationToken.None));

        Assert.Equal(status, exception.CurrentStatus);
        Assert.Equal(DefectStatus.Rejected, exception.RequestedStatus);
    }

    private static async Task<VesselDefect> SeedAtStatusAsync(HarborOpsDbContext dbContext, DefectStatus status)
    {
        var defect = VesselDefect.Register(
            Guid.NewGuid(),
            null,
            "Hydraulic leak on crane boom",
            "Visible oil leak near the boom cylinder",
            DefectSeverity.Medium,
            ObservedAtUtc,
            Guid.NewGuid(),
            ReportedAtUtc);

        switch (status)
        {
            case DefectStatus.Triaged:
                defect.Triage(DefectSeverity.High, WorkOrderPriority.High, null, Guid.NewGuid(), UtcNow);
                break;

            case DefectStatus.Converted:
                defect.Triage(DefectSeverity.High, WorkOrderPriority.High, null, Guid.NewGuid(), UtcNow);
                defect.ConvertToWorkOrder(Guid.NewGuid(), Guid.NewGuid(), UtcNow);
                break;

            case DefectStatus.Rejected:
                defect.Reject("Not applicable", Guid.NewGuid(), UtcNow);
                break;

            default:
                break;
        }

        await dbContext.Set<VesselDefect>().AddAsync(defect);
        await dbContext.SaveChangesAsync();

        return defect;
    }

    private static RejectDefectService CreateService(HarborOpsDbContext dbContext)
    {
        return new RejectDefectService(dbContext, new FixedTimeProvider(FixedNow));
    }

    private static HarborOpsDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<HarborOpsDbContext>()
            .UseInMemoryDatabase($"HarborOpsUnitTests-{Guid.NewGuid()}")
            .Options;

        return new HarborOpsDbContext(options);
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
