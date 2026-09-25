using HarborOps.Modules.Crew.Application;
using HarborOps.Modules.Crew.Contracts;

namespace HarborOps.UnitTests.Crew;

public sealed class GetCrewQualificationEligibilityQueryTests
{
    [Fact]
    public async Task GetAsync_returns_not_configured_when_no_policy_exists_for_the_rank()
    {
        await using var dbContext = QualificationPolicyTestData.CreateDbContext();
        var position = await VesselManningPlanTestData.SeedPositionAsync(dbContext);
        var member = await VesselManningPlanTestData.SeedMemberAsync(dbContext, position.Id);

        var response = await Query(dbContext).GetAsync(
            Request(member.Id, position.Id, new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 10)), CancellationToken.None);

        Assert.Equal(QualificationStatuses.NotConfigured, response.Status);
        Assert.Empty(response.MissingTypes);
        Assert.Empty(response.UncoveredDates);
    }

    [Fact]
    public async Task GetAsync_returns_not_configured_when_the_policy_is_disabled()
    {
        await using var dbContext = QualificationPolicyTestData.CreateDbContext();
        var position = await VesselManningPlanTestData.SeedPositionAsync(dbContext);
        var member = await VesselManningPlanTestData.SeedMemberAsync(dbContext, position.Id);
        await QualificationPolicyTestData.SeedPolicyAsync(dbContext, position.Id, false, ["STCW-II/1"]);

        var response = await Query(dbContext).GetAsync(
            Request(member.Id, position.Id, new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 10)), CancellationToken.None);

        Assert.Equal(QualificationStatuses.NotConfigured, response.Status);
    }

    [Fact]
    public async Task GetAsync_returns_qualified_for_an_explicitly_empty_requirement_set()
    {
        await using var dbContext = QualificationPolicyTestData.CreateDbContext();
        var position = await VesselManningPlanTestData.SeedPositionAsync(dbContext);
        var member = await VesselManningPlanTestData.SeedMemberAsync(dbContext, position.Id);
        await QualificationPolicyTestData.SeedPolicyAsync(dbContext, position.Id, true, []);

        var response = await Query(dbContext).GetAsync(
            Request(member.Id, position.Id, new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 10)), CancellationToken.None);

        Assert.Equal(QualificationStatuses.Qualified, response.Status);
    }

    [Fact]
    public async Task GetAsync_returns_qualified_when_certificates_cover_the_whole_window()
    {
        await using var dbContext = QualificationPolicyTestData.CreateDbContext();
        var position = await VesselManningPlanTestData.SeedPositionAsync(dbContext);
        var member = await VesselManningPlanTestData.SeedMemberAsync(dbContext, position.Id);
        await QualificationPolicyTestData.SeedPolicyAsync(dbContext, position.Id, true, ["STCW-II/1"]);
        await QualificationPolicyTestData.SeedCertificateAsync(
            dbContext, member.Id, "STCW-II/1", new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31));

        var response = await Query(dbContext).GetAsync(
            Request(member.Id, position.Id, new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 10)), CancellationToken.None);

        Assert.Equal(QualificationStatuses.Qualified, response.Status);
        Assert.Contains(response.CertificateLinks, id => id != Guid.Empty);
    }

    [Fact]
    public async Task GetAsync_returns_not_qualified_with_missing_types_and_uncovered_dates_for_a_gap()
    {
        await using var dbContext = QualificationPolicyTestData.CreateDbContext();
        var position = await VesselManningPlanTestData.SeedPositionAsync(dbContext);
        var member = await VesselManningPlanTestData.SeedMemberAsync(dbContext, position.Id);
        await QualificationPolicyTestData.SeedPolicyAsync(dbContext, position.Id, true, ["STCW-II/1"]);

        var response = await Query(dbContext).GetAsync(
            Request(member.Id, position.Id, new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 10)), CancellationToken.None);

        Assert.Equal(QualificationStatuses.NotQualified, response.Status);
        Assert.Equal(["STCW-II/1"], response.MissingTypes);
        var range = Assert.Single(response.UncoveredDates);
        Assert.Equal(new DateOnly(2026, 1, 1), range.StartDate);
        Assert.Equal(new DateOnly(2026, 1, 10), range.EndDate);
    }

    [Fact]
    public async Task GetAsync_rejects_an_unknown_crew_member()
    {
        await using var dbContext = QualificationPolicyTestData.CreateDbContext();

        await Assert.ThrowsAsync<CrewMemberNotFoundException>(() => Query(dbContext).GetAsync(
            Request(Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 10)), CancellationToken.None));
    }

    private static GetCrewQualificationEligibilityQuery Query(HarborOps.Infrastructure.Persistence.HarborOpsDbContext dbContext) => new(dbContext);

    private static GetCrewQualificationEligibilityRequest Request(Guid crewMemberId, Guid rankId, DateOnly from, DateOnly to) => new()
    {
        CrewMemberId = crewMemberId,
        RankId = rankId,
        From = from,
        To = to,
    };
}
