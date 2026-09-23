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

public sealed class CrewManningCoverageEndpointTests : IntegrationTestBase
{
    private readonly HarborOpsApiFactory _application;

    public CrewManningCoverageEndpointTests(HarborOpsApiFactory application)
        : base(application)
    {
        _application = application;
    }

    [Fact]
    public async Task Get_without_authentication_returns_401()
    {
        using var client = _application.CreateClient();

        using var response = await client.GetAsync(
            $"/api/crew/manning-coverage?vesselId={Guid.NewGuid()}&from=2026-01-01&to=2026-01-07", CancellationToken.None);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_requires_only_the_read_permission()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Crew.Read);
        var vessel = Guid.NewGuid();
        var rank = await SeedRankAsync();
        await SeedPlanAsync(vessel, rank.Id, new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31));

        using var response = await client.GetAsync(
            $"/api/crew/manning-coverage?vesselId={vessel}&from=2026-01-01&to=2026-01-07", CancellationToken.None);
        var result = await response.Content.ReadFromJsonAsync<GetCrewManningCoverageResponse>(CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(result);
        Assert.Equal(7, result!.Days.Count);
        Assert.All(result.Days, day => Assert.Equal(2, day.RequiredHeadcount));
    }

    [Fact]
    public async Task Get_returns_400_when_the_window_exceeds_90_days()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Crew.Read);

        using var response = await client.GetAsync(
            $"/api/crew/manning-coverage?vesselId={Guid.NewGuid()}&from=2026-01-01&to=2026-04-15", CancellationToken.None);
        var problem = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>(CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotNull(problem);
        Assert.Contains(GetCrewManningCoverageValidator.ToField, problem.Errors);
    }

    [Fact]
    public async Task Get_does_not_mutate_any_data()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Crew.Read);
        var vessel = Guid.NewGuid();
        var rank = await SeedRankAsync();
        await SeedPlanAsync(vessel, rank.Id, new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31));

        using var response = await client.GetAsync(
            $"/api/crew/manning-coverage?vesselId={vessel}&from=2026-01-01&to=2026-01-07", CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var plans = await GetPlanCountAsync();
        Assert.Equal(1, plans);
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

    private async Task SeedPlanAsync(Guid vesselId, Guid rankId, DateOnly start, DateOnly end)
    {
        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();
        var plan = VesselManningPlan.Create(vesselId, start, end, Guid.NewGuid(), DateTime.UtcNow);
        var line = VesselManningPlanLine.Create(plan.Id, rankId, 2);

        await dbContext.Set<VesselManningPlan>().AddAsync(plan);
        await dbContext.Set<VesselManningPlanLine>().AddAsync(line);
        await dbContext.SaveChangesAsync();
    }

    private async Task<int> GetPlanCountAsync()
    {
        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();

        return dbContext.Set<VesselManningPlan>().Count();
    }
}
