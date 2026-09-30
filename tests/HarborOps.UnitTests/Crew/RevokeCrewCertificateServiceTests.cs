using HarborOps.Infrastructure.Persistence;
using HarborOps.Modules.Crew.Application;
using HarborOps.Modules.Crew.Contracts;
using HarborOps.Modules.Crew.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.UnitTests.Crew;

public sealed class RevokeCrewCertificateServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task RevokeAsync_rejects_a_missing_reason()
    {
        await using var dbContext = QualificationPolicyTestData.CreateDbContext();
        var certificate = await SeedCertificateAsync(dbContext);
        var request = new RevokeCrewCertificateRequest { Reason = " " };

        var exception = await Assert.ThrowsAsync<RevokeCrewCertificateValidationException>(
            () => CreateService(dbContext).RevokeAsync(certificate.Id, Guid.NewGuid(), request, CancellationToken.None));

        Assert.Contains(RevokeCrewCertificateValidator.ReasonField, exception.Errors);
    }

    [Fact]
    public async Task RevokeAsync_rejects_a_reason_that_exceeds_the_max_length()
    {
        await using var dbContext = QualificationPolicyTestData.CreateDbContext();
        var certificate = await SeedCertificateAsync(dbContext);
        var request = new RevokeCrewCertificateRequest { Reason = new string('a', CrewCertificate.RevocationReasonMaxLength + 1) };

        var exception = await Assert.ThrowsAsync<RevokeCrewCertificateValidationException>(
            () => CreateService(dbContext).RevokeAsync(certificate.Id, Guid.NewGuid(), request, CancellationToken.None));

        Assert.Contains(RevokeCrewCertificateValidator.ReasonField, exception.Errors);
    }

    [Fact]
    public async Task RevokeAsync_throws_when_the_certificate_does_not_exist()
    {
        await using var dbContext = QualificationPolicyTestData.CreateDbContext();
        var unknownId = Guid.NewGuid();

        var exception = await Assert.ThrowsAsync<CrewCertificateNotFoundException>(
            () => CreateService(dbContext).RevokeAsync(unknownId, Guid.NewGuid(), ValidRequest(), CancellationToken.None));

        Assert.Equal(unknownId, exception.CertificateId);
    }

    [Fact]
    public async Task RevokeAsync_records_lifecycle_fields_history_and_bumps_the_concurrency_token()
    {
        await using var dbContext = QualificationPolicyTestData.CreateDbContext();
        var certificate = await SeedCertificateAsync(dbContext);
        var previousToken = certificate.ConcurrencyToken;
        var actorUserId = Guid.NewGuid();

        var response = await CreateService(dbContext).RevokeAsync(certificate.Id, actorUserId, ValidRequest(), CancellationToken.None);

        Assert.Equal(certificate.Id, response.Id);
        Assert.NotEqual(previousToken, response.ConcurrencyToken);
        Assert.Empty(response.AffectedActiveAssignments);

        var revoked = await dbContext.Set<CrewCertificate>().SingleAsync(c => c.Id == certificate.Id);
        Assert.True(revoked.IsRevoked);
        Assert.Equal("Issued in error", revoked.RevocationReason);

        var history = await dbContext.Set<CrewCertificateHistory>().SingleAsync(h => h.CertificateId == certificate.Id);
        Assert.Equal(CrewCertificateHistory.RevokedEvent, history.EventType);
        Assert.Equal(actorUserId, history.ActorUserId);
        Assert.Equal("Issued in error", history.Reason);
    }

    [Fact]
    public async Task RevokeAsync_rejects_double_revocation()
    {
        await using var dbContext = QualificationPolicyTestData.CreateDbContext();
        var certificate = await SeedCertificateAsync(dbContext);
        await CreateService(dbContext).RevokeAsync(certificate.Id, Guid.NewGuid(), ValidRequest(), CancellationToken.None);

        await Assert.ThrowsAsync<CrewCertificateAlreadyRevokedException>(
            () => CreateService(dbContext).RevokeAsync(certificate.Id, Guid.NewGuid(), ValidRequest(), CancellationToken.None));
    }

    [Fact]
    public async Task RevokeAsync_reports_an_active_assignment_whose_recorded_evidence_relied_on_this_certificate()
    {
        await using var dbContext = QualificationPolicyTestData.CreateDbContext();
        var position = await VesselManningPlanTestData.SeedPositionAsync(dbContext);
        var member = await VesselManningPlanTestData.SeedMemberAsync(dbContext, position.Id);
        var vesselId = Guid.NewGuid();
        var assignment = await VesselManningPlanTestData.SeedAssignmentAsync(
            dbContext, member.Id, vesselId, position.Id, new DateOnly(2026, 1, 1), new DateOnly(2026, 6, 30));
        var policy = await QualificationPolicyTestData.SeedPolicyAsync(dbContext, position.Id, true, ["STCW-II/1"]);
        var certificate = await QualificationPolicyTestData.SeedCertificateAsync(
            dbContext, member.Id, "STCW-II/1", new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31));

        await new ActivateCrewAssignmentService(dbContext, new FixedTimeProvider(Now))
            .ActivateAsync(assignment.Id, Guid.NewGuid(), CancellationToken.None);

        var response = await CreateService(dbContext).RevokeAsync(certificate.Id, Guid.NewGuid(), ValidRequest(), CancellationToken.None);

        var affected = Assert.Single(response.AffectedActiveAssignments);
        Assert.Equal(assignment.Id, affected.AssignmentId);
        Assert.Equal(vesselId, affected.VesselId);
        Assert.Equal(position.Id, affected.RankId);

        // Prior activation evidence must remain untouched -- revocation only warns, never rewrites history.
        var evidence = await dbContext.Set<CrewAssignmentQualificationEvidence>().SingleAsync(e => e.AssignmentId == assignment.Id);
        Assert.Equal(policy.Id, evidence.PolicyId);
        Assert.Contains(certificate.Id.ToString(), evidence.CertificateIdsSnapshot);

        var savedAssignment = await dbContext.Set<CrewAssignment>().SingleAsync(a => a.Id == assignment.Id);
        Assert.Equal(CrewAssignment.ActiveStatus, savedAssignment.Status);
    }

    [Fact]
    public async Task RevokeAsync_does_not_report_a_completed_assignment_as_affected()
    {
        await using var dbContext = QualificationPolicyTestData.CreateDbContext();
        var position = await VesselManningPlanTestData.SeedPositionAsync(dbContext);
        var member = await VesselManningPlanTestData.SeedMemberAsync(dbContext, position.Id);
        var assignment = await VesselManningPlanTestData.SeedAssignmentAsync(
            dbContext, member.Id, Guid.NewGuid(), position.Id, new DateOnly(2026, 1, 1), new DateOnly(2026, 6, 30));
        await QualificationPolicyTestData.SeedPolicyAsync(dbContext, position.Id, true, ["STCW-II/1"]);
        var certificate = await QualificationPolicyTestData.SeedCertificateAsync(
            dbContext, member.Id, "STCW-II/1", new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31));

        await new ActivateCrewAssignmentService(dbContext, new FixedTimeProvider(Now))
            .ActivateAsync(assignment.Id, Guid.NewGuid(), CancellationToken.None);
        await new CompleteCrewAssignmentService(dbContext).CompleteAsync(
            assignment.Id,
            new CompleteCrewAssignmentRequest { ActualSignOffDate = new DateOnly(2026, 6, 30) },
            CancellationToken.None);

        var response = await CreateService(dbContext).RevokeAsync(certificate.Id, Guid.NewGuid(), ValidRequest(), CancellationToken.None);

        Assert.Empty(response.AffectedActiveAssignments);
    }

    [Fact]
    public async Task RevokeAsync_makes_the_certificate_unable_to_satisfy_a_fresh_activation_attempt()
    {
        await using var dbContext = QualificationPolicyTestData.CreateDbContext();
        var position = await VesselManningPlanTestData.SeedPositionAsync(dbContext);
        var member = await VesselManningPlanTestData.SeedMemberAsync(dbContext, position.Id);
        var assignment = await VesselManningPlanTestData.SeedAssignmentAsync(
            dbContext, member.Id, Guid.NewGuid(), position.Id, new DateOnly(2026, 1, 1), new DateOnly(2026, 6, 30));
        await QualificationPolicyTestData.SeedPolicyAsync(dbContext, position.Id, true, ["STCW-II/1"]);
        var certificate = await QualificationPolicyTestData.SeedCertificateAsync(
            dbContext, member.Id, "STCW-II/1", new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31));

        await CreateService(dbContext).RevokeAsync(certificate.Id, Guid.NewGuid(), ValidRequest(), CancellationToken.None);

        var exception = await Assert.ThrowsAsync<CrewMemberNotQualifiedException>(
            () => new ActivateCrewAssignmentService(dbContext, new FixedTimeProvider(Now))
                .ActivateAsync(assignment.Id, Guid.NewGuid(), CancellationToken.None));

        Assert.Contains("STCW-II/1", exception.MissingTypes);
    }

    private static async Task<CrewCertificate> SeedCertificateAsync(HarborOpsDbContext dbContext)
    {
        var position = await VesselManningPlanTestData.SeedPositionAsync(dbContext);
        var member = await VesselManningPlanTestData.SeedMemberAsync(dbContext, position.Id);
        return await QualificationPolicyTestData.SeedCertificateAsync(
            dbContext, member.Id, "STCW", new DateOnly(2024, 1, 1), new DateOnly(2026, 3, 1));
    }

    private static RevokeCrewCertificateRequest ValidRequest() => new() { Reason = "Issued in error" };

    private static RevokeCrewCertificateService CreateService(HarborOpsDbContext dbContext) =>
        new(dbContext, new FixedTimeProvider(Now));
}
