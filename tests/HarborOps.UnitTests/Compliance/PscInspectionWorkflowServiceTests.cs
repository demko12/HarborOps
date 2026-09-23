using HarborOps.Infrastructure.Persistence;
using HarborOps.Modules.Compliance.Application;
using HarborOps.Modules.Compliance.Contracts;
using HarborOps.Modules.Compliance.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.UnitTests.Compliance;

public sealed class PscInspectionWorkflowServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 21, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task RegisterDeficiencyAsync_creates_a_standard_linked_non_conformity_with_psc_metadata()
    {
        await using var dbContext = CreateDbContext();
        var inspection = await SeedInspectionAsync(dbContext, deficiencyCount: 1);
        var evidenceId = Guid.NewGuid();

        var response = await CreateService(dbContext).RegisterDeficiencyAsync(inspection.Id,
            new RegisterPscDeficiencyRequest { DeficiencyCode = "07103", Description = "Fire doors not maintained.", Severity = "Critical", CorrectiveActionDeadline = DateOnly.FromDateTime(Now.UtcDateTime).AddDays(1), EvidenceDocumentId = evidenceId }, CancellationToken.None);

        var finding = await dbContext.Set<NonConformity>().SingleAsync(item => item.Id == response.NonConformityId);
        Assert.Equal(inspection.Id, finding.PscId);
        Assert.Equal("07103", finding.PscDeficiencyCode);
        Assert.Equal("Fire doors not maintained.", finding.PscDeficiencyDescription);
        Assert.Equal(evidenceId, finding.EvidenceDocumentId);
        Assert.Equal(NonConformity.ClosureStatusOpen, finding.ClosureStatus);
    }

    [Fact]
    public async Task DetainAsync_requires_an_open_critical_psc_deficiency()
    {
        await using var dbContext = CreateDbContext();
        var inspection = await SeedInspectionAsync(dbContext, deficiencyCount: 1);

        var exception = await Assert.ThrowsAsync<PscInspectionWorkflowConflictException>(() => CreateService(dbContext).DetainAsync(
            inspection.Id, new DetainPscInspectionRequest { AuthorityReference = "PSC-14", Reason = "Critical fire safety finding." }, Guid.NewGuid(), CancellationToken.None));

        Assert.Contains("Critical", exception.Message);
    }

    [Fact]
    public async Task ReleaseAsync_requires_reconciled_and_closed_psc_deficiencies()
    {
        await using var dbContext = CreateDbContext();
        var inspection = await SeedInspectionAsync(dbContext, deficiencyCount: 2);
        var finding = await AddFindingAsync(dbContext, inspection.Id, NonConformity.SeverityCritical, NonConformity.ClosureStatusOpen);
        var service = CreateService(dbContext);
        await service.DetainAsync(inspection.Id, new DetainPscInspectionRequest { AuthorityReference = "PSC-14", Reason = "Critical finding." }, Guid.NewGuid(), CancellationToken.None);
        await CloseFindingAsync(dbContext, finding);

        var exception = await Assert.ThrowsAsync<PscInspectionWorkflowConflictException>(() => service.ReleaseAsync(
            inspection.Id, new ReleasePscInspectionRequest { AuthorityReference = "PSC-15", ReleaseDate = DateOnly.FromDateTime(Now.UtcDateTime) }, Guid.NewGuid(), CancellationToken.None));

        Assert.Contains("reported", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ReleaseAsync_records_an_append_only_release_after_every_linked_finding_is_closed()
    {
        await using var dbContext = CreateDbContext();
        var inspection = await SeedInspectionAsync(dbContext, deficiencyCount: 1);
        var finding = await AddFindingAsync(dbContext, inspection.Id, NonConformity.SeverityCritical, NonConformity.ClosureStatusOpen);
        var actorId = Guid.NewGuid();
        var service = CreateService(dbContext);
        await service.DetainAsync(inspection.Id, new DetainPscInspectionRequest { AuthorityReference = "PSC-14", Reason = "Critical finding." }, actorId, CancellationToken.None);
        await CloseFindingAsync(dbContext, finding);
        await service.ReleaseAsync(inspection.Id, new ReleasePscInspectionRequest { AuthorityReference = "PSC-15", ReleaseDate = DateOnly.FromDateTime(Now.UtcDateTime), DocumentId = Guid.NewGuid() }, actorId, CancellationToken.None);

        var decisions = await dbContext.Set<PscDetentionDecision>().OrderBy(item => item.OccurredAtUtc).ToListAsync();
        Assert.Collection(decisions,
            detained => Assert.Equal(PscDetentionDecision.DetainedDecision, detained.Decision),
            released => { Assert.Equal(PscDetentionDecision.ReleasedDecision, released.Decision); Assert.Equal(actorId, released.ActorUserId); });

        await Assert.ThrowsAsync<PscInspectionWorkflowConflictException>(() => service.DetainAsync(
            inspection.Id, new DetainPscInspectionRequest { AuthorityReference = "PSC-16", Reason = "Repeated decision." }, actorId, CancellationToken.None));
    }

    private static async Task<PscInspection> SeedInspectionAsync(HarborOpsDbContext dbContext, int deficiencyCount)
    {
        var inspection = new PscInspection(Guid.NewGuid(), Guid.NewGuid(), DateOnly.FromDateTime(Now.UtcDateTime), "Authority", deficiencyCount, vesselDetained: false);
        await dbContext.AddAsync(inspection);
        await dbContext.SaveChangesAsync();
        return inspection;
    }

    private static async Task<NonConformity> AddFindingAsync(HarborOpsDbContext dbContext, int pscId, string severity, string closureStatus)
    {
        var finding = new NonConformity(Guid.NewGuid(), severity, DateOnly.FromDateTime(Now.UtcDateTime).AddDays(1), closureStatus, pscId: pscId);
        await dbContext.AddAsync(finding);
        await dbContext.SaveChangesAsync();
        return finding;
    }

    private static async Task CloseFindingAsync(HarborOpsDbContext dbContext, NonConformity finding)
    {
        finding.AddCorrectiveAction("Corrective action completed.", Guid.NewGuid(), Now, null);
        finding.Close("Verified and closed.", Guid.NewGuid(), Now, null);
        await dbContext.SaveChangesAsync();
    }

    private static PscInspectionWorkflowService CreateService(HarborOpsDbContext dbContext) => new(dbContext, new FixedTimeProvider(Now));

    private static HarborOpsDbContext CreateDbContext() => new(new DbContextOptionsBuilder<HarborOpsDbContext>().UseInMemoryDatabase($"HarborOpsUnitTests-{Guid.NewGuid()}").Options);

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
