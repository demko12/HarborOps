using HarborOps.Infrastructure.Persistence;
using HarborOps.Modules.Compliance.Application;
using HarborOps.Modules.Compliance.Contracts;
using HarborOps.Modules.Compliance.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.UnitTests.Compliance;

public sealed class ReportsComplianceExposureQueryTests
{
    private static readonly DateOnly AsOf = new(2026, 8, 15);

    [Fact]
    public async Task SearchAsync_returns_empty_result_when_no_compliance_data_exists()
    {
        await using var dbContext = CreateDbContext();

        var result = await QueryAsync(dbContext, AsOf);

        Assert.Empty(result.Items);
        Assert.Equal(0, result.TotalCount);
    }

    [Fact]
    public async Task SearchAsync_includes_certificates_expiring_within_the_exposure_window_and_excludes_others()
    {
        await using var dbContext = CreateDbContext();
        var vesselId = Guid.NewGuid();
        var withinWindow = ComplianceCertificate.Register(
            vesselId, "SMC", "CERT-1", "Authority", AsOf.AddYears(-1), AsOf.AddDays(60));
        var outsideWindow = ComplianceCertificate.Register(
            vesselId, "ISM", "CERT-2", "Authority", AsOf.AddYears(-1), AsOf.AddDays(61));
        await dbContext.AddRangeAsync(withinWindow, outsideWindow);
        await dbContext.SaveChangesAsync();

        var result = await QueryAsync(dbContext, AsOf);

        var item = Assert.Single(result.Items);
        Assert.Equal("Certificate", item.ItemType);
        Assert.Equal("CERT-1", item.Reference);
        Assert.Equal("Expiring", item.Status);
    }

    [Fact]
    public async Task SearchAsync_marks_certificate_expired_before_as_of_and_expiring_on_as_of()
    {
        await using var dbContext = CreateDbContext();
        var vesselId = Guid.NewGuid();
        var expired = ComplianceCertificate.Register(
            vesselId, "SMC", "EXPIRED", "Authority", AsOf.AddYears(-1), AsOf.AddDays(-1));
        var onAsOf = ComplianceCertificate.Register(
            vesselId, "ISM", "ON-AS-OF", "Authority", AsOf.AddYears(-1), AsOf);
        await dbContext.AddRangeAsync(expired, onAsOf);
        await dbContext.SaveChangesAsync();

        var result = await QueryAsync(dbContext, AsOf);

        var expiredItem = result.Items.Single(item => item.Reference == "EXPIRED");
        var onAsOfItem = result.Items.Single(item => item.Reference == "ON-AS-OF");
        Assert.Equal("Expired", expiredItem.Status);
        Assert.Equal("Expiring", onAsOfItem.Status);
    }

    [Fact]
    public async Task SearchAsync_includes_insurance_policies_expiring_within_the_exposure_window()
    {
        await using var dbContext = CreateDbContext();
        var vesselId = Guid.NewGuid();
        var withinWindow = new InsurancePolicy(vesselId, "Insurer A", "POL-1", "Hull", AsOf.AddDays(30));
        var outsideWindow = new InsurancePolicy(vesselId, "Insurer B", "POL-2", "Cargo", AsOf.AddDays(90));
        await dbContext.AddRangeAsync(withinWindow, outsideWindow);
        await dbContext.SaveChangesAsync();

        var result = await QueryAsync(dbContext, AsOf);

        var item = Assert.Single(result.Items);
        Assert.Equal("Insurance", item.ItemType);
        Assert.Equal("POL-1", item.Reference);
    }

    [Fact]
    public async Task SearchAsync_includes_only_detained_psc_inspections()
    {
        await using var dbContext = CreateDbContext();
        var vesselId = Guid.NewGuid();
        var detained = new PscInspection(vesselId, Guid.NewGuid(), AsOf.AddDays(-5), "Authority", 3, vesselDetained: true);
        var notDetained = new PscInspection(vesselId, Guid.NewGuid(), AsOf.AddDays(-5), "Authority", 1, vesselDetained: false);
        await dbContext.AddRangeAsync(detained, notDetained);
        await dbContext.SaveChangesAsync();

        var result = await QueryAsync(dbContext, AsOf);

        var item = Assert.Single(result.Items);
        Assert.Equal("Detention", item.ItemType);
        Assert.Equal("Detained", item.Status);
    }

    [Fact]
    public async Task SearchAsync_includes_only_open_non_conformities_and_excludes_closed()
    {
        await using var dbContext = CreateDbContext();
        var vesselId = Guid.NewGuid();
        var inspection = new PscInspection(vesselId, Guid.NewGuid(), AsOf.AddDays(-10), "Authority", 2, false);
        await dbContext.AddAsync(inspection);
        await dbContext.SaveChangesAsync();

        var open = new NonConformity(Guid.NewGuid(), "Major", AsOf.AddDays(10), "Open", pscId: inspection.Id);
        var closed = new NonConformity(Guid.NewGuid(), "Minor", AsOf.AddDays(5), "Closed", pscId: inspection.Id);
        await dbContext.AddRangeAsync(open, closed);
        await dbContext.SaveChangesAsync();

        var result = await QueryAsync(dbContext, AsOf);

        var item = Assert.Single(result.Items);
        Assert.Equal("NonConformity", item.ItemType);
        Assert.Equal(vesselId, item.VesselId);
        Assert.Equal("Major", item.Severity);
    }

