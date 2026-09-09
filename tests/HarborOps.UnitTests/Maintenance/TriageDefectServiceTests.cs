using HarborOps.Infrastructure.Persistence;
using HarborOps.Modules.Maintenance.Application;
using HarborOps.Modules.Maintenance.Contracts;
using HarborOps.Modules.Maintenance.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.UnitTests.Maintenance;

public sealed class TriageDefectServiceTests
{
    private static readonly DateTime ObservedAtUtc = new(2026, 7, 31, 8, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime ReportedAtUtc = new(2026, 8, 1, 8, 0, 0, DateTimeKind.Utc);
    private static readonly DateTimeOffset FixedNow = new(2026, 8, 2, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTime UtcNow = FixedNow.UtcDateTime;

    [Fact]
    public async Task TriageAsync_moves_a_reported_defect_to_triaged_and_records_actor_and_time()
    {
        await using var dbContext = CreateDbContext();
        var defect = await SeedReportedAsync(dbContext);
        var tokenBeforeTriage = defect.ConcurrencyToken;
        var actorUserId = Guid.NewGuid();
        var request = new TriageDefectRequest
        {
            ConfirmedSeverity = "High",
            Priority = "High",
            Notes = "Confirmed active leak, needs urgent repair",
        };

        var response = await CreateService(dbContext).TriageAsync(defect.Id, request, actorUserId, CancellationToken.None);

        var stored = await dbContext.Set<VesselDefect>().AsNoTracking().SingleAsync();

        Assert.Equal("Triaged", response.Status);
        Assert.Equal("High", response.ConfirmedSeverity);
        Assert.Equal("High", response.Priority);
        Assert.Equal(DefectStatus.Triaged, stored.Status);
        Assert.Equal(DefectSeverity.High, stored.ConfirmedSeverity);
        Assert.Equal(WorkOrderPriority.High, stored.Priority);
        Assert.Equal("Confirmed active leak, needs urgent repair", stored.TriageNotes);
        Assert.Equal(actorUserId, stored.TriagedByUserId);
        Assert.Equal(UtcNow, stored.TriagedAtUtc);
        Assert.NotEqual(tokenBeforeTriage, stored.ConcurrencyToken);
    }

    [Fact]
    public async Task TriageAsync_rejects_an_unknown_defect()
    {
        await using var dbContext = CreateDbContext();
        var missingId = Guid.NewGuid();

        var exception = await Assert.ThrowsAsync<DefectNotFoundException>(
            () => CreateService(dbContext).TriageAsync(missingId, ValidRequest, Guid.NewGuid(), CancellationToken.None));

        Assert.Equal(missingId, exception.DefectId);
    }

    [Theory]
    [InlineData(DefectStatus.Triaged)]
    [InlineData(DefectStatus.Converted)]
    [InlineData(DefectStatus.Rejected)]
    public async Task TriageAsync_propagates_the_transition_guard_when_not_reported(DefectStatus status)
    {
        await using var dbContext = CreateDbContext();
        var defect = await SeedAtStatusAsync(dbContext, status);
        dbContext.ChangeTracker.Clear();

        var exception = await Assert.ThrowsAsync<DefectTransitionNotAllowedException>(
            () => CreateService(dbContext).TriageAsync(defect.Id, ValidRequest, Guid.NewGuid(), CancellationToken.None));

        Assert.Equal(defect.Id, exception.DefectId);
        Assert.Equal(status, exception.CurrentStatus);
        Assert.Equal(DefectStatus.Triaged, exception.RequestedStatus);
    }

    [Fact]
    public async Task TriageAsync_rejects_invalid_input()
    {
        await using var dbContext = CreateDbContext();
        var defect = await SeedReportedAsync(dbContext);
        var request = ValidRequest with { Priority = null };

        var exception = await Assert.ThrowsAsync<TriageDefectValidationException>(
            () => CreateService(dbContext).TriageAsync(defect.Id, request, Guid.NewGuid(), CancellationToken.None));

        Assert.Contains(TriageDefectValidator.PriorityField, exception.Errors);
    }

    // Investigated before writing (mirroring DocumentLifecycleServiceReviewTests' own note):
    // EF Core's InMemory provider does evaluate a plain application-assigned IsConcurrencyToken()
    // property against the original tracked value at SaveChanges time, so this scenario - two
    // separate DbContext instances over the same in-memory database, one saving first - genuinely
    // reproduces DbUpdateConcurrencyException without needing a real SQL Server row-version column.
    [Fact]
    public async Task Concurrent_triage_saves_against_the_same_defect_throw_DbUpdateConcurrencyException()
    {
        var databaseName = $"HarborOpsUnitTests-{Guid.NewGuid()}";
        Guid defectId;

        await using (var seedContext = CreateDbContext(databaseName))
        {
            var defect = await SeedReportedAsync(seedContext);
            defectId = defect.Id;
        }

        await using var firstContext = CreateDbContext(databaseName);
        await using var secondContext = CreateDbContext(databaseName);

        var firstDefect = await firstContext.Set<VesselDefect>().SingleAsync(d => d.Id == defectId);
        var secondDefect = await secondContext.Set<VesselDefect>().SingleAsync(d => d.Id == defectId);

        firstDefect.Triage(DefectSeverity.High, WorkOrderPriority.High, "First triage", Guid.NewGuid(), UtcNow);
        await firstContext.SaveChangesAsync();

        secondDefect.Triage(DefectSeverity.Critical, WorkOrderPriority.Critical, "Second triage", Guid.NewGuid(), UtcNow);

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => secondContext.SaveChangesAsync());
    }

    private static TriageDefectRequest ValidRequest => new()
    {
        ConfirmedSeverity = "High",
        Priority = "High",
        Notes = "Confirmed active leak, needs urgent repair",
    };

    private static async Task<VesselDefect> SeedReportedAsync(HarborOpsDbContext dbContext)
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

        await dbContext.Set<VesselDefect>().AddAsync(defect);
        await dbContext.SaveChangesAsync();

        return defect;
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

    private static TriageDefectService CreateService(HarborOpsDbContext dbContext)
    {
        return new TriageDefectService(dbContext, new FixedTimeProvider(FixedNow));
    }

    private static HarborOpsDbContext CreateDbContext(string? databaseName = null)
    {
        var options = new DbContextOptionsBuilder<HarborOpsDbContext>()
            .UseInMemoryDatabase(databaseName ?? $"HarborOpsUnitTests-{Guid.NewGuid()}")
            .Options;

        return new HarborOpsDbContext(options);
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
