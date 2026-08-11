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

public sealed class RecordVoyageArrivalEndpointTests : IntegrationTestBase
{
    private static readonly DateTime PlannedDepartureUtc =
        new(2026, 1, 1, 8, 0, 0, DateTimeKind.Utc);

    private static readonly DateTime PlannedArrivalUtc =
        new(2026, 1, 5, 17, 30, 0, DateTimeKind.Utc);

    private static readonly DateTime ActualDepartureUtc =
        new(2026, 1, 2, 6, 0, 0, DateTimeKind.Utc);

    private static readonly DateTime ActualArrivalUtc =
        new(2026, 1, 4, 18, 0, 0, DateTimeKind.Utc);

    private readonly HarborOpsApiFactory _application;

    public RecordVoyageArrivalEndpointTests(HarborOpsApiFactory application)
        : base(application)
    {
        _application = application;
    }

    [Fact]
    public async Task Post_records_arrival_and_persists_transition()
    {
        using var client = await _application.CreateAuthenticatedClientAsync();
        var voyageId = await SeedDepartedVoyageAsync("VOY-400");

        using var response = await client.PostAsJsonAsync(
            ArrivalUri(voyageId),
            new RecordVoyageArrivalRequest { ActualArrivalUtc = ActualArrivalUtc },
            CancellationToken.None);
        var result = await response.Content.ReadFromJsonAsync<RecordVoyageArrivalResponse>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(result);
        Assert.Equal(voyageId, result.Id);
        Assert.Equal(ActualArrivalUtc, result.ActualArrivalUtc);
        Assert.Equal(Voyage.CompletedStatus, result.Status);

        var persisted = await LoadVoyageAsync(voyageId);

        Assert.Equal(ActualArrivalUtc, persisted.ActualArrival);
        Assert.Equal(Voyage.CompletedStatus, persisted.Status);
    }

    [Fact]
    public async Task Post_returns_not_found_for_unknown_voyage()
    {
        using var client = await _application.CreateAuthenticatedClientAsync();

        using var response = await client.PostAsJsonAsync(
            ArrivalUri(Guid.NewGuid()),
            new RecordVoyageArrivalRequest { ActualArrivalUtc = ActualArrivalUtc },
            CancellationToken.None);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.NotNull(problem);
        Assert.Equal(StatusCodes.Status404NotFound, problem.Status);
    }

    [Fact]
    public async Task Post_returns_validation_problem_for_future_arrival()
    {
        using var client = await _application.CreateAuthenticatedClientAsync();
        var voyageId = await SeedDepartedVoyageAsync("VOY-401");

        using var response = await client.PostAsJsonAsync(
            ArrivalUri(voyageId),
            new RecordVoyageArrivalRequest { ActualArrivalUtc = DateTime.UtcNow.AddDays(1) },
            CancellationToken.None);
        var problem = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotNull(problem);
        Assert.Contains(RecordVoyageArrivalValidator.ActualArrivalUtcField, problem.Errors);

        var persisted = await LoadVoyageAsync(voyageId);

        Assert.Null(persisted.ActualArrival);
        Assert.Equal(Voyage.InProgressStatus, persisted.Status);
    }

    [Fact]
    public async Task Post_returns_conflict_for_voyage_that_has_not_departed()
    {
        using var client = await _application.CreateAuthenticatedClientAsync();
        var voyageId = await SeedPlannedVoyageAsync("VOY-402");

        using var response = await client.PostAsJsonAsync(
            ArrivalUri(voyageId),
            new RecordVoyageArrivalRequest { ActualArrivalUtc = ActualArrivalUtc },
            CancellationToken.None);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.NotNull(problem);
        Assert.Equal(StatusCodes.Status409Conflict, problem.Status);

        var persisted = await LoadVoyageAsync(voyageId);

        Assert.Null(persisted.ActualArrival);
        Assert.Equal(Voyage.PlannedStatus, persisted.Status);
    }

    [Fact]
    public async Task Post_returns_conflict_for_second_arrival()
    {
        using var client = await _application.CreateAuthenticatedClientAsync();
        var voyageId = await SeedDepartedVoyageAsync("VOY-403");
        var request = new RecordVoyageArrivalRequest { ActualArrivalUtc = ActualArrivalUtc };

        using var firstResponse = await client.PostAsJsonAsync(
            ArrivalUri(voyageId),
            request,
            CancellationToken.None);
        using var secondResponse = await client.PostAsJsonAsync(
            ArrivalUri(voyageId),
            request,
            CancellationToken.None);
        var problem = await secondResponse.Content.ReadFromJsonAsync<ProblemDetails>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, secondResponse.StatusCode);
        Assert.NotNull(problem);
        Assert.Equal(StatusCodes.Status409Conflict, problem.Status);

        var persisted = await LoadVoyageAsync(voyageId);

        Assert.Equal(ActualArrivalUtc, persisted.ActualArrival);
        Assert.Equal(Voyage.CompletedStatus, persisted.Status);
    }

    [Fact]
    public async Task Arrival_introduces_no_fleet_lookup_or_reference()
    {
        using var client = await _application.CreateAuthenticatedClientAsync();
        var voyageId = await SeedDepartedVoyageAsync("VOY-404");

        using var response = await client.PostAsJsonAsync(
            ArrivalUri(voyageId),
            new RecordVoyageArrivalRequest { ActualArrivalUtc = ActualArrivalUtc },
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

    private static Uri ArrivalUri(Guid voyageId)
    {
        return new Uri($"/api/voyages/{voyageId}/arrival", UriKind.Relative);
    }

    private async Task<Voyage> LoadVoyageAsync(Guid voyageId)
    {
        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();

        return await dbContext
            .Set<Voyage>()
            .SingleAsync(candidate => candidate.Id == voyageId, CancellationToken.None);
    }

    private async Task<Guid> SeedDepartedVoyageAsync(string voyageNumber)
    {
        return await SeedVoyageAsync(voyageNumber, voyage => voyage.Depart(ActualDepartureUtc));
    }

    private async Task<Guid> SeedPlannedVoyageAsync(string voyageNumber)
    {
        return await SeedVoyageAsync(voyageNumber, _ => { });
    }

    private async Task<Guid> SeedVoyageAsync(string voyageNumber, Action<Voyage> arrange)
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

        arrange(voyage);

        await dbContext.Set<Voyage>().AddAsync(voyage);
        await dbContext.SaveChangesAsync();

        return voyage.Id;
    }
}
