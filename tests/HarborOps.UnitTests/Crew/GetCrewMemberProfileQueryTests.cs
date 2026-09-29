using HarborOps.Infrastructure.Persistence;
using HarborOps.Modules.Crew.Application;
using HarborOps.Modules.Crew.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.UnitTests.Crew;

public sealed class GetCrewMemberProfileQueryTests
{
    [Fact]
    public async Task GetAsync_throws_when_crew_member_not_found()
    {
        await using var dbContext = CreateDbContext();
        var query = new GetCrewMemberProfileQuery(dbContext);

        var exception = await Assert.ThrowsAsync<CrewMemberNotFoundException>(
            () => query.GetAsync(Guid.NewGuid(), CancellationToken.None));

        Assert.NotEqual(Guid.Empty, exception.CrewMemberId);
    }

    [Fact]
    public async Task GetAsync_returns_profile_with_empty_sections_when_no_certificates_or_assignments()
    {
        await using var dbContext = CreateDbContext();
        var position = await SeedCrewPositionAsync(dbContext, "Bosun");
        var member = await SeedCrewMemberAsync(dbContext, position.Id, "IMO100001");
        var query = new GetCrewMemberProfileQuery(dbContext);

        var profile = await query.GetAsync(member.Id, CancellationToken.None);

        Assert.Equal(member.Id, profile.CrewMemberId);
        Assert.Equal(position.Id, profile.BaseRankId);
        Assert.Equal(position.RankName, profile.BaseRankName);
        Assert.Empty(profile.Certificates);
        Assert.Empty(profile.Assignments);
    }

    [Fact]
    public async Task GetAsync_orders_certificates_by_expiry_date_then_id()
    {
        await using var dbContext = CreateDbContext();
        var position = await SeedCrewPositionAsync(dbContext, "Bosun");
        var member = await SeedCrewMemberAsync(dbContext, position.Id, "IMO100002");

        var laterExpiry = CrewCertificate.Create(
            member.Id, "STCW", "Basic Safety", "Authority", "LIC-LATER",
            new DateOnly(2024, 1, 1), new DateOnly(2027, 1, 1), "Valid");
        var earlierExpiry = CrewCertificate.Create(
            member.Id, "STCW", "Advanced Firefighting", "Authority", "LIC-EARLIER",
            new DateOnly(2024, 1, 1), new DateOnly(2026, 1, 1), "Valid");

        await dbContext.Set<CrewCertificate>().AddRangeAsync(laterExpiry, earlierExpiry);
        await dbContext.SaveChangesAsync();

        var query = new GetCrewMemberProfileQuery(dbContext);
        var profile = await query.GetAsync(member.Id, CancellationToken.None);

        Assert.Equal(2, profile.Certificates.Count);
        Assert.Equal(earlierExpiry.Id, profile.Certificates[0].CertificateId);
        Assert.Equal(laterExpiry.Id, profile.Certificates[1].CertificateId);
    }

    [Fact]
    public async Task GetAsync_orders_assignments_by_sign_on_date_descending()
    {
        await using var dbContext = CreateDbContext();
        var position = await SeedCrewPositionAsync(dbContext, "Bosun");
        var member = await SeedCrewMemberAsync(dbContext, position.Id, "IMO100003");

        var olderAssignment = CrewAssignment.CreatePlanned(
            member.Id, Guid.NewGuid(), position.Id, null,
            new DateOnly(2023, 1, 1), new DateOnly(2023, 6, 1));
        var newerAssignment = CrewAssignment.CreatePlanned(
            member.Id, Guid.NewGuid(), position.Id, null,
            new DateOnly(2024, 1, 1), new DateOnly(2024, 6, 1));

        await dbContext.Set<CrewAssignment>().AddRangeAsync(olderAssignment, newerAssignment);
        await dbContext.SaveChangesAsync();

        var query = new GetCrewMemberProfileQuery(dbContext);
        var profile = await query.GetAsync(member.Id, CancellationToken.None);

        Assert.Equal(2, profile.Assignments.Count);
        Assert.Equal(newerAssignment.Id, profile.Assignments[0].AssignmentId);
        Assert.Equal(olderAssignment.Id, profile.Assignments[1].AssignmentId);
    }

