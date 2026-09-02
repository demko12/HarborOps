using HarborOps.Infrastructure.Persistence;
using HarborOps.Modules.Crew.Application;
using HarborOps.Modules.Crew.Contracts;
using HarborOps.Modules.Crew.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.UnitTests.Crew;

public sealed class RegisterCrewMemberServiceTests
{
    [Fact]
    public async Task Register_rejects_invalid_input()
    {
        await using var dbContext = CreateDbContext();
        var service = new RegisterCrewMemberService(dbContext);
        var request = new RegisterCrewMemberRequest
        {
            BaseRankId = null,
            FirstName = " ",
            LastName = " ",
            DateOfBirth = null,
            Nationality = null,
            ImoSeafarerId = string.Empty,
            PassportNumber = null,
            PassportExpiry = null,
            CdcNumber = null,
            HireDate = null,
            EmploymentStatus = null,
        };

        var exception = await Assert.ThrowsAsync<RegisterCrewMemberValidationException>(
            () => service.RegisterAsync(request, CancellationToken.None));

        Assert.Equal(11, exception.Errors.Count);
        Assert.Contains(RegisterCrewMemberValidator.BaseRankIdField, exception.Errors);
        Assert.Contains(RegisterCrewMemberValidator.FirstNameField, exception.Errors);
        Assert.Contains(RegisterCrewMemberValidator.LastNameField, exception.Errors);
        Assert.Contains(RegisterCrewMemberValidator.DateOfBirthField, exception.Errors);
        Assert.Contains(RegisterCrewMemberValidator.NationalityField, exception.Errors);
        Assert.Contains(RegisterCrewMemberValidator.ImoSeafarerIdField, exception.Errors);
        Assert.Contains(RegisterCrewMemberValidator.PassportNumberField, exception.Errors);
        Assert.Contains(RegisterCrewMemberValidator.PassportExpiryField, exception.Errors);
        Assert.Contains(RegisterCrewMemberValidator.CdcNumberField, exception.Errors);
        Assert.Contains(RegisterCrewMemberValidator.HireDateField, exception.Errors);
        Assert.Contains(RegisterCrewMemberValidator.EmploymentStatusField, exception.Errors);
    }

    [Fact]
    public async Task Register_rejects_an_employment_status_outside_the_supported_vocabulary()
    {
        await using var dbContext = CreateDbContext();
        var baseRankId = await SeedCrewPositionAsync(dbContext);
        var service = new RegisterCrewMemberService(dbContext);
        var request = CreateValidRequest("IMO000009", baseRankId) with { EmploymentStatus = "Inactive" };

        var exception = await Assert.ThrowsAsync<RegisterCrewMemberValidationException>(
            () => service.RegisterAsync(request, CancellationToken.None));

        Assert.Contains(RegisterCrewMemberValidator.EmploymentStatusField, exception.Errors);
    }

    [Fact]
    public async Task Register_rejects_future_date_of_birth()
    {
        await using var dbContext = CreateDbContext();
        var baseRankId = await SeedCrewPositionAsync(dbContext);
        var service = new RegisterCrewMemberService(dbContext);
        var request = CreateValidRequest("IMO000001", baseRankId) with
        {
            DateOfBirth = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1)),
        };

        var exception = await Assert.ThrowsAsync<RegisterCrewMemberValidationException>(
            () => service.RegisterAsync(request, CancellationToken.None));

        Assert.Contains(RegisterCrewMemberValidator.DateOfBirthField, exception.Errors);
    }

    [Fact]
    public async Task Register_rejects_duplicate_imo_seafarer_id()
    {
        await using var dbContext = CreateDbContext();
        var baseRankId = await SeedCrewPositionAsync(dbContext);
        var service = new RegisterCrewMemberService(dbContext);
        var firstRequest = CreateValidRequest("IMO000002", baseRankId);
        var duplicateRequest = firstRequest with { FirstName = "Marko", LastName = "Marković" };

        await service.RegisterAsync(firstRequest, CancellationToken.None);

        var exception = await Assert.ThrowsAsync<DuplicateImoSeafarerIdException>(
            () => service.RegisterAsync(duplicateRequest, CancellationToken.None));

        Assert.Equal("IMO000002", exception.ImoSeafarerId);
    }

    [Fact]
    public async Task Register_rejects_unknown_base_rank()
    {
        await using var dbContext = CreateDbContext();
        var service = new RegisterCrewMemberService(dbContext);
        var request = CreateValidRequest("IMO000003", Guid.NewGuid());

        var exception = await Assert.ThrowsAsync<CrewPositionNotFoundException>(
            () => service.RegisterAsync(request, CancellationToken.None));

        Assert.Equal(request.BaseRankId, exception.BaseRankId);
    }

    [Fact]
    public async Task Register_persists_crew_member_and_returns_id()
    {
        await using var dbContext = CreateDbContext();
        var baseRankId = await SeedCrewPositionAsync(dbContext);
        var service = new RegisterCrewMemberService(dbContext);
        var request = CreateValidRequest("IMO000004", baseRankId);

        var response = await service.RegisterAsync(request, CancellationToken.None);

        var crewMember = await dbContext
            .Set<CrewMember>()
            .SingleAsync(CancellationToken.None);

        Assert.NotEqual(Guid.Empty, response.Id);
        Assert.Equal(response.Id, crewMember.Id);
        Assert.Equal(baseRankId, crewMember.BaseRankId);
        Assert.Equal("Ivan", crewMember.FirstName);
        Assert.Equal("Petrović", crewMember.LastName);
        Assert.Equal(new DateOnly(1990, 5, 14), crewMember.DateOfBirth);
        Assert.Equal("Montenegro", crewMember.Nationality);
        Assert.Equal("IMO000004", crewMember.ImoSeafarerId);
        Assert.Equal("P1234567", crewMember.PassportNumber);
        Assert.Equal(new DateOnly(2028, 1, 1), crewMember.PassportExpiry);
        Assert.Equal("CDC987654", crewMember.CdcNumber);
        Assert.Equal(new DateOnly(2024, 1, 15), crewMember.HireDate);
        Assert.Equal("Active", crewMember.EmploymentStatus);
    }

    private static RegisterCrewMemberRequest CreateValidRequest(string imoSeafarerId, Guid baseRankId)
    {
        return new RegisterCrewMemberRequest
        {
            BaseRankId = baseRankId,
            FirstName = " Ivan ",
            LastName = " Petrović ",
            DateOfBirth = new DateOnly(1990, 5, 14),
            Nationality = " Montenegro ",
            ImoSeafarerId = $" {imoSeafarerId} ",
            PassportNumber = " P1234567 ",
            PassportExpiry = new DateOnly(2028, 1, 1),
            CdcNumber = " CDC987654 ",
            HireDate = new DateOnly(2024, 1, 15),
            EmploymentStatus = " Active ",
        };
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
