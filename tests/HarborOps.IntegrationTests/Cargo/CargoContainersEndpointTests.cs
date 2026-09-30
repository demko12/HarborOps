using System.Net;
using System.Net.Http.Json;
using HarborOps.Infrastructure.Persistence;
using HarborOps.IntegrationTests.Authentication;
using HarborOps.Modules.Cargo.Contracts;
using HarborOps.Modules.Cargo.Domain;
using HarborOps.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HarborOps.IntegrationTests.Cargo;

public sealed class CargoContainersEndpointTests : IntegrationTestBase
{
    private const string BookingsEndpoint = "/api/cargo/bookings";

    private static readonly Guid VoyageId = Guid.Parse("2b1a0c9d-8e7f-4a6b-9c5d-4e3f2a1b0c9d");

    private readonly HarborOpsApiFactory _application;

    public CargoContainersEndpointTests(HarborOpsApiFactory application)
        : base(application)
    {
        _application = application;
    }

    [Fact]
    public async Task Get_returns_the_manifest_ordered_by_container_number()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Cargo.Manage);
        var bookingId = await RegisterBookingAsync(client, "BK-CONT-INT-0001");
        await AddContainerAsync(client, bookingId, "TGHU7654321");
        await AddContainerAsync(client, bookingId, "CSQU3054383");

        using var response = await client.GetAsync(
            ContainersEndpoint(bookingId),
            CancellationToken.None);
        var result = await response.Content.ReadFromJsonAsync<GetCargoContainersResponse>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(result);
        Assert.Equal(
            ["CSQU3054383", "TGHU7654321"],
            result.Items.Select(item => item.ContainerNumber));
        Assert.All(result.Items, item => Assert.Equal(bookingId, item.CargoBookingId));
        Assert.All(result.Items, item => Assert.Null(item.VesselId));
    }

    [Fact]
    public async Task Get_returns_an_empty_manifest_for_a_booking_without_containers()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Cargo.Read);
        using var manageClient = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Cargo.Manage);
        var bookingId = await RegisterBookingAsync(manageClient, "BK-CONT-INT-0002");

        using var response = await client.GetAsync(
            ContainersEndpoint(bookingId),
            CancellationToken.None);
        var result = await response.Content.ReadFromJsonAsync<GetCargoContainersResponse>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(result);
        Assert.Empty(result.Items);
    }

    [Fact]
    public async Task Get_returns_not_found_for_an_unknown_booking()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Cargo.Read);

        using var response = await client.GetAsync(
            ContainersEndpoint(Guid.NewGuid()),
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Get_is_forbidden_without_a_cargo_permission()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.IdentityUsers.Read);

        using var response = await client.GetAsync(
            ContainersEndpoint(Guid.NewGuid()),
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Get_lets_a_reader_find_a_container_beyond_the_first_page()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Cargo.Read);
        using var manageClient = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Cargo.Manage);
        var bookingId = await RegisterBookingAsync(manageClient, "BK-CONT-INT-0101");
        var otherBookingId = await RegisterBookingAsync(manageClient, "BK-CONT-INT-0102");
        await SeedContainersAsync(bookingId, "FLTU", 18, CargoStatus.Loaded);
        await SeedContainersAsync(otherBookingId, "FLTU", 1, CargoStatus.Loaded, firstOrdinal: 900);

        using var response = await client.GetAsync(
            $"{ContainersEndpoint(bookingId)}?search=%20fltu0000017%20&status=loaded&containerType=42r1&portOfLoadingCode=nlrtm&portOfDischargeCode=USNYC",
            CancellationToken.None);
        var result = await response.Content.ReadFromJsonAsync<GetCargoContainersResponse>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(result);
        Assert.Equal("FLTU0000017", Assert.Single(result.Items).ContainerNumber);
        Assert.Equal(1, result.MatchingCount);
        Assert.Equal(18, result.TotalCount);
        Assert.Equal(18, result.LoadedCount);
    }

    [Fact]
    public async Task Get_pages_the_matching_containers()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Cargo.Read);
        using var manageClient = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Cargo.Manage);
        var bookingId = await RegisterBookingAsync(manageClient, "BK-CONT-INT-0103");
        await SeedContainersAsync(bookingId, "PGEU", 16, CargoStatus.Registered);
        await SeedContainersAsync(bookingId, "PGFU", 3, CargoStatus.Loaded);

        using var response = await client.GetAsync(
            $"{ContainersEndpoint(bookingId)}?search=PGEU&pageNumber=2&pageSize=15",
            CancellationToken.None);
        var result = await response.Content.ReadFromJsonAsync<GetCargoContainersResponse>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(result);
        Assert.Equal("PGEU0000016", Assert.Single(result.Items).ContainerNumber);
        Assert.Equal(16, result.MatchingCount);
        Assert.Equal(19, result.TotalCount);
        Assert.Equal(3, result.LoadedCount);
        Assert.Equal(2, result.PageNumber);
    }

    [Fact]
    public async Task Get_rejects_invalid_filters_with_a_message_for_each_field()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Cargo.Read);
        using var manageClient = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Cargo.Manage);
        var bookingId = await RegisterBookingAsync(manageClient, "BK-CONT-INT-0104");

        using var response = await client.GetAsync(
            $"{ContainersEndpoint(bookingId)}?status=Lost&containerType=99ZZ&portOfLoadingCode=NL&portOfDischargeCode=US-NYC&search={new string('M', 21)}&pageSize=16",
            CancellationToken.None);
        var problem = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotNull(problem);
        Assert.Equal(
            ["containerType", "pageSize", "portOfDischargeCode", "portOfLoadingCode", "search", "status"],
            problem.Errors.Keys.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task Get_rejects_a_page_number_that_would_overflow_the_offset()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Cargo.Read);
        using var manageClient = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Cargo.Manage);
        var bookingId = await RegisterBookingAsync(manageClient, "BK-CONT-INT-0105");

        using var response = await client.GetAsync(
            $"{ContainersEndpoint(bookingId)}?pageNumber={int.MaxValue}",
            CancellationToken.None);
        var problem = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotNull(problem);
        Assert.Equal(["pageNumber"], problem.Errors.Keys);
    }

    [Fact]
    public async Task Get_with_filters_is_forbidden_without_a_cargo_permission()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.IdentityUsers.Read);

        using var response = await client.GetAsync(
            $"{ContainersEndpoint(Guid.NewGuid())}?search=MSKU&status=Loaded",
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Post_adds_and_persists_a_normalized_container()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Cargo.Manage);
        var bookingId = await RegisterBookingAsync(client, "BK-CONT-INT-0003");

        using var response = await client.PostAsJsonAsync(
            ContainersEndpoint(bookingId),
            ManifestRequest(" msku1234567 ", "42G1"),
            CancellationToken.None);
        var result = await response.Content.ReadFromJsonAsync<AddCargoContainerResponse>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(result);
        Assert.Equal("MSKU1234567", result.ContainerNumber);
        Assert.Equal(nameof(CargoStatus.Registered), result.Status);
        Assert.Equal(
            $"{ContainersEndpoint(bookingId)}/{result.Id}",
            response.Headers.Location?.OriginalString);

        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();
        var container = await dbContext
            .Set<Container>()
            .SingleAsync(candidate => candidate.Id == result.Id, CancellationToken.None);
        Assert.Equal("MSKU1234567", container.ContainerNumber);
        Assert.Equal(bookingId, container.CargoBookingId);
        Assert.Null(container.VesselId);
    }

    [Fact]
    public async Task Post_rejects_a_missing_container_number()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Cargo.Manage);
        var bookingId = await RegisterBookingAsync(client, "BK-CONT-INT-0004");

        using var response = await client.PostAsJsonAsync(
            ContainersEndpoint(bookingId),
            ManifestRequest("  ", "42G1"),
            CancellationToken.None);
        var problem = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotNull(problem);
        Assert.Contains("containerNumber", problem.Errors.Keys, StringComparer.Ordinal);
    }

    [Fact]
    public async Task Post_rejects_a_duplicate_container_number()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Cargo.Manage);
        var bookingId = await RegisterBookingAsync(client, "BK-CONT-INT-0005");
        await AddContainerAsync(client, bookingId, "MSKU1234567");

        using var response = await client.PostAsJsonAsync(
            ContainersEndpoint(bookingId),
            ManifestRequest("msku1234567", "42G1"),
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Post_rejects_a_completed_booking()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Cargo.Manage);
        var bookingId = await RegisterBookingAsync(client, "BK-CONT-INT-0006");
        await CompleteBookingAsync(bookingId);

        using var response = await client.PostAsJsonAsync(
            ContainersEndpoint(bookingId),
            ManifestRequest("MSKU1234567", "42G1"),
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Post_returns_not_found_for_an_unknown_booking()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Cargo.Manage);

        using var response = await client.PostAsJsonAsync(
            ContainersEndpoint(Guid.NewGuid()),
            ManifestRequest("MSKU1234567", "42G1"),
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Post_is_forbidden_with_only_the_cargo_read_permission()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Cargo.Read);

        using var response = await client.PostAsJsonAsync(
            ContainersEndpoint(Guid.NewGuid()),
            ManifestRequest("MSKU1234567", "42G1"),
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private static string ContainersEndpoint(Guid bookingId)
    {
        return $"{BookingsEndpoint}/{bookingId}/containers";
    }

    private static AddCargoContainerRequest ManifestRequest(string containerNumber, string containerType) => new()
    {
        ContainerNumber = containerNumber,
        ContainerType = containerType,
        PortOfLoadingCode = "NLRTM",
        PortOfDischargeCode = "USNYC",
        WeightKg = 12500m,
    };

    private static async Task AddContainerAsync(
        HttpClient client,
        Guid bookingId,
        string containerNumber)
    {
        using var response = await client.PostAsJsonAsync(
            ContainersEndpoint(bookingId),
            ManifestRequest(containerNumber, "22G1"),
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    private async Task SeedContainersAsync(
        Guid bookingId,
        string ownerCode,
        int count,
        CargoStatus status,
        int firstOrdinal = 1)
    {
        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();

        dbContext.Set<Container>().AddRange(Enumerable
            .Range(firstOrdinal, count)
            .Select(ordinal => Container.Register(
                $"{ownerCode}{ordinal:D7}",
                "42R1",
                status,
                cargoBookingId: bookingId,
                portOfLoadingCode: "NLRTM",
                portOfDischargeCode: "USNYC",
                weightKg: 12500m)));

        await dbContext.SaveChangesAsync(CancellationToken.None);
    }

    private async Task CompleteBookingAsync(Guid bookingId)
    {
        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();
        var booking = await dbContext
            .Set<CargoBooking>()
            .SingleAsync(candidate => candidate.Id == bookingId, CancellationToken.None);

        while (booking.Status != CargoStatus.Completed)
        {
            booking.AdvanceStatusTo(booking.Status + 1);
        }

        await dbContext.SaveChangesAsync(CancellationToken.None);
    }

    private static async Task<Guid> RegisterBookingAsync(HttpClient client, string bookingNumber)
    {
        var request = new RegisterCargoBookingRequest
        {
            BookingNumber = bookingNumber,
            VoyageId = VoyageId,
            Description = "Steel coils",
            CargoType = "General Cargo",
            WeightKg = 5000.000m,
            PackageCount = 10,
            IsHazardous = false,
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
