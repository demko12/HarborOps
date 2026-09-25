using System.Net;
using System.Net.Http.Json;
using HarborOps.Infrastructure.Persistence;
using HarborOps.IntegrationTests.Authentication;
using HarborOps.Modules.Crew.Application;
using HarborOps.Modules.Crew.Contracts;
using HarborOps.Modules.Crew.Domain;
using HarborOps.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace HarborOps.IntegrationTests.Crew;

public sealed class QualificationPoliciesEndpointTests : IntegrationTestBase
{
    private readonly HarborOpsApiFactory _application;

    public QualificationPoliciesEndpointTests(HarborOpsApiFactory application) : base(application) => _application = application;

    [Fact]
    public async Task Post_without_authentication_returns_401()
    {
        using var client = _application.CreateClient();

        using var response = await client.PostAsJsonAsync(
            "/api/crew/qualification-policies", CreateValidRequest(Guid.NewGuid()), CancellationToken.None);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Post_without_manage_permission_returns_403()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Crew.Read);
        var position = await SeedRankAsync();

        using var response = await client.PostAsJsonAsync(
            "/api/crew/qualification-policies", CreateValidRequest(position.Id), CancellationToken.None);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Post_returns_201_for_a_new_policy()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Crew.Manage);
        var position = await SeedRankAsync();

        using var response = await client.PostAsJsonAsync(
            "/api/crew/qualification-policies", CreateValidRequest(position.Id), CancellationToken.None);
        var result = await response.Content.ReadFromJsonAsync<CreateQualificationPolicyResponse>(CancellationToken.None);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(result);
        Assert.Equal($"/api/crew/qualification-policies/{result!.Id}", response.Headers.Location?.OriginalString);
        Assert.Equal(["STCW-II/1"], result.RequiredTypes);
    }

    [Fact]
    public async Task Post_returns_404_when_the_rank_does_not_exist()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Crew.Manage);

        using var response = await client.PostAsJsonAsync(
            "/api/crew/qualification-policies", CreateValidRequest(Guid.NewGuid()), CancellationToken.None);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Post_returns_409_for_a_duplicate_policy_on_the_same_rank()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Crew.Manage);
        var position = await SeedRankAsync();
        await SeedPolicyAsync(position.Id, true, ["STCW-II/1"]);

        using var response = await client.PostAsJsonAsync(
            "/api/crew/qualification-policies", CreateValidRequest(position.Id), CancellationToken.None);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Post_returns_400_for_duplicate_required_types()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Crew.Manage);
        var position = await SeedRankAsync();

        var request = CreateValidRequest(position.Id) with
        {
            Requirements =
            [
                new QualificationRequirementInput { RequiredType = "STCW-II/1" },
                new QualificationRequirementInput { RequiredType = "STCW-II/1" },
            ],
        };

        using var response = await client.PostAsJsonAsync("/api/crew/qualification-policies", request, CancellationToken.None);
        var problem = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>(CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotNull(problem);
        Assert.Contains(QualificationRequirementValidation.RequirementsField, problem.Errors);
    }

    [Fact]
    public async Task Get_list_requires_only_the_read_permission()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Crew.Read);
        var position = await SeedRankAsync();
        await SeedPolicyAsync(position.Id, true, ["STCW-II/1"]);

        using var response = await client.GetAsync($"/api/crew/qualification-policies?crewPositionId={position.Id}", CancellationToken.None);
        var result = await response.Content.ReadFromJsonAsync<GetQualificationPoliciesResponse>(CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(result);
        Assert.Single(result!.Items);
    }

    [Fact]
    public async Task Put_replaces_requirements_and_requires_manage_permission()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Crew.Manage);
        var position = await SeedRankAsync();
        var policy = await SeedPolicyAsync(position.Id, true, ["STCW-II/1"]);

        using var response = await client.PutAsJsonAsync(
            $"/api/crew/qualification-policies/{policy.Id}",
            new UpdateQualificationPolicyRequest
            {
                Enabled = false,
                Requirements = [new QualificationRequirementInput { RequiredType = "STCW-VI/1" }],
            },
            CancellationToken.None);
        var result = await response.Content.ReadFromJsonAsync<UpdateQualificationPolicyResponse>(CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(result);
        Assert.False(result!.Enabled);
        Assert.Equal(["STCW-VI/1"], result.RequiredTypes);
    }

    [Fact]
    public async Task Put_without_manage_permission_returns_403()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Crew.Read);
        var position = await SeedRankAsync();
        var policy = await SeedPolicyAsync(position.Id, true, ["STCW-II/1"]);

        using var response = await client.PutAsJsonAsync(
            $"/api/crew/qualification-policies/{policy.Id}",
            new UpdateQualificationPolicyRequest { Enabled = true, Requirements = [] },
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Get_detail_returns_404_for_an_unknown_policy()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Crew.Read);

        using var response = await client.GetAsync($"/api/crew/qualification-policies/{Guid.NewGuid()}", CancellationToken.None);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private static CreateQualificationPolicyRequest CreateValidRequest(Guid crewPositionId) => new()
    {
        CrewPositionId = crewPositionId,
        Enabled = true,
        Requirements = [new QualificationRequirementInput { RequiredType = "STCW-II/1" }],
    };

    private async Task<CrewPosition> SeedRankAsync()
    {
        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();
        var position = CrewPosition.Create("Bosun", "Deck", "Senior");

        await dbContext.Set<CrewPosition>().AddAsync(position);
        await dbContext.SaveChangesAsync();

        return position;
    }

    private async Task<CrewQualificationPolicy> SeedPolicyAsync(Guid crewPositionId, bool enabled, IEnumerable<string> requiredTypes)
    {
        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();
        var policy = CrewQualificationPolicy.Create(crewPositionId, enabled, Guid.NewGuid(), DateTime.UtcNow);

        await dbContext.Set<CrewQualificationPolicy>().AddAsync(policy);
        foreach (var type in requiredTypes)
        {
            await dbContext.Set<CrewQualificationRequirement>().AddAsync(CrewQualificationRequirement.Create(policy.Id, type));
        }

        await dbContext.SaveChangesAsync();

        return policy;
    }
}