    [Fact]
    public async Task SearchAsync_filters_by_vessel_id_across_all_categories()
    {
        await using var dbContext = CreateDbContext();
        var targetVesselId = Guid.NewGuid();
        var otherVesselId = Guid.NewGuid();
        var target = ComplianceCertificate.Register(
            targetVesselId, "SMC", "TARGET", "Authority", AsOf.AddYears(-1), AsOf.AddDays(10));
        var other = ComplianceCertificate.Register(
            otherVesselId, "SMC", "OTHER", "Authority", AsOf.AddYears(-1), AsOf.AddDays(10));
        await dbContext.AddRangeAsync(target, other);
        await dbContext.SaveChangesAsync();

        var result = await QueryAsync(dbContext, AsOf, vesselId: targetVesselId);

        var item = Assert.Single(result.Items);
        Assert.Equal("TARGET", item.Reference);
    }

    [Fact]
    public async Task SearchAsync_filters_by_category()
    {
        await using var dbContext = CreateDbContext();
        var vesselId = Guid.NewGuid();
        var certificate = ComplianceCertificate.Register(
            vesselId, "SMC", "CERT-1", "Authority", AsOf.AddYears(-1), AsOf.AddDays(10));
        var policy = new InsurancePolicy(vesselId, "Insurer", "POL-1", "Hull", AsOf.AddDays(10));
        await dbContext.AddRangeAsync(certificate, policy);
        await dbContext.SaveChangesAsync();

        var result = await QueryAsync(dbContext, AsOf, category: "Insurance");

        var item = Assert.Single(result.Items);
        Assert.Equal("Insurance", item.ItemType);
    }

    [Fact]
    public async Task SearchAsync_orders_by_relevant_date_then_item_type_then_reference()
    {
        await using var dbContext = CreateDbContext();
        var vesselId = Guid.NewGuid();
        var sameDate = AsOf.AddDays(20);
        var certificate = ComplianceCertificate.Register(
            vesselId, "SMC", "CERT-1", "Authority", AsOf.AddYears(-1), sameDate);
        var policy = new InsurancePolicy(vesselId, "Insurer", "POL-1", "Hull", sameDate);
        var earliestCertificate = ComplianceCertificate.Register(
            vesselId, "ISM", "CERT-0", "Authority", AsOf.AddYears(-1), AsOf.AddDays(5));
        await dbContext.AddRangeAsync(certificate, policy, earliestCertificate);
        await dbContext.SaveChangesAsync();

        var result = await QueryAsync(dbContext, AsOf);

        Assert.Equal(3, result.Items.Count);
        Assert.Equal("CERT-0", result.Items[0].Reference);
        Assert.Equal("Certificate", result.Items[1].ItemType);
        Assert.Equal("Insurance", result.Items[2].ItemType);
    }

    [Fact]
    public async Task SearchAsync_returns_second_page_and_total_count_across_all_pages()
    {
        await using var dbContext = CreateDbContext();
        var vesselId = Guid.NewGuid();

        for (var index = 0; index < 30; index++)
        {
            await dbContext.AddAsync(ComplianceCertificate.Register(
                vesselId, "SMC", $"CERT-{index:D2}", "Authority", AsOf.AddYears(-1), AsOf.AddDays(index)));
        }
        await dbContext.SaveChangesAsync();

        var secondPage = await QueryAsync(dbContext, AsOf, pageNumber: 2);

        Assert.Equal(2, secondPage.PageNumber);
        Assert.Equal(25, secondPage.PageSize);
        Assert.Equal(30, secondPage.TotalCount);
        Assert.Equal(5, secondPage.Items.Count);
    }

    [Fact]
    public async Task SearchAsync_keeps_revoked_certificates_visible_as_revoked_exposure_on_their_revocation_date()
    {
        await using var dbContext = CreateDbContext();
        var vesselId = Guid.NewGuid();
        var revokedAtUtc = new DateTime(2026, 8, 10, 9, 0, 0, DateTimeKind.Utc);
        var revoked = ComplianceCertificate.Register(
            vesselId, "SMC", "REVOKED", "Authority", AsOf.AddYears(-1), AsOf.AddYears(3));
        revoked.Revoke(Guid.NewGuid(), "Issued in error by the flag state.", revokedAtUtc);
        await dbContext.AddAsync(revoked);
        await dbContext.SaveChangesAsync();

        var result = await QueryAsync(dbContext, AsOf);

        var item = Assert.Single(result.Items);
        Assert.Equal("REVOKED", item.Reference);
        Assert.Equal(ReportsComplianceExposureQuery.StatusRevoked, item.Status);
        Assert.Equal(new DateOnly(2026, 8, 10), item.RelevantDate);
    }

