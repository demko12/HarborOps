using HarborOps.Infrastructure.Persistence;
using HarborOps.Modules.Compliance.Application;
using HarborOps.Modules.Compliance.Contracts;
using HarborOps.Modules.Compliance.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.UnitTests.Compliance;

public sealed class GetVesselComplianceOverviewQueryTests
{
    [Fact]
    public async Task GetAsync_returns_empty_collections_when_vessel_has_no_compliance_data()
    {
        await using var dbContext = CreateDbContext();
        var response = await QueryAsync(dbContext, Guid.NewGuid());

        Assert.Empty(response.Certificates);
        Assert.Empty(response.PscInspections);
        Assert.Empty(response.SurveyAudits);
        Assert.Empty(response.OpenNonConformities);
    }

    [Fact]
    public async Task GetAsync_orders_certificates_deterministically_by_expiry_then_id()
    {
        await using var dbContext = CreateDbContext();
        var vesselId = Guid.NewGuid();
        var sameExpiry = new DateOnly(2027, 1, 1);
        var firstById = ComplianceCertificate.Register(vesselId, "A", "1", "Authority", sameExpiry.AddYears(-1), sameExpiry);
        var secondById = ComplianceCertificate.Register(vesselId, "B", "2", "Authority", sameExpiry.AddYears(-1), sameExpiry);
        var early = ComplianceCertificate.Register(vesselId, "C", "3", "Authority", sameExpiry.AddYears(-1), sameExpiry.AddDays(-1));
        await dbContext.AddRangeAsync(firstById, secondById, early);
        await dbContext.SaveChangesAsync();

        var response = await QueryAsync(dbContext, vesselId);
        var expectedTiedIds = new[] { firstById.Id, secondById.Id }.Order().ToArray();

        Assert.Equal(early.Id, response.Certificates[0].Id);
        Assert.Equal(expectedTiedIds, response.Certificates.Skip(1).Select(item => item.Id));
    }

    [Fact]
    public async Task GetAsync_orders_psc_inspections_deterministically_by_date_descending_then_id()
    {
        await using var dbContext = CreateDbContext();
        var vesselId = Guid.NewGuid();
        var date = new DateOnly(2026, 8, 1);
        var older = new PscInspection(vesselId, Guid.NewGuid(), date.AddDays(-1), "A", 0, false);
        var tiedOne = new PscInspection(vesselId, Guid.NewGuid(), date, "B", 1, false);
        var tiedTwo = new PscInspection(vesselId, Guid.NewGuid(), date, "C", 2, true);
        await dbContext.AddRangeAsync(older, tiedOne, tiedTwo);
        await dbContext.SaveChangesAsync();

        var response = await QueryAsync(dbContext, vesselId);

        Assert.Equal(new[] { tiedOne.Id, tiedTwo.Id, older.Id }, response.PscInspections.Select(item => item.Id));
    }

    [Fact]
    public async Task GetAsync_orders_survey_audits_deterministically_by_date_descending_then_id()
    {
        await using var dbContext = CreateDbContext();
        var vesselId = Guid.NewGuid();
        var date = new DateOnly(2026, 8, 1);
        var older = new SurveyAudit(vesselId, date.AddDays(-1), "Annual Survey", "Pass", "No findings.");
        var tiedOne = new SurveyAudit(vesselId, date, "Intermediate Survey", "Pass", "Minor findings.");
        var tiedTwo = new SurveyAudit(vesselId, date, "Renewal Survey", "Fail", "Major findings.");
        await dbContext.AddRangeAsync(older, tiedOne, tiedTwo);
        await dbContext.SaveChangesAsync();

        var response = await QueryAsync(dbContext, vesselId);

        Assert.Equal(new[] { tiedOne.Id, tiedTwo.Id, older.Id }, response.SurveyAudits.Select(item => item.Id));
    }

