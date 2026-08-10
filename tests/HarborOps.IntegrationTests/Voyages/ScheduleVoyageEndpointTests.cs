using System.Net;
using System.Net.Http.Json;
using HarborOps.Infrastructure.Persistence;
using HarborOps.IntegrationTests.Authentication;
using HarborOps.Modules.Voyages;
using HarborOps.Modules.Voyages.Application;
using HarborOps.Modules.Voyages.Contracts;
using HarborOps.Modules.Voyages.Domain;
using HarborOps.Modules.Voyages.Presentation;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HarborOps.IntegrationTests.Voyages;

public sealed class ScheduleVoyageEndpointTests : IntegrationTestBase
{
    private static readonly DateTime PlannedDepartureUtc =
        new(2026, 9, 1, 8, 0, 0, DateTimeKind.Utc);

    private static readonly DateTime PlannedArrivalUtc =
        new(2026, 9, 4, 17, 30, 0, DateTimeKind.Utc);

    private readonly HarborOpsApiFactory _application;

    public ScheduleVoyageEndpointTests(HarborOpsApiFactory application)
        : base(application)
    {
        _application = application;
    }

    [Fact]
    public async Task Post_schedules_and_persists_voyage()
    {
        using var client = await _application.CreateAuthenticatedClientAsync();
        var request = CreateValidRequest("VOY-100");

        using var response = await client.PostAsJsonAsync(
            "/api/voyages",
            request,
            CancellationToken.None);
        var result = await response.Content.ReadFromJsonAsync<ScheduleVoyageResponse>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(result);
        Assert.NotEqual(Guid.Empty, result.Id);
        Assert.Equal($"/api/voyages/{result.Id}", response.Headers.Location?.OriginalString);

        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();
        var voyage = await dbContext
            .Set<Voyage>()
            .SingleAsync(
                candidate => candidate.Id == result.Id,
                CancellationToken.None);
        var entityType = dbContext.Model.FindEntityType(typeof(Voyage));
        var voyageNumberIndex = entityType!
            .GetIndexes()
            .Single(index => index.Properties.Single().Name == nameof(Voyage.VoyageNumber));

        Assert.Equal(request.VoyageNumber, voyage.VoyageNumber);
        Assert.Equal(request.VesselId, voyage.VesselId);
        Assert.Equal(request.DeparturePortCode, voyage.DeparturePortCode);
        Assert.Equal(request.ArrivalPortCode, voyage.ArrivalPortCode);
        Assert.Equal(Voyage.PlannedStatus, voyage.Status);
        Assert.Equal(PlannedDepartureUtc, voyage.PlannedDeparture);
        Assert.Equal(PlannedArrivalUtc, voyage.PlannedArrival);
        Assert.Equal("Voyages", entityType.GetTableName());
        Assert.Equal(VoyagesModule.Schema, entityType.GetSchema());
        Assert.True(voyageNumberIndex.IsUnique);
    }

    [Fact]
    public async Task Post_returns_validation_problem_for_invalid_input()
    {
        using var client = await _application.CreateAuthenticatedClientAsync();
        var request = new ScheduleVoyageRequest
        {
            VoyageNumber = string.Empty,
            VesselId = Guid.Empty,
            DeparturePortCode = "MER",
            ArrivalPortCode = "ITGOA",
            PlannedDepartureUtc = PlannedArrivalUtc,
            PlannedArrivalUtc = PlannedDepartureUtc,
        };

        using var response = await client.PostAsJsonAsync(
            "/api/voyages",
            request,
            CancellationToken.None);
        var problem = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotNull(problem);
        Assert.Contains(ScheduleVoyageValidator.VoyageNumberField, problem.Errors);
        Assert.Contains(ScheduleVoyageValidator.VesselIdField, problem.Errors);
        Assert.Contains(ScheduleVoyageValidator.DeparturePortCodeField, problem.Errors);
        Assert.Contains(ScheduleVoyageValidator.PlannedArrivalUtcField, problem.Errors);
    }

    [Fact]
    public async Task Post_returns_conflict_for_duplicate_voyage_number()
    {
        using var client = await _application.CreateAuthenticatedClientAsync();
        var request = CreateValidRequest("VOY-200");

        using var firstResponse = await client.PostAsJsonAsync(
            "/api/voyages",
            request,
            CancellationToken.None);
        using var duplicateResponse = await client.PostAsJsonAsync(
            "/api/voyages",
            request,
            CancellationToken.None);
        var problem = await duplicateResponse.Content.ReadFromJsonAsync<HttpValidationProblemDetails>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Created, firstResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, duplicateResponse.StatusCode);
        Assert.NotNull(problem);
        Assert.Contains(ScheduleVoyageValidator.VoyageNumberField, problem.Errors);
    }

    [Fact]
    public async Task Post_without_authentication_returns_401()
    {
        using var client = _application.CreateClient();
        var request = CreateValidRequest("VOY-300");

        using var response = await client.PostAsJsonAsync(
            "/api/voyages",
            request,
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public void Dependency_injection_resolves_module_service_and_controller()
    {
        using var scope = _application.Services.CreateScope();

        var service = scope.ServiceProvider.GetRequiredService<ScheduleVoyageService>();
        var controller = ActivatorUtilities.CreateInstance<VoyagesController>(
            scope.ServiceProvider);

        Assert.NotNull(service);
        Assert.NotNull(controller);
    }

    private static ScheduleVoyageRequest CreateValidRequest(string voyageNumber)
    {
        return new ScheduleVoyageRequest
        {
            VoyageNumber = voyageNumber,
            VesselId = Guid.NewGuid(),
            DeparturePortCode = "MERIJ",
            ArrivalPortCode = "ITGOA",
            PlannedDepartureUtc = PlannedDepartureUtc,
            PlannedArrivalUtc = PlannedArrivalUtc,
        };
    }
}
