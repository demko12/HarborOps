using System.Net;
using System.Net.Http.Json;
using HarborOps.Infrastructure.Persistence;
using HarborOps.IntegrationTests.Authentication;
using HarborOps.Modules.Voyages;
using HarborOps.Modules.Voyages.Application;
using HarborOps.Modules.Voyages.Contracts;
using HarborOps.Modules.Voyages.Domain;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HarborOps.IntegrationTests.Voyages;

public sealed class RecordVoyageDepartureEndpointTests : IntegrationTestBase
{
    private static readonly DateTime PlannedDepartureUtc =
        new(2026, 9, 1, 8, 0, 0, DateTimeKind.Utc);

    private static readonly DateTime PlannedArrivalUtc =
        new(2026, 9, 4, 17, 30, 0, DateTimeKind.Utc);

    private readonly HarborOpsApiFactory _application;

    public RecordVoyageDepartureEndpointTests(HarborOpsApiFactory application)
        : base(application)
    {
        _application = application;
    }

    [Fact]
    public async Task Post_records_departure_and_persists_transition()
    {
        using var client = await _application.CreateAuthenticatedClientAsync();
        var seeded = await SeedPlannedVoyageAsync("VOY-300");

        using var response = await client.PostAsJsonAsync(
            DepartureUri(seeded.VoyageId),
            new RecordVoyageDepartureRequest { ActualDepartureUtc = seeded.CreatedUtc },
            CancellationToken.None);
        var result = await response.Content.ReadFromJsonAsync<RecordVoyageDepartureResponse>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(result);
        Assert.Equal(seeded.VoyageId, result.Id);
        Assert.Equal(seeded.CreatedUtc, result.ActualDepartureUtc);
        Assert.Equal(Voyage.InProgressStatus, result.Status);

        var persisted = await LoadVoyageAsync(seeded.VoyageId);

        Assert.Equal(seeded.CreatedUtc, persisted.ActualDeparture);
        Assert.Equal(Voyage.InProgressStatus, persisted.Status);
    }

    [Fact]
    public async Task Post_returns_not_found_for_unknown_voyage()
    {
        using var client = await _application.CreateAuthenticatedClientAsync();

        using var response = await client.PostAsJsonAsync(
            DepartureUri(Guid.NewGuid()),
            new RecordVoyageDepartureRequest
            {
                ActualDepartureUtc = DateTime.UtcNow.AddHours(-1),
            },
            CancellationToken.None);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.NotNull(problem);
        Assert.Equal(StatusCodes.Status404NotFound, problem.Status);
    }

    [Fact]
    public async Task Post_returns_validation_problem_for_future_departure()
    {
        using var client = await _application.CreateAuthenticatedClientAsync();
        var seeded = await SeedPlannedVoyageAsync("VOY-301");

        using var response = await client.PostAsJsonAsync(
            DepartureUri(seeded.VoyageId),
            new RecordVoyageDepartureRequest
            {
                ActualDepartureUtc = DateTime.UtcNow.AddDays(1),
            },
            CancellationToken.None);
        var problem = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotNull(problem);
        Assert.Contains(RecordVoyageDepartureValidator.ActualDepartureUtcField, problem.Errors);

        var persisted = await LoadVoyageAsync(seeded.VoyageId);

        Assert.Null(persisted.ActualDeparture);
        Assert.Equal(Voyage.PlannedStatus, persisted.Status);
    }

    [Fact]
    public async Task Post_returns_conflict_for_second_departure()
    {
        using var client = await _application.CreateAuthenticatedClientAsync();
        var seeded = await SeedPlannedVoyageAsync("VOY-302");
        var request = new RecordVoyageDepartureRequest
        {
            ActualDepartureUtc = seeded.CreatedUtc,
        };

        using var firstResponse = await client.PostAsJsonAsync(
            DepartureUri(seeded.VoyageId),
            request,
            CancellationToken.None);
        using var secondResponse = await client.PostAsJsonAsync(
            DepartureUri(seeded.VoyageId),
            request,
            CancellationToken.None);
        var problem = await secondResponse.Content.ReadFromJsonAsync<ProblemDetails>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, secondResponse.StatusCode);
        Assert.NotNull(problem);
        Assert.Equal(StatusCodes.Status409Conflict, problem.Status);

        var persisted = await LoadVoyageAsync(seeded.VoyageId);

        Assert.Equal(seeded.CreatedUtc, persisted.ActualDeparture);
        Assert.Equal(Voyage.InProgressStatus, persisted.Status);
    }

    [Fact]
    public async Task Departure_introduces_no_fleet_lookup_or_reference()
    {
        using var client = await _application.CreateAuthenticatedClientAsync();
        var seeded = await SeedPlannedVoyageAsync("VOY-303");

        using var response = await client.PostAsJsonAsync(
            DepartureUri(seeded.VoyageId),
            new RecordVoyageDepartureRequest { ActualDepartureUtc = seeded.CreatedUtc },
            CancellationToken.None);

        var referencedAssemblies = VoyagesModule.Assembly
            .GetReferencedAssemblies()
            .Select(reference => reference.Name)
            .OfType<string>()
            .ToArray();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain(
            "HarborOps.Modules.Fleet",
            referencedAssemblies,
            StringComparer.Ordinal);
    }

    [Fact]
    public async Task Post_without_authentication_returns_401()
    {
        using var client = _application.CreateClient();

        using var response = await client.PostAsJsonAsync(
            DepartureUri(Guid.NewGuid()),
            new RecordVoyageDepartureRequest { ActualDepartureUtc = DateTime.UtcNow.AddHours(-1) },
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private static Uri DepartureUri(Guid voyageId)
    {
        return new Uri($"/api/voyages/{voyageId}/departure", UriKind.Relative);
    }

    private async Task<Voyage> LoadVoyageAsync(Guid voyageId)
    {
        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();

        return await dbContext
            .Set<Voyage>()
            .SingleAsync(candidate => candidate.Id == voyageId, CancellationToken.None);
    }

    private async Task<SeededVoyage> SeedPlannedVoyageAsync(string voyageNumber)
    {
        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();

        var voyage = Voyage.Schedule(
            Guid.NewGuid(),
            voyageNumber,
            "MERIJ",
            "ITGOA",
            PlannedDepartureUtc,
            PlannedArrivalUtc);

        await dbContext.Set<Voyage>().AddAsync(voyage);
        await dbContext.SaveChangesAsync();

        return new SeededVoyage(voyage.Id, voyage.CreatedUtc);
    }

    private sealed record SeededVoyage(Guid VoyageId, DateTime CreatedUtc);
}