    [Fact]
    public async Task GetAsync_orders_open_non_conformities_and_filters_closed_records()
    {
        await using var dbContext = CreateDbContext();
        var vesselId = Guid.NewGuid();
        var inspection = new PscInspection(vesselId, Guid.NewGuid(), new DateOnly(2026, 7, 1), "Authority", 2, false);
        await dbContext.AddAsync(inspection);
        await dbContext.SaveChangesAsync();
        var deadline = new DateOnly(2026, 9, 1);
        var tiedOne = new NonConformity(Guid.NewGuid(), "Minor", deadline, "Open", pscId: inspection.Id);
        var tiedTwo = new NonConformity(Guid.NewGuid(), "Major", deadline, "Open", pscId: inspection.Id);
        var earlier = new NonConformity(Guid.NewGuid(), "Major", deadline.AddDays(-1), "Open", pscId: inspection.Id);
        var closed = new NonConformity(Guid.NewGuid(), "Major", deadline.AddDays(-2), "Closed", pscId: inspection.Id);
        await dbContext.AddRangeAsync(tiedOne, tiedTwo, earlier, closed);
        await dbContext.SaveChangesAsync();

        var response = await QueryAsync(dbContext, vesselId);

        Assert.Equal(new[] { earlier.Id, tiedOne.Id, tiedTwo.Id }, response.OpenNonConformities.Select(item => item.Id));
        Assert.DoesNotContain(response.OpenNonConformities, item => item.Id == closed.Id);
    }

    [Fact]
    public async Task GetAsync_isolates_every_collection_by_vessel_id()
    {
        await using var dbContext = CreateDbContext();
        var targetVesselId = Guid.NewGuid();
        var otherVesselId = Guid.NewGuid();
        await dbContext.AddAsync(ComplianceCertificate.Register(otherVesselId, "Other", "O", "Authority", new DateOnly(2026, 1, 1), new DateOnly(2027, 1, 1)));
        var otherInspection = new PscInspection(otherVesselId, Guid.NewGuid(), new DateOnly(2026, 8, 1), "Authority", 1, false);
        await dbContext.AddAsync(otherInspection);
        await dbContext.AddAsync(new SurveyAudit(otherVesselId, new DateOnly(2026, 8, 1), "Annual Survey", "Pass", "No findings."));
        await dbContext.SaveChangesAsync();
        await dbContext.AddAsync(new NonConformity(Guid.NewGuid(), "Minor", new DateOnly(2026, 9, 1), "Open", pscId: otherInspection.Id));
        await dbContext.SaveChangesAsync();

        var response = await QueryAsync(dbContext, targetVesselId);

        Assert.Empty(response.Certificates);
        Assert.Empty(response.PscInspections);
        Assert.Empty(response.SurveyAudits);
        Assert.Empty(response.OpenNonConformities);
    }

