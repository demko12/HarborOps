using HarborOps.Infrastructure.Persistence;
using HarborOps.Modules.Crew.Application;
using HarborOps.Modules.Crew.Contracts;
using HarborOps.Modules.Crew.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.UnitTests.Crew;

public sealed class RegisterCrewCertificateServiceTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 8, 10, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly Today = DateOnly.FromDateTime(FixedUtcNow);

    [Fact]
    public async Task Register_rejects_invalid_input()
    {
        await using var dbContext = CreateDbContext();
        var crewMemberId = await SeedCrewMemberAsync(dbContext);
        var service = new RegisterCrewCertificateService(dbContext, new FakeDateTimeProvider(FixedUtcNow));
        var request = new RegisterCrewCertificateRequest
        {
            Type = " ",
            Name = string.Empty,
            IssuingAuthority = null,
            LicenseNumber = " ",
            IssueDate = null,
            ExpiryDate = null,
        };

        var exception = await Assert.ThrowsAsync<RegisterCrewCertificateValidationException>(
            () => service.RegisterAsync(request, crewMemberId, CancellationToken.None));

        Assert.Equal(6, exception.Errors.Count);
        Assert.Contains(RegisterCrewCertificateValidator.TypeField, exception.Errors);
        Assert.Contains(RegisterCrewCertificateValidator.NameField, exception.Errors);
        Assert.Contains(RegisterCrewCertificateValidator.IssuingAuthorityField, exception.Errors);
        Assert.Contains(RegisterCrewCertificateValidator.LicenseNumberField, exception.Errors);
        Assert.Contains(RegisterCrewCertificateValidator.IssueDateField, exception.Errors);
        Assert.Contains(RegisterCrewCertificateValidator.ExpiryDateField, exception.Errors);
    }

    [Fact]
    public async Task Register_rejects_empty_crew_member_id()
    {
        await using var dbContext = CreateDbContext();
        var service = new RegisterCrewCertificateService(dbContext, new FakeDateTimeProvider(FixedUtcNow));
        var request = CreateValidRequest("STCW-0001", Today.AddYears(1));

        var exception = await Assert.ThrowsAsync<RegisterCrewCertificateValidationException>(
            () => service.RegisterAsync(request, Guid.Empty, CancellationToken.None));

        Assert.Contains(RegisterCrewCertificateValidator.CrewMemberIdField, exception.Errors);
    }

    [Fact]
    public async Task Register_rejects_unknown_crew_member()
    {
        await using var dbContext = CreateDbContext();
        var service = new RegisterCrewCertificateService(dbContext, new FakeDateTimeProvider(FixedUtcNow));
        var request = CreateValidRequest("STCW-0002", Today.AddYears(1));
        var unknownCrewMemberId = Guid.NewGuid();

        var exception = await Assert.ThrowsAsync<CrewMemberNotFoundException>(
            () => service.RegisterAsync(request, unknownCrewMemberId, CancellationToken.None));

        Assert.Equal(unknownCrewMemberId, exception.CrewMemberId);
    }

    [Fact]
    public async Task Register_rejects_expiry_date_before_issue_date()
    {
        await using var dbContext = CreateDbContext();
        var crewMemberId = await SeedCrewMemberAsync(dbContext);
        var service = new RegisterCrewCertificateService(dbContext, new FakeDateTimeProvider(FixedUtcNow));
        var request = CreateValidRequest("STCW-0003", Today.AddYears(1)) with
        {
            IssueDate = new DateOnly(2026, 6, 1),
            ExpiryDate = new DateOnly(2026, 1, 1),
        };

        var exception = await Assert.ThrowsAsync<RegisterCrewCertificateValidationException>(
            () => service.RegisterAsync(request, crewMemberId, CancellationToken.None));

        Assert.Contains(RegisterCrewCertificateValidator.ExpiryDateField, exception.Errors);
    }

    [Fact]
    public async Task Register_accepts_expiry_date_equal_to_issue_date()
    {
        await using var dbContext = CreateDbContext();
        var crewMemberId = await SeedCrewMemberAsync(dbContext);
        var service = new RegisterCrewCertificateService(dbContext, new FakeDateTimeProvider(FixedUtcNow));
        var sameDate = new DateOnly(2026, 6, 1);
        var request = CreateValidRequest("STCW-0004", sameDate) with { IssueDate = sameDate };

        var response = await service.RegisterAsync(request, crewMemberId, CancellationToken.None);

        Assert.NotEqual(Guid.Empty, response.Id);
    }

    [Fact]
    public async Task Register_normalizes_type_name_issuing_authority_and_license_number()
    {
        await using var dbContext = CreateDbContext();
        var crewMemberId = await SeedCrewMemberAsync(dbContext);
        var service = new RegisterCrewCertificateService(dbContext, new FakeDateTimeProvider(FixedUtcNow));
        var request = new RegisterCrewCertificateRequest
        {
            Type = " STCW Basic Safety Training ",
            Name = " Basic Safety Training Certificate ",
            IssuingAuthority = " Flag State Maritime Authority ",
            LicenseNumber = " stcw-0005 ",
            IssueDate = new DateOnly(2024, 1, 1),
            ExpiryDate = Today.AddYears(1),
        };

        var response = await service.RegisterAsync(request, crewMemberId, CancellationToken.None);

        var certificate = await dbContext
            .Set<CrewCertificate>()
            .SingleAsync(candidate => candidate.Id == response.Id, CancellationToken.None);

        Assert.Equal("STCW Basic Safety Training", certificate.Type);
        Assert.Equal("Basic Safety Training Certificate", certificate.Name);
        Assert.Equal("Flag State Maritime Authority", certificate.IssuingAuthority);
        Assert.Equal("STCW-0005", certificate.LicenseNumber);
    }

    [Fact]
    public async Task Register_computes_valid_status_when_expiry_date_is_today_or_later()
    {
        await using var dbContext = CreateDbContext();
        var crewMemberId = await SeedCrewMemberAsync(dbContext);
        var service = new RegisterCrewCertificateService(dbContext, new FakeDateTimeProvider(FixedUtcNow));
        var request = CreateValidRequest("STCW-0006", Today);

        var response = await service.RegisterAsync(request, crewMemberId, CancellationToken.None);

        var certificate = await dbContext
            .Set<CrewCertificate>()
            .SingleAsync(candidate => candidate.Id == response.Id, CancellationToken.None);

        Assert.Equal("Valid", certificate.Status);
    }

    [Fact]
    public async Task Register_computes_expired_status_when_expiry_date_is_before_today()
    {
        await using var dbContext = CreateDbContext();
        var crewMemberId = await SeedCrewMemberAsync(dbContext);
        var service = new RegisterCrewCertificateService(dbContext, new FakeDateTimeProvider(FixedUtcNow));
        var expiryDate = Today.AddDays(-1);
        var request = CreateValidRequest("STCW-0007", expiryDate) with { IssueDate = expiryDate.AddYears(-1) };

        var response = await service.RegisterAsync(request, crewMemberId, CancellationToken.None);

        var certificate = await dbContext
            .Set<CrewCertificate>()
            .SingleAsync(candidate => candidate.Id == response.Id, CancellationToken.None);

        Assert.Equal("Expired", certificate.Status);
    }

    [Fact]
    public async Task Register_rejects_duplicate_license_number_ignoring_case_and_whitespace()
    {
        await using var dbContext = CreateDbContext();
        var crewMemberId = await SeedCrewMemberAsync(dbContext);
        var service = new RegisterCrewCertificateService(dbContext, new FakeDateTimeProvider(FixedUtcNow));
        var firstRequest = CreateValidRequest("STCW-0008", Today.AddYears(1));
        var duplicateRequest = CreateValidRequest(" stcw-0008 ", Today.AddYears(1)) with { Name = "Different name" };

        await service.RegisterAsync(firstRequest, crewMemberId, CancellationToken.None);

        var exception = await Assert.ThrowsAsync<DuplicateCrewCertificateLicenseException>(
            () => service.RegisterAsync(duplicateRequest, crewMemberId, CancellationToken.None));

        Assert.Equal(crewMemberId, exception.CrewMemberId);
        Assert.Equal("STCW-0008", exception.LicenseNumber);
    }

    [Fact]
    public async Task Register_allows_same_license_number_for_different_crew_members()
    {
        await using var dbContext = CreateDbContext();
        var firstCrewMemberId = await SeedCrewMemberAsync(dbContext);
        var secondCrewMemberId = await SeedCrewMemberAsync(dbContext);
        var service = new RegisterCrewCertificateService(dbContext, new FakeDateTimeProvider(FixedUtcNow));
        var firstRequest = CreateValidRequest("STCW-0009", Today.AddYears(1));
        var secondRequest = CreateValidRequest("STCW-0009", Today.AddYears(1));

        await service.RegisterAsync(firstRequest, firstCrewMemberId, CancellationToken.None);
        var response = await service.RegisterAsync(secondRequest, secondCrewMemberId, CancellationToken.None);

        Assert.NotEqual(Guid.Empty, response.Id);
        var certificateCount = await dbContext.Set<CrewCertificate>().CountAsync(CancellationToken.None);
        Assert.Equal(2, certificateCount);
    }

    private static RegisterCrewCertificateRequest CreateValidRequest(string licenseNumber, DateOnly expiryDate)
    {
        return new RegisterCrewCertificateRequest
        {
            Type = "STCW Basic Safety Training",
            Name = "Basic Safety Training Certificate",
            IssuingAuthority = "Flag State Maritime Authority",
            LicenseNumber = licenseNumber,
            IssueDate = new DateOnly(2024, 1, 1),
            ExpiryDate = expiryDate,
        };
    }

    private static async Task<Guid> SeedCrewMemberAsync(HarborOpsDbContext dbContext)
    {
        var baseRankId = await SeedCrewPositionAsync(dbContext);
        var crewMember = CrewMember.Create(
            baseRankId,
            "Ivan",
            "Petrović",
            new DateOnly(1990, 5, 14),
            "Montenegro",
            $"IMO{Guid.NewGuid():N}"[..12],
            "P1234567",
            new DateOnly(2028, 1, 1),
            "CDC987654",
            new DateOnly(2024, 1, 15),
            "Active");

        await dbContext.Set<CrewMember>().AddAsync(crewMember);
        await dbContext.SaveChangesAsync();

        return crewMember.Id;
    }

    private static async Task<Guid> SeedCrewPositionAsync(HarborOpsDbContext dbContext)
    {
        var crewPosition = CrewPosition.Create("Bosun", "Deck", "Senior");

        await dbContext.Set<CrewPosition>().AddAsync(crewPosition);
        await dbContext.SaveChangesAsync();

        return crewPosition.Id;
    }

    private static HarborOpsDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<HarborOpsDbContext>()
            .UseInMemoryDatabase($"HarborOpsUnitTests-{Guid.NewGuid()}")
            .Options;

        return new HarborOpsDbContext(options);
    }
}
