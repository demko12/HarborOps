using System.Net;
using System.Net.Http.Json;
using HarborOps.Infrastructure.Persistence;
using HarborOps.Modules.Crew;
using HarborOps.Modules.Crew.Application;
using HarborOps.Modules.Crew.Contracts;
using HarborOps.Modules.Crew.Domain;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HarborOps.IntegrationTests.Crew;

public sealed class RegisterCrewMemberEndpointTests : IClassFixture<HarborOpsApiFactory>
{
    private readonly HarborOpsApiFactory _application;

    public RegisterCrewMemberEndpointTests(HarborOpsApiFactory application)
    {
        _application = application;
    }

    [Fact]
    public async Task Post_registers_and_persists_crew_member()
    {
        using var client = _application.CreateClient();
        var baseRankId = await SeedCrewPositionAsync();
        var request = CreateValidRequest("IMO900001", baseRankId);

        using var response = await client.PostAsJsonAsync(
            "/api/crew/members",
            request,
            CancellationToken.None);
        var result = await response.Content.ReadFromJsonAsync<RegisterCrewMemberResponse>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(result);
        Assert.NotEqual(Guid.Empty, result.Id);
        Assert.Equal($"/api/crew/members/{result.Id}", response.Headers.Location?.OriginalString);

        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();
        var crewMember = await dbContext
            .Set<CrewMember>()
            .SingleAsync(
                candidate => candidate.Id == result.Id,
                CancellationToken.None);
        var entityType = dbContext.Model.FindEntityType(typeof(CrewMember));

        Assert.Equal(request.Name, crewMember.Name);
        Assert.Equal(request.BaseRankId, crewMember.BaseRankId);
        Assert.Equal(request.Nationality, crewMember.Nationality);
        Assert.Equal(request.ImoSeafarerId, crewMember.ImoSeafarerId);
        Assert.Equal(request.PassportNumber, crewMember.PassportNumber);
        Assert.Equal(request.CdcNumber, crewMember.CdcNumber);
        Assert.Equal(request.EmploymentStatus, crewMember.EmploymentStatus);
        Assert.Equal("CrewMembers", entityType!.GetTableName());
        Assert.Equal(CrewModule.Schema, entityType!.GetSchema());
    }

    [Fact]
    public async Task Post_returns_validation_problem_for_invalid_input()
    {
        using var client = _application.CreateClient();
        var request = new RegisterCrewMemberRequest
        {
            BaseRankId = null,
            Name = string.Empty,
            DateOfBirth = null,
            Nationality = null,
            ImoSeafarerId = null,
            PassportNumber = null,
            PassportExpiry = null,
            CdcNumber = null,
            HireDate = null,
            EmploymentStatus = null,
        };

        using var response = await client.PostAsJsonAsync(
            "/api/crew/members",
            request,
            CancellationToken.None);
        var problem = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotNull(problem);
        Assert.Contains(RegisterCrewMemberValidator.BaseRankIdField, problem.Errors);
        Assert.Contains(RegisterCrewMemberValidator.NameField, problem.Errors);
        Assert.Contains(RegisterCrewMemberValidator.DateOfBirthField, problem.Errors);
        Assert.Contains(RegisterCrewMemberValidator.NationalityField, problem.Errors);
        Assert.Contains(RegisterCrewMemberValidator.ImoSeafarerIdField, problem.Errors);
    }

    [Fact]
    public async Task Post_returns_conflict_for_duplicate_imo_seafarer_id()
    {
        using var client = _application.CreateClient();
        var baseRankId = await SeedCrewPositionAsync();
        var firstRequest = CreateValidRequest("IMO900002", baseRankId);
        var duplicateRequest = firstRequest with { Name = "Marko Marković" };

        using var firstResponse = await client.PostAsJsonAsync(
            "/api/crew/members",
            firstRequest,
            CancellationToken.None);
        Assert.Equal(HttpStatusCode.Created, firstResponse.StatusCode);

        using var response = await client.PostAsJsonAsync(
            "/api/crew/members",
            duplicateRequest,
            CancellationToken.None);
        var problem = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.NotNull(problem);
        Assert.Contains(RegisterCrewMemberValidator.ImoSeafarerIdField, problem.Errors);
    }

    [Fact]
    public async Task Post_returns_bad_request_for_unknown_base_rank()
    {
        using var client = _application.CreateClient();
        var request = CreateValidRequest("IMO900003", Guid.NewGuid());

        using var response = await client.PostAsJsonAsync(
            "/api/crew/members",
            request,
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private static RegisterCrewMemberRequest CreateValidRequest(string imoSeafarerId, Guid baseRankId)
    {
        return new RegisterCrewMemberRequest
        {
            BaseRankId = baseRankId,
            Name = "Ivan Petrović",
            DateOfBirth = new DateOnly(1990, 5, 14),
            Nationality = "Montenegro",
            ImoSeafarerId = imoSeafarerId,
            PassportNumber = "P1234567",
            PassportExpiry = new DateOnly(2028, 1, 1),
            CdcNumber = "CDC987654",
            HireDate = new DateOnly(2024, 1, 15),
            EmploymentStatus = "Active",
        };
    }

    private async Task<Guid> SeedCrewPositionAsync()
    {
        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();
        var crewPosition = CrewPosition.Create("Bosun", "Deck", "Senior");

        await dbContext.Set<CrewPosition>().AddAsync(crewPosition);
        await dbContext.SaveChangesAsync();

        return crewPosition.Id;
    }
}
