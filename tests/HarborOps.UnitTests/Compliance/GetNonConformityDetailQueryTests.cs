using HarborOps.Infrastructure.Persistence;
using HarborOps.Modules.Compliance.Application;
using HarborOps.Modules.Compliance.Contracts;
using HarborOps.Modules.Compliance.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.UnitTests.Compliance;

public sealed class GetNonConformityDetailQueryTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 8, 20, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task GetAsync_returns_null_for_an_unknown_non_conformity()
    {
        await using var dbContext = CreateDbContext();

        var response = await QueryAsync(dbContext, 999_999);

        Assert.Null(response);
    }

    [Fact]
    public async Task GetAsync_resolves_vessel_id_from_a_survey_source()
    {
        await using var dbContext = CreateDbContext();
        var vesselId = Guid.NewGuid();
        var survey = new SurveyAudit(vesselId, new DateOnly(2026, 7, 1), "Annual Survey", "Pass", "No findings.");
        await dbContext.AddAsync(survey);
        await dbContext.SaveChangesAsync();
        var nonConformity = new NonConformity(
            Guid.NewGuid(), NonConformity.SeverityMajor, new DateOnly(2026, 12, 1), NonConformity.ClosureStatusOpen, surveyId: survey.Id);
        await dbContext.AddAsync(nonConformity);
        await dbContext.SaveChangesAsync();

        var response = await QueryAsync(dbContext, nonConformity.Id);

        Assert.NotNull(response);
        Assert.Equal(vesselId, response.VesselId);
    }

    [Fact]
    public async Task GetAsync_resolves_vessel_id_from_a_psc_source()
    {
        await using var dbContext = CreateDbContext();
        var vesselId = Guid.NewGuid();
        var inspection = new PscInspection(vesselId, Guid.NewGuid(), new DateOnly(2026, 7, 1), "Port Authority", 1, false);
        await dbContext.AddAsync(inspection);
        await dbContext.SaveChangesAsync();
        var nonConformity = new NonConformity(
            Guid.NewGuid(), NonConformity.SeverityMajor, new DateOnly(2026, 12, 1), NonConformity.ClosureStatusOpen, pscId: inspection.Id);
        await dbContext.AddAsync(nonConformity);
        await dbContext.SaveChangesAsync();

        var response = await QueryAsync(dbContext, nonConformity.Id);

        Assert.NotNull(response);
        Assert.Equal(vesselId, response.VesselId);
    }

    [Fact]
    public async Task GetAsync_orders_corrective_actions_newest_first()
    {
        await using var dbContext = CreateDbContext();
        var inspection = await SeedPscInspectionAsync(dbContext);
        var nonConformity = new NonConformity(
            Guid.NewGuid(), NonConformity.SeverityMajor, new DateOnly(2026, 12, 1), NonConformity.ClosureStatusOpen, pscId: inspection.Id);
        await dbContext.AddAsync(nonConformity);
        await dbContext.SaveChangesAsync();

        var older = CorrectiveAction.Record(
            nonConformity.Id, "First action.", Guid.NewGuid(), new DateTimeOffset(2026, 8, 1, 9, 0, 0, TimeSpan.Zero), null);
        var newer = CorrectiveAction.Record(
            nonConformity.Id, "Second action.", Guid.NewGuid(), new DateTimeOffset(2026, 8, 5, 9, 0, 0, TimeSpan.Zero), null);
        await dbContext.AddRangeAsync(older, newer);
        await dbContext.SaveChangesAsync();

        var response = await QueryAsync(dbContext, nonConformity.Id);

        Assert.NotNull(response);
        Assert.Equal([newer.Id, older.Id], response.CorrectiveActions.Select(action => action.Id));
    }

    [Fact]
    public async Task GetAsync_is_overdue_when_open_and_past_deadline()
    {
        await using var dbContext = CreateDbContext();
        var inspection = await SeedPscInspectionAsync(dbContext);
        var today = DateOnly.FromDateTime(FixedNow.UtcDateTime);
        var nonConformity = new NonConformity(
            Guid.NewGuid(), NonConformity.SeverityMajor, today.AddDays(-1), NonConformity.ClosureStatusOpen, pscId: inspection.Id);
        await dbContext.AddAsync(nonConformity);
        await dbContext.SaveChangesAsync();

        var response = await QueryAsync(dbContext, nonConformity.Id);

        Assert.NotNull(response);
        Assert.True(response.IsOverdue);
    }

    [Fact]
    public async Task GetAsync_is_not_overdue_when_the_deadline_has_not_passed()
    {
        await using var dbContext = CreateDbContext();
        var inspection = await SeedPscInspectionAsync(dbContext);
        var today = DateOnly.FromDateTime(FixedNow.UtcDateTime);
        var nonConformity = new NonConformity(
            Guid.NewGuid(), NonConformity.SeverityMajor, today.AddDays(1), NonConformity.ClosureStatusOpen, pscId: inspection.Id);
        await dbContext.AddAsync(nonConformity);
        await dbContext.SaveChangesAsync();

        var response = await QueryAsync(dbContext, nonConformity.Id);

        Assert.NotNull(response);
        Assert.False(response.IsOverdue);
    }

    [Fact]
    public async Task GetAsync_is_not_overdue_when_closed_even_with_a_past_deadline()
    {
        await using var dbContext = CreateDbContext();
        var inspection = await SeedPscInspectionAsync(dbContext);
        var today = DateOnly.FromDateTime(FixedNow.UtcDateTime);
        var nonConformity = new NonConformity(
            Guid.NewGuid(), NonConformity.SeverityMajor, today.AddDays(-1), NonConformity.ClosureStatusClosed, pscId: inspection.Id);
        await dbContext.AddAsync(nonConformity);
        await dbContext.SaveChangesAsync();

        var response = await QueryAsync(dbContext, nonConformity.Id);

        Assert.NotNull(response);
        Assert.False(response.IsOverdue);
    }

    private static Task<GetNonConformityDetailResponse?> QueryAsync(HarborOpsDbContext dbContext, int nonConformityId) =>
        new GetNonConformityDetailQuery(dbContext, new FixedTimeProvider(FixedNow)).GetAsync(nonConformityId, CancellationToken.None);

    private static async Task<PscInspection> SeedPscInspectionAsync(HarborOpsDbContext dbContext)
    {
        var inspection = new PscInspection(Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2026, 7, 1), "Port Authority", 1, false);
        await dbContext.AddAsync(inspection);
        await dbContext.SaveChangesAsync();
        return inspection;
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
        public override DateTimeOffset GetUtcNow()
        {
            return utcNow;
        }
    }
}
