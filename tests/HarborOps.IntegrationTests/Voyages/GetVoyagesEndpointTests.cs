using System.Net;
using System.Net.Http.Json;
using HarborOps.Infrastructure.Persistence;
using HarborOps.IntegrationTests.Authentication;
using HarborOps.Modules.Identity.Contracts;
using HarborOps.Modules.Voyages.Application;
using HarborOps.Modules.Voyages.Contracts;
using HarborOps.Modules.Voyages.Domain;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace HarborOps.IntegrationTests.Voyages;

public sealed class GetVoyagesEndpointTests : IntegrationTestBase
{
    private readonly HarborOpsApiFactory _application;

    public GetVoyagesEndpointTests(HarborOpsApiFactory application)
        : base(application)
    {
        _application = application;
    }

    [Fact]
    public async Task Get_returns_voyages_inside_the_requested_window()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Voyages.Read);
        var seeded = await SeedVoyageAsync(
            "VOY-600",
            new DateTime(2026, 9, 15, 8, 0, 0, DateTimeKind.Utc));
        await SeedVoyageAsync("VOY-601", new DateTime(2026, 10, 15, 8, 0, 0, DateTimeKind.Utc));

        using var response = await client.GetAsync(
            new Uri("/api/voyages?from=2026-09-01&to=2026-09-30", UriKind.Relative),
            CancellationToken.None);
        var page = await response.Content.ReadFromJsonAsync<GetVoyagesResponse>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(page);
        Assert.Equal(GetVoyagesValidator.DefaultPageNumber, page.PageNumber);
        Assert.Equal(GetVoyagesValidator.DefaultPageSize, page.PageSize);
        Assert.Equal(1, page.TotalCount);

        var item = Assert.Single(page.Items);

        Assert.Equal(seeded.VoyageId, item.VoyageId);
        Assert.Equal("VOY-600", item.VoyageNumber);
        Assert.Equal(seeded.VesselId, item.VesselId);
        Assert.Equal("MERIJ", item.DeparturePortCode);
        Assert.Equal("ITGOA", item.ArrivalPortCode);
        Assert.Equal(Voyage.PlannedStatus, item.Status);
        Assert.Null(item.ActualDeparture);
        Assert.Null(item.ActualArrival);
    }

    [Fact]
    public async Task Get_applies_status_filter_and_paging_from_the_query_string()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Voyages.Read);
        await SeedVoyageAsync("VOY-610", new DateTime(2026, 9, 5, 8, 0, 0, DateTimeKind.Utc));
        await SeedVoyageAsync("VOY-611", new DateTime(2026, 9, 6, 8, 0, 0, DateTimeKind.Utc));

        using var response = await client.GetAsync(
            new Uri(
                "/api/voyages?from=2026-09-01&to=2026-09-30&status=Planned&pageNumber=2&pageSize=1",
                UriKind.Relative),
            CancellationToken.None);
        var page = await response.Content.ReadFromJsonAsync<GetVoyagesResponse>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(page);
        Assert.Equal(2, page.PageNumber);
        Assert.Equal(1, page.PageSize);
        Assert.Equal(2, page.TotalCount);
        Assert.Equal("VOY-611", Assert.Single(page.Items).VoyageNumber);
    }

    [Fact]
    public async Task Get_applies_the_search_term_from_the_query_string()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Voyages.Read);
        await SeedVoyageAsync("VOY-640", new DateTime(2026, 9, 4, 8, 0, 0, DateTimeKind.Utc));
        await SeedVoyageAsync("ATL-641", new DateTime(2026, 9, 5, 8, 0, 0, DateTimeKind.Utc));

        using var response = await client.GetAsync(
            new Uri("/api/voyages?from=2026-09-01&to=2026-09-30&search=VOY-64", UriKind.Relative),
            CancellationToken.None);
        var page = await response.Content.ReadFromJsonAsync<GetVoyagesResponse>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(page);
        Assert.Equal(1, page.TotalCount);
        Assert.Equal("VOY-640", Assert.Single(page.Items).VoyageNumber);
    }

    [Fact]
    public async Task Get_returns_empty_page_when_the_window_holds_no_voyages()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Voyages.Read);
        await SeedVoyageAsync("VOY-620", new DateTime(2026, 9, 5, 8, 0, 0, DateTimeKind.Utc));

        using var response = await client.GetAsync(
            new Uri("/api/voyages?from=2026-11-01&to=2026-11-30", UriKind.Relative),
            CancellationToken.None);
        var page = await response.Content.ReadFromJsonAsync<GetVoyagesResponse>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(page);
        Assert.Equal(0, page.TotalCount);
        Assert.Empty(page.Items);
    }

    [Fact]
    public async Task Get_returns_validation_problem_for_reversed_date_range()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Voyages.Read);

        using var response = await client.GetAsync(
            new Uri("/api/voyages?from=2026-09-30&to=2026-09-01", UriKind.Relative),
            CancellationToken.None);
        var problem = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotNull(problem);
        Assert.Equal(StatusCodes.Status400BadRequest, problem.Status);
        Assert.Contains(GetVoyagesValidator.ToField, problem.Errors);
    }

    [Fact]
    public async Task Get_returns_validation_problem_for_oversized_page_size()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Voyages.Read);

        using var response = await client.GetAsync(
            new Uri("/api/voyages?from=2026-09-01&to=2026-09-30&pageSize=101", UriKind.Relative),
            CancellationToken.None);
        var problem = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotNull(problem);
        Assert.Contains(GetVoyagesValidator.PageSizeField, problem.Errors);
    }

    [Fact]
    public async Task Get_does_not_reference_the_fleet_module()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Voyages.Read);
        await SeedVoyageAsync("VOY-630", new DateTime(2026, 9, 5, 8, 0, 0, DateTimeKind.Utc));

        using var response = await client.GetAsync(
            new Uri("/api/voyages?from=2026-09-01&to=2026-09-30", UriKind.Relative),
            CancellationToken.None);

        var referencedAssemblies = HarborOps.Modules.Voyages.VoyagesModule.Assembly
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

    private async Task<SeededVoyage> SeedVoyageAsync(string voyageNumber, DateTime plannedDeparture)
    {
        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();

        var voyage = Voyage.Schedule(
            Guid.NewGuid(),
            voyageNumber,
            "MERIJ",
            "ITGOA",
            plannedDeparture,
            plannedDeparture.AddDays(3));

        await dbContext.Set<Voyage>().AddAsync(voyage);
        await dbContext.SaveChangesAsync();

        return new SeededVoyage(voyage.Id, voyage.VesselId);
    }

    private sealed record SeededVoyage(Guid VoyageId, Guid VesselId);
}
