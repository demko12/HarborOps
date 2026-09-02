using System.Net;
using System.Net.Http.Json;
using HarborOps.Infrastructure.Persistence;
using HarborOps.IntegrationTests.Authentication;
using HarborOps.Modules.Crew.Application;
using HarborOps.Modules.Crew.Contracts;
using HarborOps.Modules.Crew.Domain;
using HarborOps.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HarborOps.IntegrationTests.Crew;

public sealed class UpdateCrewMemberEndpointTests : IntegrationTestBase
{
    private readonly HarborOpsApiFactory _application;

    public UpdateCrewMemberEndpointTests(HarborOpsApiFactory application)
        : base(application)
    {
        _application = application;
    }

    [Fact]
    public async Task Put_updates_and_persists_mutable_fields()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Crew.Manage);
        var memberId = await SeedMemberAsync("IMO910001");
        var newRankId = await SeedPositionAsync("Chief Engineer");
        var request = CreateValidRequest(newRankId) with { FirstName = "Marko", LastName = "Marković" };

        using var response = await client.PutAsJsonAsync($"/api/crew/members/{memberId}", request);
        var result = await response.Content.ReadFromJsonAsync<UpdateCrewMemberResponse>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(result);
        Assert.Equal(memberId, result.Id);

        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();
        var updated = await dbContext.Set<CrewMember>().SingleAsync(m => m.Id == memberId);

        Assert.Equal("Marko", updated.FirstName);
        Assert.Equal("Marković", updated.LastName);
        Assert.Equal(newRankId, updated.BaseRankId);
    }

    [Fact]
    public async Task Put_leaves_the_imo_seafarer_id_untouched()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Crew.Manage);
        var memberId = await SeedMemberAsync("IMO910002");
        var rankId = await SeedPositionAsync("Bosun");
        var request = CreateValidRequest(rankId);

        using var response = await client.PutAsJsonAsync($"/api/crew/members/{memberId}", request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();
        var updated = await dbContext.Set<CrewMember>().SingleAsync(m => m.Id == memberId);

        Assert.Equal("IMO910002", updated.ImoSeafarerId);
    }

    [Fact]
    public async Task Put_returns_validation_problem_for_invalid_input()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Crew.Manage);
        var memberId = await SeedMemberAsync("IMO910003");
        var request = new UpdateCrewMemberRequest
        {
            FirstName = string.Empty,
            LastName = null,
            DateOfBirth = null,
            Nationality = null,
            PassportNumber = null,
            PassportExpiry = null,
            CdcNumber = null,
            HireDate = null,
            BaseRankId = null,
        };

        using var response = await client.PutAsJsonAsync($"/api/crew/members/{memberId}", request);
        var problem = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotNull(problem);
        Assert.Contains(UpdateCrewMemberValidator.FirstNameField, problem.Errors);
        Assert.Contains(UpdateCrewMemberValidator.BaseRankIdField, problem.Errors);
    }

    [Fact]
    public async Task Put_returns_not_found_for_an_unknown_crew_member()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Crew.Manage);
        var rankId = await SeedPositionAsync("Bosun");
        var request = CreateValidRequest(rankId);

        using var response = await client.PutAsJsonAsync($"/api/crew/members/{Guid.NewGuid()}", request);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.NotNull(problem);
    }

    [Fact]
    public async Task Put_returns_not_found_for_an_unknown_base_rank()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Crew.Manage);
        var memberId = await SeedMemberAsync("IMO910004");
        var request = CreateValidRequest(Guid.NewGuid());

        using var response = await client.PutAsJsonAsync($"/api/crew/members/{memberId}", request);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.NotNull(problem);
    }

    [Fact]
    public async Task Put_without_authentication_returns_401()
    {
        using var client = _application.CreateClient();
        var request = CreateValidRequest(Guid.NewGuid());

        using var response = await client.PutAsJsonAsync($"/api/crew/members/{Guid.NewGuid()}", request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Put_with_read_only_permission_returns_403()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Crew.Read);
        var memberId = await SeedMemberAsync("IMO910005");
        var rankId = await SeedPositionAsync("Bosun");
        var request = CreateValidRequest(rankId);

        using var response = await client.PutAsJsonAsync($"/api/crew/members/{memberId}", request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private static UpdateCrewMemberRequest CreateValidRequest(Guid baseRankId)
    {
        return new UpdateCrewMemberRequest
        {
            FirstName = "Ivan",
            LastName = "Petrović",
            DateOfBirth = new DateOnly(1990, 5, 14),
            Nationality = "Montenegro",
            PassportNumber = "P1234567",
            PassportExpiry = new DateOnly(2028, 1, 1),
            CdcNumber = "CDC987654",
            HireDate = new DateOnly(2024, 1, 15),
            BaseRankId = baseRankId,
        };
    }

    private async Task<Guid> SeedPositionAsync(string rankName)
    {
        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();
        var position = CrewPosition.Create(rankName, "Deck", "Senior");

        await dbContext.Set<CrewPosition>().AddAsync(position);
        await dbContext.SaveChangesAsync();

        return position.Id;
    }

    private async Task<Guid> SeedMemberAsync(string imoSeafarerId)
    {
        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();
        var position = CrewPosition.Create("Bosun", "Deck", "Senior");

        var member = CrewMember.Create(
            position.Id,
            "Ana",
            "Petrović",
            new DateOnly(1990, 1, 1),
            "Montenegro",
            imoSeafarerId,
            "P1234567",
            new DateOnly(2030, 1, 1),
            "CDC1234567",
            new DateOnly(2020, 1, 1),
            "Active");

        await dbContext.Set<CrewPosition>().AddAsync(position);
        await dbContext.Set<CrewMember>().AddAsync(member);
        await dbContext.SaveChangesAsync();

        return member.Id;
    }
}
