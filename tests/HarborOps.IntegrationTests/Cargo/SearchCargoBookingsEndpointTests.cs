using System.Net;
using System.Net.Http.Json;
using HarborOps.IntegrationTests.Authentication;
using HarborOps.Modules.Cargo.Application;
using HarborOps.Modules.Cargo.Contracts;
using HarborOps.Modules.Cargo.Domain;
using HarborOps.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace HarborOps.IntegrationTests.Cargo;

public sealed class SearchCargoBookingsEndpointTests : IntegrationTestBase
{
    private const string BookingsEndpoint = "/api/cargo/bookings";

    private static readonly Guid VoyageId = Guid.Parse("3c2b1a0d-9e8f-4a7b-8c6d-5e4f3a2b1c0d");

    private readonly HarborOpsApiFactory _application;

    public SearchCargoBookingsEndpointTests(HarborOpsApiFactory application)
        : base(application)
    {
        _application = application;
    }

    [Fact]
    public async Task Get_returns_registered_bookings()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Cargo.Manage);
        var bookingId = await RegisterBookingAsync(client, "BK-SEARCH-INT-0001", isHazardous: true);

        using var response = await client.GetAsync(BookingsEndpoint, CancellationToken.None);
        var result = await response.Content.ReadFromJsonAsync<SearchCargoBookingsResponse>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(result);

        var item = Assert.Single(result.Items);

        Assert.Equal(bookingId, item.CargoBookingId);
        Assert.Equal("BK-SEARCH-INT-0001", item.BookingNumber);
        Assert.Equal(VoyageId, item.VoyageId);
        Assert.Equal("Ammonium nitrate", item.Description);
        Assert.Equal("Dangerous Goods", item.CargoType);
        Assert.Equal(5000.000m, item.WeightKg);
        Assert.Equal(10, item.PackageCount);
        Assert.True(item.IsHazardous);
        Assert.Equal(nameof(CargoStatus.Registered), item.Status);
        Assert.Equal(1, result.TotalCount);
        Assert.Equal(SearchCargoBookingsValidator.DefaultPageNumber, result.PageNumber);
        Assert.Equal(SearchCargoBookingsValidator.DefaultPageSize, result.PageSize);
    }

    [Fact]
    public async Task Get_applies_search_and_hazardous_filters()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Cargo.Manage);
        await RegisterBookingAsync(client, "BK-SEARCH-INT-0002", isHazardous: true);
        await RegisterBookingAsync(client, "BK-SEARCH-INT-0003", isHazardous: false);

        using var response = await client.GetAsync(
            $"{BookingsEndpoint}?search=INT-0003&isHazardous=false&pageNumber=1&pageSize=10",
            CancellationToken.None);
        var result = await response.Content.ReadFromJsonAsync<SearchCargoBookingsResponse>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(result);
        Assert.Equal("BK-SEARCH-INT-0003", Assert.Single(result.Items).BookingNumber);
    }

    [Fact]
    public async Task Get_returns_validation_problem_for_an_unknown_status()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Cargo.Read);

        using var response = await client.GetAsync(
            $"{BookingsEndpoint}?status=Departed",
            CancellationToken.None);
        var problem = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotNull(problem);
        Assert.Contains(SearchCargoBookingsValidator.StatusField, problem.Errors);
    }

    [Fact]
    public async Task Get_returns_validation_problem_for_invalid_paging()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Cargo.Read);

        using var response = await client.GetAsync(
            $"{BookingsEndpoint}?pageNumber=0&pageSize={SearchCargoBookingsValidator.MaxPageSize + 1}",
            CancellationToken.None);
        var problem = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotNull(problem);
        Assert.Contains(SearchCargoBookingsValidator.PageNumberField, problem.Errors);
        Assert.Contains(SearchCargoBookingsValidator.PageSizeField, problem.Errors);
    }

    [Fact]
    public async Task Get_is_allowed_with_the_cargo_read_permission()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Cargo.Read);

        using var response = await client.GetAsync(BookingsEndpoint, CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static async Task<Guid> RegisterBookingAsync(
        HttpClient client,
        string bookingNumber,
        bool isHazardous)
    {
        var request = new RegisterCargoBookingRequest
        {
            BookingNumber = bookingNumber,
            VoyageId = VoyageId,
            Description = "Ammonium nitrate",
            CargoType = "Dangerous Goods",
            WeightKg = 5000.000m,
            PackageCount = 10,
            IsHazardous = isHazardous,
        };

        using var response = await client.PostAsJsonAsync(
            BookingsEndpoint,
            request,
            CancellationToken.None);
        var result = await response.Content.ReadFromJsonAsync<RegisterCargoBookingResponse>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(result);

        return result.Id;
    }
}
