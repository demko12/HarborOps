using System.Net;
using System.Net.Http.Json;
using HarborOps.Infrastructure.Persistence;
using HarborOps.IntegrationTests.Authentication;
using HarborOps.Modules.Crew.Application;
using HarborOps.Modules.Crew.Contracts;
using HarborOps.Modules.Crew.Domain;
using HarborOps.Modules.Fleet.Domain;
using HarborOps.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HarborOps.IntegrationTests.Crew;

public sealed class VesselManningPlansEndpointTests : IntegrationTestBase
{
    private readonly HarborOpsApiFactory _application;

    public VesselManningPlansEndpointTests(HarborOpsApiFactory application)
        : base(application)
    {
        _application = application;
    }

    [Fact]
    public async Task Post_without_authentication_returns_401()
    {
        using var client = _application.CreateClient();

        using var response = await client.PostAsJsonAsync(
            "/api/crew/manning-plans", CreateValidRequest(Guid.NewGuid(), Guid.NewGuid()), CancellationToken.None);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Post_without_manage_permission_returns_403()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Crew.Read);
        var vessel = await SeedVesselAsync();
        var rank = await SeedRankAsync();

        using var response = await client.PostAsJsonAsync(
            "/api/crew/manning-plans", CreateValidRequest(vessel.Id, rank.Id), CancellationToken.None);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Post_returns_201_and_validates_the_vessel_through_the_fleet_lookup()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Crew.Manage);
        var vessel = await SeedVesselAsync();
        var rank = await SeedRankAsync();

        using var response = await client.PostAsJsonAsync(
            "/api/crew/manning-plans", CreateValidRequest(vessel.Id, rank.Id), CancellationToken.None);
        var result = await response.Content.ReadFromJsonAsync<CreateVesselManningPlanResponse>(CancellationToken.None);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(result);
        Assert.Equal($"/api/crew/manning-plans/{result!.Id}", response.Headers.Location?.OriginalString);
        Assert.Equal(vessel.Id, result.VesselId);
        Assert.Single(result.Lines);
    }

    [Fact]
    public async Task Post_returns_404_when_the_vessel_does_not_exist_in_fleet()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Crew.Manage);
        var rank = await SeedRankAsync();

        using var response = await client.PostAsJsonAsync(
            "/api/crew/manning-plans", CreateValidRequest(Guid.NewGuid(), rank.Id), CancellationToken.None);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Post_returns_404_when_the_rank_does_not_exist()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Crew.Manage);
        var vessel = await SeedVesselAsync();

        using var response = await client.PostAsJsonAsync(
            "/api/crew/manning-plans", CreateValidRequest(vessel.Id, Guid.NewGuid()), CancellationToken.None);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Post_returns_400_for_invalid_input()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Crew.Manage);

        using var response = await client.PostAsJsonAsync(
            "/api/crew/manning-plans",
            new CreateVesselManningPlanRequest { VesselId = null, EffectiveStartDate = null, EffectiveEndDate = null, Lines = null },
            CancellationToken.None);
        var problem = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>(CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotNull(problem);
        Assert.Contains(CreateVesselManningPlanValidator.VesselIdField, problem.Errors);
        Assert.Contains(ManningPlanLineValidation.LinesField, problem.Errors);
    }

    [Fact]
    public async Task Post_returns_409_for_an_overlapping_plan_on_the_same_vessel()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Crew.Manage);
        var vessel = await SeedVesselAsync();
        var rank = await SeedRankAsync();
        await SeedPlanAsync(vessel.Id, rank.Id, new DateOnly(2026, 1, 1), new DateOnly(2026, 3, 31));

        using var response = await client.PostAsJsonAsync(
            "/api/crew/manning-plans", CreateValidRequest(vessel.Id, rank.Id), CancellationToken.None);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Get_list_requires_only_the_read_permission()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Crew.Read);
        var vessel = await SeedVesselAsync();
        var rank = await SeedRankAsync();
        await SeedPlanAsync(vessel.Id, rank.Id, new DateOnly(2026, 1, 1), new DateOnly(2026, 3, 31));

        using var response = await client.GetAsync($"/api/crew/manning-plans?vesselId={vessel.Id}", CancellationToken.None);
        var result = await response.Content.ReadFromJsonAsync<GetVesselManningPlansResponse>(CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(result);
        Assert.Single(result!.Items);
    }

    [Fact]
    public async Task Put_lines_replaces_the_lines_and_requires_manage_permission()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Crew.Manage);
        var vessel = await SeedVesselAsync();
        var rank = await SeedRankAsync();
        var plan = await SeedPlanAsync(vessel.Id, rank.Id, new DateOnly(2026, 1, 1), new DateOnly(2026, 3, 31));

        using var response = await client.PutAsJsonAsync(
            $"/api/crew/manning-plans/{plan.Id}/lines",
            new ReplaceVesselManningPlanLinesRequest
            {
                Reason = "Revised headcount.",
                Lines = [new ManningPlanLineInput { RankId = rank.Id, RequiredHeadcount = 5 }],
            },
            CancellationToken.None);
        var result = await response.Content.ReadFromJsonAsync<ReplaceVesselManningPlanLinesResponse>(CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(result);
        Assert.Equal(5, result!.Lines.Single().RequiredHeadcount);
    }

    [Fact]
    public async Task Put_lines_without_manage_permission_returns_403()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Crew.Read);
        var vessel = await SeedVesselAsync();
        var rank = await SeedRankAsync();
        var plan = await SeedPlanAsync(vessel.Id, rank.Id, new DateOnly(2026, 1, 1), new DateOnly(2026, 3, 31));

        using var response = await client.PutAsJsonAsync(
            $"/api/crew/manning-plans/{plan.Id}/lines",
            new ReplaceVesselManningPlanLinesRequest
            {
                Reason = "Revised headcount.",
                Lines = [new ManningPlanLineInput { RankId = rank.Id, RequiredHeadcount = 5 }],
            },
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Post_end_date_shortens_the_plan()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Crew.Manage);
        var vessel = await SeedVesselAsync();
        var rank = await SeedRankAsync();
        var plan = await SeedPlanAsync(vessel.Id, rank.Id, new DateOnly(2026, 1, 1), new DateOnly(2026, 3, 31));

        using var response = await client.PostAsJsonAsync(
            $"/api/crew/manning-plans/{plan.Id}/end-date",
            new EndVesselManningPlanRequest { NewEndDate = new DateOnly(2026, 2, 1), Reason = "Vessel laid up." },
            CancellationToken.None);
        var result = await response.Content.ReadFromJsonAsync<EndVesselManningPlanResponse>(CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(result);
        Assert.Equal(new DateOnly(2026, 2, 1), result!.EffectiveEndDate);
    }

    [Fact]
    public async Task Get_detail_returns_404_for_an_unknown_plan()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Crew.Read);

        using var response = await client.GetAsync($"/api/crew/manning-plans/{Guid.NewGuid()}", CancellationToken.None);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private static CreateVesselManningPlanRequest CreateValidRequest(Guid vesselId, Guid rankId) => new()
    {
        VesselId = vesselId,
        EffectiveStartDate = new DateOnly(2026, 1, 1),
        EffectiveEndDate = new DateOnly(2026, 3, 31),
        Lines = [new ManningPlanLineInput { RankId = rankId, RequiredHeadcount = 4 }],
    };

    private async Task<Vessel> SeedVesselAsync()
    {
        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();
        var vesselType = VesselType.Register("Container Ship", "CNTR", "Container carrying vessel");
        var vessel = Vessel.Register(Guid.NewGuid(), vesselType.Id, "North Star", "1000001", "Malta", "Active");

        await dbContext.Set<VesselType>().AddAsync(vesselType);
        await dbContext.Set<Vessel>().AddAsync(vessel);
        await dbContext.SaveChangesAsync();

        return vessel;
    }

    private async Task<CrewPosition> SeedRankAsync()
    {
        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();
        var position = CrewPosition.Create("Bosun", "Deck", "Senior");

        await dbContext.Set<CrewPosition>().AddAsync(position);
        await dbContext.SaveChangesAsync();

        return position;
    }

    private async Task<VesselManningPlan> SeedPlanAsync(Guid vesselId, Guid rankId, DateOnly start, DateOnly end)
    {
        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();
        var plan = VesselManningPlan.Create(vesselId, start, end, Guid.NewGuid(), DateTime.UtcNow);
        var line = VesselManningPlanLine.Create(plan.Id, rankId, 2);

        await dbContext.Set<VesselManningPlan>().AddAsync(plan);
        await dbContext.Set<VesselManningPlanLine>().AddAsync(line);
        await dbContext.SaveChangesAsync();

        return plan;
    }
}