    [Fact]
    public async Task SearchAsync_marks_a_covered_original_as_renewed_and_keeps_gaps_and_revoked_successors_as_exposure()
    {
        await using var dbContext = CreateDbContext();
        var vesselId = Guid.NewGuid();
        var renewedAtUtc = new DateTime(2026, 8, 1, 9, 0, 0, DateTimeKind.Utc);
        var covered = ComplianceCertificate.Register(vesselId, "SMC", "COVERED", "Authority", AsOf.AddYears(-1), AsOf.AddDays(20));
        var coveredSuccessor = covered.Renew("COVERED-2", "Authority", AsOf.AddDays(21), AsOf.AddYears(5), renewedAtUtc);
        var gapped = ComplianceCertificate.Register(vesselId, "ISM", "GAPPED", "Authority", AsOf.AddYears(-1), AsOf.AddDays(20));
        var gappedSuccessor = gapped.Renew("GAPPED-2", "Authority", AsOf.AddDays(25), AsOf.AddYears(5), renewedAtUtc);
        var withRevokedSuccessor = ComplianceCertificate.Register(vesselId, "IOPP", "REVOKED-SUCCESSOR", "Authority", AsOf.AddYears(-1), AsOf.AddDays(20));
        var revokedSuccessor = withRevokedSuccessor.Renew("REVOKED-SUCCESSOR-2", "Authority", AsOf.AddDays(10), AsOf.AddYears(5), renewedAtUtc);
        revokedSuccessor.Revoke(Guid.NewGuid(), "Successor issued with a wrong IMO number.", renewedAtUtc);
        await dbContext.AddRangeAsync(covered, coveredSuccessor, gapped, gappedSuccessor, withRevokedSuccessor, revokedSuccessor);
        await dbContext.SaveChangesAsync();

        var result = await QueryAsync(dbContext, AsOf, category: "Certificate");

        Assert.Equal(ReportsComplianceExposureQuery.StatusRenewed, result.Items.Single(item => item.Reference == "COVERED").Status);
        Assert.Equal("Expiring", result.Items.Single(item => item.Reference == "GAPPED").Status);
        Assert.Equal("Expiring", result.Items.Single(item => item.Reference == "REVOKED-SUCCESSOR").Status);
        Assert.Equal(ReportsComplianceExposureQuery.StatusRevoked, result.Items.Single(item => item.Reference == "REVOKED-SUCCESSOR-2").Status);
        Assert.DoesNotContain(result.Items, item => item.Reference is "COVERED-2" or "GAPPED-2");
    }

    [Fact]
    public async Task SearchAsync_marks_a_certificate_issued_after_as_of_as_not_yet_effective()
    {
        await using var dbContext = CreateDbContext();
        var future = ComplianceCertificate.Register(
            Guid.NewGuid(), "SMC", "FUTURE", "Authority", AsOf.AddDays(5), AsOf.AddDays(40));
        await dbContext.AddAsync(future);
        await dbContext.SaveChangesAsync();

        var result = await QueryAsync(dbContext, AsOf);

        Assert.Equal(ReportsComplianceExposureQuery.StatusNotYetEffective, Assert.Single(result.Items).Status);
    }

    [Fact]
    public async Task SearchAsync_leaves_insurance_rows_unchanged_by_certificate_revocation()
    {
        await using var dbContext = CreateDbContext();
        var vesselId = Guid.NewGuid();
        var certificate = ComplianceCertificate.Register(vesselId, "SMC", "CERT-1", "Authority", AsOf.AddYears(-1), AsOf.AddDays(10));
        certificate.Revoke(Guid.NewGuid(), "Issued in error by the flag state.", new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc));
        var policy = new InsurancePolicy(vesselId, "Insurer", "POL-1", "Hull", AsOf.AddDays(-1));
        await dbContext.AddRangeAsync(certificate, policy);
        await dbContext.SaveChangesAsync();

        var result = await QueryAsync(dbContext, AsOf, category: "Insurance");

        var item = Assert.Single(result.Items);
        Assert.Equal("POL-1", item.Reference);
        Assert.Equal("Insurer · Hull", item.Description);
        Assert.Equal("Expired", item.Status);
    }

    private static Task<ReportsComplianceExposureResult> QueryAsync(
        HarborOpsDbContext dbContext,
        DateOnly asOf,
        Guid? vesselId = null,
        string? category = null,
        int pageNumber = 1) => new ReportsComplianceExposureQuery(dbContext).SearchAsync(
            new ReportsComplianceExposureRequest
            {
                AsOf = asOf,
                VesselId = vesselId,
                Category = category,
                PageNumber = pageNumber,
            },
            CancellationToken.None);

    private static HarborOpsDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<HarborOpsDbContext>()
            .UseInMemoryDatabase($"HarborOpsComplianceQueryTests-{Guid.NewGuid()}")
            .Options;

        return new HarborOpsDbContext(options);
    }
}
