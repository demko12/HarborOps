using System.Net;
using System.Net.Http.Json;
using HarborOps.IntegrationTests.Authentication;
using HarborOps.Modules.Cargo.Contracts;
using HarborOps.Modules.Cargo.Domain;
using HarborOps.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Mvc;

namespace HarborOps.IntegrationTests.Cargo;

public sealed class GetCargoBookingDetailsEndpointTests : IntegrationTestBase
{
    private const string BookingsEndpoint = "/api/cargo/bookings";

    private static readonly Guid VoyageId = Guid.Parse("6b5a4938-2716-4f0e-9d8c-7b6a59483726");

    private readonly HarborOpsApiFactory _application;

    public GetCargoBookingDetailsEndpointTests(HarborOpsApiFactory application)
        : base(application)
    {
        _application = application;
    }

    [Fact]
    public async Task Get_returns_the_registered_booking()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Cargo.Manage);
        var bookingId = await RegisterBookingAsync(client, "BK-DETAIL-INT-0001", isHazardous: false);

        using var response = await client.GetAsync(
            $"{BookingsEndpoint}/{bookingId}",
            CancellationToken.None);
        var details = await response.Content.ReadFromJsonAsync<GetCargoBookingDetailsResponse>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(details);
        Assert.Equal(bookingId, details.CargoBookingId);
        Assert.Equal("BK-DETAIL-INT-0001", details.BookingNumber);
        Assert.Equal(VoyageId, details.VoyageId);
        Assert.False(details.IsHazardous);
        Assert.Equal(nameof(CargoStatus.Registered), details.Status);
        Assert.Equal(nameof(CargoStatus.Planned), details.NextStatus);
        Assert.Null(details.SafetyInfo);
        Assert.Empty(details.TrackingEvents);
    }

    [Fact]
    public async Task Get_returns_declared_safety_information_and_recorded_tracking_events()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Cargo.Manage);
        var bookingId = await RegisterBookingAsync(client, "BK-DETAIL-INT-0002", isHazardous: true);
        await DeclareSafetyInformationAsync(client, bookingId);
        await RecordTrackingEventAsync(client, bookingId, nameof(CargoStatus.Planned));

        using var response = await client.GetAsync(
            $"{BookingsEndpoint}/{bookingId}",
            CancellationToken.None);
        var details = await response.Content.ReadFromJsonAsync<GetCargoBookingDetailsResponse>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(details);
        Assert.Equal(nameof(CargoStatus.Planned), details.Status);
        Assert.Equal(nameof(CargoStatus.Loaded), details.NextStatus);

        Assert.NotNull(details.SafetyInfo);
        Assert.Equal("1.4S", details.SafetyInfo.ImdgClass);
        Assert.Equal("UN1234", details.SafetyInfo.UnNumber);

        var trackingEvent = Assert.Single(details.TrackingEvents);

        Assert.Equal(nameof(CargoStatus.Planned), trackingEvent.Status);
        Assert.Equal("Terminal 4, Berth 12", trackingEvent.Location);
        Assert.NotEqual(Guid.Empty, trackingEvent.ResponsibleUserId);
    }

    [Fact]
    public async Task Get_returns_not_found_for_an_unknown_booking()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Cargo.Read);

        using var response = await client.GetAsync(
            $"{BookingsEndpoint}/{Guid.NewGuid()}",
            CancellationToken.None);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.NotNull(problem);
        Assert.Equal("Cargo booking not found.", problem.Title);
    }

    [Fact]
    public async Task Get_returns_unauthorized_for_an_anonymous_client()
    {
        using var client = _application.CreateClient();

        using var response = await client.GetAsync(
            $"{BookingsEndpoint}/{Guid.NewGuid()}",
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_is_forbidden_without_a_cargo_permission()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Fleet.Read);

        using var response = await client.GetAsync(
            $"{BookingsEndpoint}/{Guid.NewGuid()}",
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Get_is_allowed_with_the_cargo_read_permission()
    {
        using var managingClient = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Cargo.Manage);
        var bookingId = await RegisterBookingAsync(managingClient, "BK-DETAIL-INT-0003", isHazardous: false);

        using var readOnlyClient = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Cargo.Read);
        using var response = await readOnlyClient.GetAsync(
            $"{BookingsEndpoint}/{bookingId}",
            CancellationToken.None);

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

    [Fact]
    public async Task Get_returns_the_control_state_of_every_container()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Cargo.Manage);
        var bookingId = await RegisterBookingAsync(client, "BK-DETAIL-INT-CTRL", isHazardous: false);
        var releasedContainerId = await AddContainerAsync(client, bookingId, "MSKU2222222");
        await AddContainerAsync(client, bookingId, "MSKU1111111");
        await ReleaseContainerAsync(client, releasedContainerId);

        using var response = await client.GetAsync(
            $"{BookingsEndpoint}/{bookingId}",
            CancellationToken.None);
        var details = await response.Content.ReadFromJsonAsync<GetCargoBookingDetailsResponse>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(details);
        Assert.Equal(
            ["MSKU1111111", "MSKU2222222"],
            details.ContainerControls.Select(control => control.ContainerNumber));
        Assert.Equal(
            [nameof(ContainerControlState.Pending), nameof(ContainerControlState.Released)],
            details.ContainerControls.Select(control => control.State));
        Assert.Equal("ML458821", details.ContainerControls[1].SealNumber);
        Assert.NotNull(details.ContainerControls[0].ReleaseBlockedReason);
    }

    private static async Task<Guid> AddContainerAsync(
        HttpClient client,
        Guid bookingId,
        string containerNumber)
    {
        using var response = await client.PostAsJsonAsync(
            $"{BookingsEndpoint}/{bookingId}/containers",
            new AddCargoContainerRequest
            {
                ContainerNumber = containerNumber,
                ContainerType = "40HC",
            },
            CancellationToken.None);
        var result = await response.Content.ReadFromJsonAsync<AddCargoContainerResponse>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(result);

        return result.Id;
    }

    private static async Task ReleaseContainerAsync(HttpClient client, Guid containerId)
    {
        var containersEndpoint = $"/api/cargo/containers/{containerId}";

        using var sealResponse = await client.PostAsJsonAsync(
            $"{containersEndpoint}/seal",
            new AssignContainerSealRequest { SealNumber = "ML458821" },
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, sealResponse.StatusCode);

        using var inspectionResponse = await client.PostAsJsonAsync(
            $"{containersEndpoint}/inspections",
            new RecordContainerInspectionRequest
            {
                Result = nameof(ContainerInspectionResult.Passed),
                ConditionNotes = "Doors aligned",
            },
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Created, inspectionResponse.StatusCode);

        using var releaseResponse = await client.PostAsJsonAsync(
            $"{containersEndpoint}/release",
            new ReleaseContainerRequest(),
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, releaseResponse.StatusCode);
    }

    private static async Task DeclareSafetyInformationAsync(HttpClient client, Guid bookingId)
    {
        var request = new UpdateCargoSafetyInfoRequest
        {
            ImdgClass = "1.4s",
            UnNumber = "un1234",
            StorageConditions = "Keep below 25 C",
            HandlingNotes = "Segregate from foodstuffs",
        };

        using var response = await client.PutAsJsonAsync(
            $"{BookingsEndpoint}/{bookingId}/safety-information",
            request,
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static async Task RecordTrackingEventAsync(
        HttpClient client,
        Guid bookingId,
        string status)
    {
        var request = new RecordCargoTrackingEventRequest
        {
            Status = status,
            OccurredUtc = DateTime.UtcNow,
            Location = " Terminal 4, Berth 12 ",
        };

        using var response = await client.PostAsJsonAsync(
            $"{BookingsEndpoint}/{bookingId}/tracking-events",
            request,
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }
}