    [Fact]
    public async Task GetAsync_is_isolated_by_crew_member()
    {
        await using var dbContext = CreateDbContext();
        var position = await SeedCrewPositionAsync(dbContext, "Bosun");
        var member = await SeedCrewMemberAsync(dbContext, position.Id, "IMO100004");
        var otherMember = await SeedCrewMemberAsync(dbContext, position.Id, "IMO100005");

        var ownCertificate = CrewCertificate.Create(
            member.Id, "STCW", "Basic Safety", "Authority", "LIC-OWN",
            new DateOnly(2024, 1, 1), new DateOnly(2026, 1, 1), "Valid");
        var otherCertificate = CrewCertificate.Create(
            otherMember.Id, "STCW", "Basic Safety", "Authority", "LIC-OTHER",
            new DateOnly(2024, 1, 1), new DateOnly(2026, 1, 1), "Valid");

        await dbContext.Set<CrewCertificate>().AddRangeAsync(ownCertificate, otherCertificate);
        await dbContext.SaveChangesAsync();

        var query = new GetCrewMemberProfileQuery(dbContext);
        var profile = await query.GetAsync(member.Id, CancellationToken.None);

        Assert.Single(profile.Certificates);
        Assert.Equal(ownCertificate.Id, profile.Certificates[0].CertificateId);
    }

    [Fact]
    public async Task GetAsync_reports_predecessor_successor_and_revocation_state_for_certificates()
    {
        await using var dbContext = CreateDbContext();
        var position = await SeedCrewPositionAsync(dbContext, "Bosun");
        var member = await SeedCrewMemberAsync(dbContext, position.Id, "IMO100006");

        var original = CrewCertificate.Create(
            member.Id, "STCW", "Basic Safety", "Authority", "LIC-ORIG",
            new DateOnly(2020, 1, 1), new DateOnly(2024, 1, 1), "Expired");
        var renewal = CrewCertificate.CreateRenewal(
            original.Id, member.Id, "STCW", "Basic Safety", "Authority", "LIC-RENEW",
            new DateOnly(2024, 1, 1), new DateOnly(2028, 1, 1), "Valid");
        var revoked = CrewCertificate.Create(
            member.Id, "Medical", "Fitness Certificate", "Authority", "LIC-REVOKED",
            new DateOnly(2024, 1, 1), new DateOnly(2028, 1, 1), "Valid");
        revoked.Revoke(Guid.NewGuid(), "Issued in error", new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        await dbContext.Set<CrewCertificate>().AddRangeAsync(original, renewal, revoked);
        await dbContext.SaveChangesAsync();

        var query = new GetCrewMemberProfileQuery(dbContext);
        var profile = await query.GetAsync(member.Id, CancellationToken.None);

        var originalItem = Assert.Single(profile.Certificates, item => item.CertificateId == original.Id);
        Assert.False(originalItem.IsRevoked);
        Assert.Null(originalItem.PredecessorCertificateId);
        Assert.Equal(renewal.Id, originalItem.SuccessorCertificateId);

        var renewalItem = Assert.Single(profile.Certificates, item => item.CertificateId == renewal.Id);
        Assert.Equal(original.Id, renewalItem.PredecessorCertificateId);
        Assert.Null(renewalItem.SuccessorCertificateId);

        var revokedItem = Assert.Single(profile.Certificates, item => item.CertificateId == revoked.Id);
        Assert.True(revokedItem.IsRevoked);
    }

    private static async Task<CrewPosition> SeedCrewPositionAsync(HarborOpsDbContext dbContext, string rankName)
    {
        var position = CrewPosition.Create(rankName, "Deck", "Senior");
        await dbContext.Set<CrewPosition>().AddAsync(position);
        await dbContext.SaveChangesAsync();
        return position;
    }

    private static async Task<CrewMember> SeedCrewMemberAsync(HarborOpsDbContext dbContext, Guid baseRankId, string imoSeafarerId)
    {
        var member = CrewMember.Create(
            baseRankId,
            "Ivan",
            "Petrović",
            new DateOnly(1990, 5, 14),
            "Montenegro",
            imoSeafarerId,
            "P1234567",
            new DateOnly(2028, 1, 1),
            "CDC987654",
            new DateOnly(2024, 1, 15),
            "Active");

        await dbContext.Set<CrewMember>().AddAsync(member);
        await dbContext.SaveChangesAsync();
        return member;
    }

    private static HarborOpsDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<HarborOpsDbContext>()
            .UseInMemoryDatabase($"HarborOpsUnitTests-{Guid.NewGuid()}")
            .Options;

        return new HarborOpsDbContext(options);
    }
}
