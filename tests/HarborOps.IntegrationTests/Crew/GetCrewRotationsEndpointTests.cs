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

public sealed class GetCrewRotationsEndpointTests : IntegrationTestBase
{
    private readonly HarborOpsApiFactory _application;

    public GetCrewRotationsEndpointTests(HarborOpsApiFactory application) : base(application) => _application = application;

    [Fact]
    public async Task Get_returns_an_empty_page_when_no_rotations_are_planned()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Crew.Read);

        using var response = await client.GetAsync("/api/crew/rotations");
        var page = await response.Content.ReadFromJsonAsync<GetCrewRotationsResponse>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(0, page!.TotalCount);
    }

    [Fact]
    public async Task Get_returns_planned_rotations()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Crew.Read);
        var assignmentId = await SeedRotationAsync();

        using var response = await client.GetAsync("/api/crew/rotations");
        var page = await response.Content.ReadFromJsonAsync<GetCrewRotationsResponse>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(assignmentId, Assert.Single(page!.Items).AssignmentId);
    }

    [Fact]
    public async Task Get_returns_validation_problem_when_to_date_precedes_from_date()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Crew.Read);

        using var response = await client.GetAsync("/api/crew/rotations?from=2026-07-10&to=2026-07-01");
        var problem = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(GetCrewRotationsValidator.ToField, problem!.Errors);
    }

    [Fact]
    public async Task Get_without_authentication_returns_401()
    {
        using var client = _application.CreateClient();

        using var response = await client.GetAsync("/api/crew/rotations");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_without_read_permission_returns_403()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Fleet.Read);

        using var response = await client.GetAsync("/api/crew/rotations");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private async Task<Guid> SeedRotationAsync()
    {
        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();

        var position = CrewPosition.Create("Able Seafarer", "Deck", "Senior");
        var member = CrewMember.Create(
            position.Id, "Marko Petrovic", new DateOnly(1990, 1, 1), "Montenegro",
            "IMO900001", "P1234567", new DateOnly(2030, 1, 1), "CDC1234567",
            new DateOnly(2020, 1, 1), "Active");
        var assignment = CrewAssignment.CreatePlanned(
            member.Id, Guid.NewGuid(), position.Id, null, new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31));
        var rotation = CrewRotation.Create(assignment.Id, null, "2-2", new DateOnly(2026, 7, 1));

        await dbContext.Set<CrewPosition>().AddAsync(position);
        await dbContext.Set<CrewMember>().AddAsync(member);
        await dbContext.Set<CrewAssignment>().AddAsync(assignment);
        await dbContext.Set<CrewRotation>().AddAsync(rotation);
        await dbContext.SaveChangesAsync();

        return assignment.Id;
    }
}
