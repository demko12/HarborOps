using HarborOps.Infrastructure.Persistence;
using HarborOps.Modules.Crew.Application;
using HarborOps.Modules.Crew.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.UnitTests.Crew;

public sealed class ActivateCrewAssignmentServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 15, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ActivateAsync_activates_the_assignment_when_no_policy_is_configured()
    {
        await using var dbContext = QualificationPolicyTestData.CreateDbContext();
        var position = await VesselManningPlanTestData.SeedPositionAsync(dbContext);
        var member = await VesselManningPlanTestData.SeedMemberAsync(dbContext, position.Id);
        var assignment = await VesselManningPlanTestData.SeedAssignmentAsync(
            dbContext, member.Id, Guid.NewGuid(), position.Id, new DateOnly(2026, 1, 1), new DateOnly(2026, 6, 30));

        await CreateService(dbContext).ActivateAsync(assignment.Id, Guid.NewGuid(), CancellationToken.None);

        var saved = await dbContext.Set<CrewAssignment>().SingleAsync(a => a.Id == assignment.Id);
        Assert.Equal(CrewAssignment.ActiveStatus, saved.Status);
        Assert.Empty(await dbContext.Set<CrewAssignmentQualificationEvidence>().ToListAsync());
    }

    [Fact]
    public async Task ActivateAsync_activates_and_records_evidence_when_fully_qualified()
    {
        await using var dbContext = QualificationPolicyTestData.CreateDbContext();
        var position = await VesselManningPlanTestData.SeedPositionAsync(dbContext);
        var member = await VesselManningPlanTestData.SeedMemberAsync(dbContext, position.Id);
        var assignment = await VesselManningPlanTestData.SeedAssignmentAsync(
            dbContext, member.Id, Guid.NewGuid(), position.Id, new DateOnly(2026, 1, 1), new DateOnly(2026, 6, 30));
        var policy = await QualificationPolicyTestData.SeedPolicyAsync(dbContext, position.Id, true, ["STCW-II/1"]);
        var certificate = await QualificationPolicyTestData.SeedCertificateAsync(
            dbContext, member.Id, "STCW-II/1", new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31));
        var actorUserId = Guid.NewGuid();

        await CreateService(dbContext).ActivateAsync(assignment.Id, actorUserId, CancellationToken.None);

        var saved = await dbContext.Set<CrewAssignment>().SingleAsync(a => a.Id == assignment.Id);
        Assert.Equal(CrewAssignment.ActiveStatus, saved.Status);

        var evidence = await dbContext.Set<CrewAssignmentQualificationEvidence>().SingleAsync(e => e.AssignmentId == assignment.Id);
        Assert.Equal(policy.Id, evidence.PolicyId);
        Assert.Equal(policy.ConcurrencyToken, evidence.PolicyConcurrencyTokenAtDecision);
        Assert.Contains(certificate.Id.ToString(), evidence.CertificateIdsSnapshot);
        Assert.Equal(actorUserId, evidence.RecordedByUserId);
    }

    [Fact]
    public async Task ActivateAsync_records_evidence_with_no_certificates_for_an_explicitly_empty_requirement_set()
    {
        await using var dbContext = QualificationPolicyTestData.CreateDbContext();
        var position = await VesselManningPlanTestData.SeedPositionAsync(dbContext);
        var member = await VesselManningPlanTestData.SeedMemberAsync(dbContext, position.Id);
        var assignment = await VesselManningPlanTestData.SeedAssignmentAsync(
            dbContext, member.Id, Guid.NewGuid(), position.Id, new DateOnly(2026, 1, 1), new DateOnly(2026, 6, 30));
        await QualificationPolicyTestData.SeedPolicyAsync(dbContext, position.Id, true, []);

        await CreateService(dbContext).ActivateAsync(assignment.Id, Guid.NewGuid(), CancellationToken.None);

        var evidence = await dbContext.Set<CrewAssignmentQualificationEvidence>().SingleAsync(e => e.AssignmentId == assignment.Id);
        Assert.Equal("[]", evidence.CertificateIdsSnapshot);
    }

    [Fact]
    public async Task ActivateAsync_blocks_with_a_409_style_exception_when_a_required_certificate_is_missing()
    {
        await using var dbContext = QualificationPolicyTestData.CreateDbContext();
        var position = await VesselManningPlanTestData.SeedPositionAsync(dbContext);
        var member = await VesselManningPlanTestData.SeedMemberAsync(dbContext, position.Id);
        var assignment = await VesselManningPlanTestData.SeedAssignmentAsync(
            dbContext, member.Id, Guid.NewGuid(), position.Id, new DateOnly(2026, 1, 1), new DateOnly(2026, 6, 30));
        await QualificationPolicyTestData.SeedPolicyAsync(dbContext, position.Id, true, ["STCW-II/1"]);

        var exception = await Assert.ThrowsAsync<CrewMemberNotQualifiedException>(
            () => CreateService(dbContext).ActivateAsync(assignment.Id, Guid.NewGuid(), CancellationToken.None));

        Assert.Equal(member.Id, exception.CrewMemberId);
        Assert.Equal(position.Id, exception.RankId);
        Assert.Equal(["STCW-II/1"], exception.MissingTypes);

        var saved = await dbContext.Set<CrewAssignment>().SingleAsync(a => a.Id == assignment.Id);
        Assert.Equal(CrewAssignment.PlannedStatus, saved.Status);
        Assert.Empty(await dbContext.Set<CrewAssignmentQualificationEvidence>().ToListAsync());
    }

    [Fact]
    public async Task ActivateAsync_evaluates_the_full_intended_service_interval_not_just_sign_on_day()
    {
        await using var dbContext = QualificationPolicyTestData.CreateDbContext();
        var position = await VesselManningPlanTestData.SeedPositionAsync(dbContext);
        var member = await VesselManningPlanTestData.SeedMemberAsync(dbContext, position.Id);
        var assignment = await VesselManningPlanTestData.SeedAssignmentAsync(
            dbContext, member.Id, Guid.NewGuid(), position.Id, new DateOnly(2026, 1, 1), new DateOnly(2026, 6, 30));
        await QualificationPolicyTestData.SeedPolicyAsync(dbContext, position.Id, true, ["STCW-II/1"]);

        // Covers sign-on day but expires well before the planned sign-off — the gap is only visible
        // when the full interval is checked, not just the sign-on date.
        await QualificationPolicyTestData.SeedCertificateAsync(
            dbContext, member.Id, "STCW-II/1", new DateOnly(2026, 1, 1), new DateOnly(2026, 2, 1));

        var exception = await Assert.ThrowsAsync<CrewMemberNotQualifiedException>(
            () => CreateService(dbContext).ActivateAsync(assignment.Id, Guid.NewGuid(), CancellationToken.None));

        var range = Assert.Single(exception.UncoveredRanges);
        Assert.Equal(new DateOnly(2026, 2, 2), range.StartDate);
        Assert.Equal(new DateOnly(2026, 6, 30), range.EndDate);
    }

    [Fact]
    public async Task ActivateAsync_still_blocks_on_approved_unavailability_before_checking_qualification()
    {
        await using var dbContext = QualificationPolicyTestData.CreateDbContext();
        var position = await VesselManningPlanTestData.SeedPositionAsync(dbContext);
        var member = await VesselManningPlanTestData.SeedMemberAsync(dbContext, position.Id);
        var assignment = await VesselManningPlanTestData.SeedAssignmentAsync(
            dbContext, member.Id, Guid.NewGuid(), position.Id, new DateOnly(2026, 1, 1), new DateOnly(2026, 6, 30));
        var unavailability = CrewAvailabilityRequest.Create(
            member.Id, "SickLeave", new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 10),
            "Recovering.", null, Guid.NewGuid(), new DateTime(2025, 12, 1, 0, 0, 0, DateTimeKind.Utc));
        unavailability.Approve(Guid.NewGuid(), new DateTime(2025, 12, 2, 0, 0, 0, DateTimeKind.Utc));
        await dbContext.Set<CrewAvailabilityRequest>().AddAsync(unavailability);
        await dbContext.SaveChangesAsync();

        await Assert.ThrowsAsync<CrewMemberUnavailableException>(
            () => CreateService(dbContext).ActivateAsync(assignment.Id, Guid.NewGuid(), CancellationToken.None));

        var saved = await dbContext.Set<CrewAssignment>().SingleAsync(a => a.Id == assignment.Id);
        Assert.Equal(CrewAssignment.PlannedStatus, saved.Status);
    }

    [Fact]
    public async Task ActivateAsync_throws_when_the_assignment_does_not_exist()
    {
        await using var dbContext = QualificationPolicyTestData.CreateDbContext();

        await Assert.ThrowsAsync<CrewAssignmentNotFoundException>(
            () => CreateService(dbContext).ActivateAsync(Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None));
    }

    private static ActivateCrewAssignmentService CreateService(HarborOpsDbContext dbContext) =>
        new(dbContext, new FixedTimeProvider(Now));
}