    [Fact]
    public async Task GetAsync_distinguishes_current_not_yet_effective_expired_renewed_and_revoked_certificates()
    {
        await using var dbContext = CreateDbContext();
        var vesselId = Guid.NewGuid();
        var asOf = new DateOnly(2026, 9, 28);
        var renewedAtUtc = new DateTime(2026, 9, 1, 8, 0, 0, DateTimeKind.Utc);
        var original = ComplianceCertificate.Register(vesselId, "Safety", "SMC-1", "Flag State", new DateOnly(2021, 10, 1), new DateOnly(2026, 9, 30));
        var futureSuccessor = original.Renew("SMC-2", "Flag State", new DateOnly(2026, 10, 1), new DateOnly(2031, 9, 30), renewedAtUtc);
        var expired = ComplianceCertificate.Register(vesselId, "Load Line", "LL-1", "Class", new DateOnly(2021, 1, 1), new DateOnly(2026, 9, 27));
        var gappedSuccessor = expired.Renew("LL-2", "Class", new DateOnly(2026, 10, 5), new DateOnly(2031, 10, 4), renewedAtUtc);
        var revoked = ComplianceCertificate.Register(vesselId, "IOPP", "IOPP-1", "Class", new DateOnly(2024, 1, 1), new DateOnly(2029, 1, 1));
        revoked.Revoke(Guid.NewGuid(), "Issued in error by the flag state.", renewedAtUtc);
        await dbContext.AddRangeAsync(original, futureSuccessor, expired, gappedSuccessor, revoked);
        await dbContext.SaveChangesAsync();

        var response = await new GetVesselComplianceOverviewQuery(dbContext, new FakeDateTimeProvider(asOf.ToDateTime(TimeOnly.MinValue)))
            .GetAsync(new GetVesselComplianceOverviewRequest { VesselId = vesselId }, CancellationToken.None);

        var originalItem = response.Certificates.Single(item => item.Id == original.Id);
        Assert.Equal(ComplianceCertificate.CurrentStatus, originalItem.Status);
        Assert.Equal(futureSuccessor.Id, originalItem.SuccessorCertificateId);
        Assert.Equal("SMC-2", originalItem.SuccessorCertificateNumber);
        Assert.Equal(new DateOnly(2026, 10, 1), originalItem.SuccessorIssuedOn);
        Assert.True(originalItem.IsRenewalCovered);
        Assert.Equal(0, originalItem.RenewalGapDays);

        var successorItem = response.Certificates.Single(item => item.Id == futureSuccessor.Id);
        Assert.Equal(ComplianceCertificate.NotYetEffectiveStatus, successorItem.Status);
        Assert.Equal(original.Id, successorItem.PredecessorCertificateId);
        Assert.Null(successorItem.SuccessorCertificateId);

        var expiredItem = response.Certificates.Single(item => item.Id == expired.Id);
        Assert.Equal(ComplianceCertificate.ExpiredStatus, expiredItem.Status);
        Assert.False(expiredItem.IsRenewalCovered);
        Assert.Equal(7, expiredItem.RenewalGapDays);

        var revokedItem = response.Certificates.Single(item => item.Id == revoked.Id);
        Assert.Equal(ComplianceCertificate.RevokedStatus, revokedItem.Status);
        Assert.Equal(renewedAtUtc, revokedItem.RevokedAtUtc);
    }

    [Fact]
    public async Task GetAsync_marks_a_revoked_successor_as_not_covering_the_original()
    {
        await using var dbContext = CreateDbContext();
        var vesselId = Guid.NewGuid();
        var original = ComplianceCertificate.Register(vesselId, "Safety", "SMC-1", "Flag State", new DateOnly(2021, 10, 1), new DateOnly(2026, 9, 30));
        var successor = original.Renew("SMC-2", "Flag State", new DateOnly(2026, 9, 1), new DateOnly(2031, 9, 30), DateTime.UtcNow);
        successor.Revoke(Guid.NewGuid(), "Successor issued with a wrong IMO number.", DateTime.UtcNow);
        await dbContext.AddRangeAsync(original, successor);
        await dbContext.SaveChangesAsync();

        var response = await QueryAsync(dbContext, vesselId);

        var originalItem = response.Certificates.Single(item => item.Id == original.Id);
        Assert.Equal(successor.Id, originalItem.SuccessorCertificateId);
        Assert.True(originalItem.SuccessorIsRevoked);
        Assert.False(originalItem.IsRenewalCovered);
    }

    private static Task<GetVesselComplianceOverviewResponse> QueryAsync(
        HarborOpsDbContext dbContext,
        Guid vesselId) => new GetVesselComplianceOverviewQuery(dbContext).GetAsync(
            new GetVesselComplianceOverviewRequest { VesselId = vesselId },
            CancellationToken.None);

    private static HarborOpsDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<HarborOpsDbContext>()
            .UseInMemoryDatabase($"HarborOpsComplianceQueryTests-{Guid.NewGuid()}")
            .Options;
        return new HarborOpsDbContext(options);
    }
}
