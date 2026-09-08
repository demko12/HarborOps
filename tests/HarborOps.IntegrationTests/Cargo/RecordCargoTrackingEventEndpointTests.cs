using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HarborOps.Infrastructure.Persistence;
using HarborOps.IntegrationTests.Authentication;
using HarborOps.Modules.Cargo.Application;
using HarborOps.Modules.Cargo.Contracts;
using HarborOps.Modules.Cargo.Domain;
using HarborOps.Modules.Cargo.Presentation;
using HarborOps.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HarborOps.IntegrationTests.Cargo;

public sealed class RecordCargoTrackingEventEndpointTests : IntegrationTestBase
{
    private const string BookingsEndpoint = "/api/cargo/bookings";

    private static readonly Guid VoyageId = Guid.Parse("2b1a0c9d-8e7f-4a6b-9c5d-4e3f2a1b0c9d");

    private readonly HarborOpsApiFactory _application;

    public RecordCargoTrackingEventEndpointTests(HarborOpsApiFactory application)
        : base(application)
    {
        _application = application;
    }

    [Fact]
    public async Task Post_records_and_persists_a_tracking_event_for_the_authenticated_user()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Cargo.Manage);
        var bookingId = await RegisterBookingAsync(client, "BK-TRACK-INT-0001");
        var responsibleUserId = await GetAuthenticatedUserIdAsync(client);

        using var response = await client.PostAsJsonAsync(
            TrackingEventsEndpoint(bookingId),
            CreateValidRequest(nameof(CargoStatus.Planned)),
            CancellationToken.None);
        var result = await response.Content.ReadFromJsonAsync<RecordCargoTrackingEventResponse>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(result);
        Assert.NotEqual(Guid.Empty, result.Id);
        Assert.Equal(nameof(CargoStatus.Planned), result.Status);
        Assert.Equal(
            $"{TrackingEventsEndpoint(bookingId)}/{result.Id}",
            response.Headers.Location?.OriginalString);

        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();
        var trackingEvent = await dbContext
            .Set<CargoTrackingEvent>()
            .SingleAsync(candidate => candidate.Id == result.Id, CancellationToken.None);
        var booking = await dbContext
            .Set<CargoBooking>()
            .SingleAsync(candidate => candidate.Id == bookingId, CancellationToken.None);

        Assert.Equal(bookingId, trackingEvent.CargoBookingId);
        Assert.Equal(responsibleUserId, trackingEvent.ResponsibleUserId);
        Assert.Equal(CargoStatus.Planned, trackingEvent.Status);
        Assert.Equal("Terminal 4, Berth 12", trackingEvent.Location);
        Assert.Equal(CargoStatus.Planned, booking.Status);
    }

    [Fact]
    public async Task Post_ignores_a_responsible_user_id_supplied_in_the_request()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Cargo.Manage);
        var bookingId = await RegisterBookingAsync(client, "BK-TRACK-INT-0005");
        var responsibleUserId = await GetAuthenticatedUserIdAsync(client);
        var spoofedUserId = Guid.Parse("9e8d7c6b-5a4f-4e3d-8c2b-1a0f9e8d7c6b");

        using var response = await client.PostAsJsonAsync(
            TrackingEventsEndpoint(bookingId),
            new
            {
                status = nameof(CargoStatus.Planned),
                occurredUtc = DateTime.UtcNow,
                location = "Terminal 4, Berth 12",
                responsibleUserId = spoofedUserId,
            },
            CancellationToken.None);
        var result = await response.Content.ReadFromJsonAsync<RecordCargoTrackingEventResponse>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(result);

        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();
        var trackingEvent = await dbContext
            .Set<CargoTrackingEvent>()
            .SingleAsync(candidate => candidate.Id == result.Id, CancellationToken.None);

        Assert.Equal(responsibleUserId, trackingEvent.ResponsibleUserId);
        Assert.NotEqual(spoofedUserId, trackingEvent.ResponsibleUserId);
    }

    [Fact]
    public async Task Post_returns_unauthorized_for_an_anonymous_client()
    {
        using var authenticatedClient = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Cargo.Manage);
        var bookingId = await RegisterBookingAsync(authenticatedClient, "BK-TRACK-INT-0002");
        using var anonymousClient = _application.CreateClient();

        using var response = await anonymousClient.PostAsJsonAsync(
            TrackingEventsEndpoint(bookingId),
            CreateValidRequest(nameof(CargoStatus.Planned)),
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();

        Assert.False(
            await dbContext
                .Set<CargoTrackingEvent>()
                .AnyAsync(
                    candidate => candidate.CargoBookingId == bookingId,
                    CancellationToken.None));
    }

    [Fact]
    public async Task Post_returns_forbidden_without_the_cargo_manage_permission()
    {
        using var managingClient = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Cargo.Manage);
        var bookingId = await RegisterBookingAsync(managingClient, "BK-TRACK-INT-0007");
        using var readOnlyClient = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Cargo.Read);

        using var response = await readOnlyClient.PostAsJsonAsync(
            TrackingEventsEndpoint(bookingId),
            CreateValidRequest(nameof(CargoStatus.Planned)),
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();
        var booking = await dbContext
            .Set<CargoBooking>()
            .SingleAsync(candidate => candidate.Id == bookingId, CancellationToken.None);

        Assert.Equal(CargoStatus.Registered, booking.Status);
        Assert.False(
            await dbContext
                .Set<CargoTrackingEvent>()
                .AnyAsync(
                    candidate => candidate.CargoBookingId == bookingId,
                    CancellationToken.None));
    }

    [Fact]
    public async Task Post_returns_not_found_for_an_unknown_booking()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Cargo.Manage);
        var unknownBookingId = Guid.Parse("7c6b5a4d-3e2f-4a1b-8c9d-0e1f2a3b4c5d");

        using var response = await client.PostAsJsonAsync(
            TrackingEventsEndpoint(unknownBookingId),
            CreateValidRequest(nameof(CargoStatus.Planned)),
            CancellationToken.None);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.NotNull(problem);
        Assert.Equal(StatusCodes.Status404NotFound, problem.Status);
    }

    [Fact]
    public async Task Post_returns_validation_problem_for_invalid_input()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Cargo.Manage);
        var bookingId = await RegisterBookingAsync(client, "BK-TRACK-INT-0003");
        var request = CreateValidRequest("Departed") with
        {
            OccurredUtc = null,
            Location = "   ",
        };

        using var response = await client.PostAsJsonAsync(
            TrackingEventsEndpoint(bookingId),
            request,
            CancellationToken.None);
        var problem = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotNull(problem);
        Assert.Contains(RecordCargoTrackingEventValidator.StatusField, problem.Errors);
        Assert.Contains(RecordCargoTrackingEventValidator.OccurredUtcField, problem.Errors);
        Assert.Contains(RecordCargoTrackingEventValidator.LocationField, problem.Errors);
    }

    [Fact]
    public async Task Post_returns_conflict_for_a_skipped_transition_and_persists_nothing()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Cargo.Manage);
        var bookingId = await RegisterBookingAsync(client, "BK-TRACK-INT-0004");

        using var response = await client.PostAsJsonAsync(
            TrackingEventsEndpoint(bookingId),
            CreateValidRequest(nameof(CargoStatus.Loaded)),
            CancellationToken.None);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.NotNull(problem);
        Assert.Equal(StatusCodes.Status409Conflict, problem.Status);

        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();
        var booking = await dbContext
            .Set<CargoBooking>()
            .SingleAsync(candidate => candidate.Id == bookingId, CancellationToken.None);

        Assert.Equal(CargoStatus.Registered, booking.Status);
        Assert.False(
            await dbContext
                .Set<CargoTrackingEvent>()
                .AnyAsync(
                    candidate => candidate.CargoBookingId == bookingId,
                    CancellationToken.None));
    }

    [Fact]
    public async Task Post_returns_conflict_for_a_repeated_transition_and_persists_nothing()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Cargo.Manage);
        var bookingId = await RegisterBookingAsync(client, "BK-TRACK-INT-0006");

        using var response = await client.PostAsJsonAsync(
            TrackingEventsEndpoint(bookingId),
            CreateValidRequest(nameof(CargoStatus.Registered)),
            CancellationToken.None);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.NotNull(problem);
        Assert.Equal(StatusCodes.Status409Conflict, problem.Status);

        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();
        var booking = await dbContext
            .Set<CargoBooking>()
            .SingleAsync(candidate => candidate.Id == bookingId, CancellationToken.None);

        Assert.Equal(CargoStatus.Registered, booking.Status);
        Assert.False(
            await dbContext
                .Set<CargoTrackingEvent>()
                .AnyAsync(
                    candidate => candidate.CargoBookingId == bookingId,
                    CancellationToken.None));
    }

    [Fact]
    public async Task Post_returns_conflict_when_hazardous_cargo_is_loaded_before_it_is_declared()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Cargo.Manage);
        var bookingId = await RegisterBookingAsync(client, "BK-TRACK-INT-0008", isHazardous: true);

        await AdvanceAsync(client, bookingId, nameof(CargoStatus.Planned));

        using var response = await client.PostAsJsonAsync(
            TrackingEventsEndpoint(bookingId),
            CreateValidRequest(nameof(CargoStatus.Loaded)),
            CancellationToken.None);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.NotNull(problem);
        Assert.Equal(StatusCodes.Status409Conflict, problem.Status);
        Assert.Equal("Cargo safety information is required before loading.", problem.Title);

        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();
        var booking = await dbContext
            .Set<CargoBooking>()
            .SingleAsync(candidate => candidate.Id == bookingId, CancellationToken.None);

        Assert.Equal(CargoStatus.Planned, booking.Status);
    }

    [Fact]
    public async Task Post_loads_hazardous_cargo_after_its_safety_declaration_is_approved()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Cargo.Manage);
        var bookingId = await RegisterBookingAsync(client, "BK-TRACK-INT-0009", isHazardous: true);

        await AdvanceAsync(client, bookingId, nameof(CargoStatus.Planned));
        await DeclareSafetyInfoAsync(client, bookingId);
        await ApproveSafetyInfoAsync(client, bookingId);

        using var response = await client.PostAsJsonAsync(
            TrackingEventsEndpoint(bookingId),
            CreateValidRequest(nameof(CargoStatus.Loaded)),
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task Post_plans_hazardous_cargo_that_has_not_been_declared_yet()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Cargo.Manage);
        var bookingId = await RegisterBookingAsync(client, "BK-TRACK-INT-0010", isHazardous: true);

        using var response = await client.PostAsJsonAsync(
            TrackingEventsEndpoint(bookingId),
            CreateValidRequest(nameof(CargoStatus.Planned)),
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    private static async Task AdvanceAsync(HttpClient client, Guid bookingId, string status)
    {
        using var response = await client.PostAsJsonAsync(
            TrackingEventsEndpoint(bookingId),
            CreateValidRequest(status),
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    private static async Task DeclareSafetyInfoAsync(HttpClient client, Guid bookingId)
    {
        var request = new UpdateCargoSafetyInfoRequest
        {
            ImdgClass = "5.1",
            UnNumber = "UN1942",
            StorageConditions = "Keep below 25C",
            HandlingNotes = null,
        };

        using var response = await client.PutAsJsonAsync(
            $"{BookingsEndpoint}/{bookingId}/safety-information",
            request,
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static async Task ApproveSafetyInfoAsync(HttpClient client, Guid bookingId)
    {
        using var submitResponse = await client.PostAsync(
            $"{BookingsEndpoint}/{bookingId}/safety-information/submit",
            content: null,
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, submitResponse.StatusCode);

        var reviewRequest = new ReviewCargoSafetyInfoRequest
        {
            Status = nameof(CargoSafetyReviewStatus.Approved),
        };

        using var reviewResponse = await client.PostAsJsonAsync(
            $"{BookingsEndpoint}/{bookingId}/safety-information/review",
            reviewRequest,
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, reviewResponse.StatusCode);
    }

    [Fact]
    public async Task Module_service_and_controller_resolve_from_dependency_injection()
    {
        await using var scope = _application.Services.CreateAsyncScope();

        var service = scope.ServiceProvider.GetRequiredService<RecordCargoTrackingEventService>();
        var controller = ActivatorUtilities.CreateInstance<CargoTrackingEventsController>(
            scope.ServiceProvider);

        Assert.NotNull(service);
        Assert.NotNull(controller);
    }

    private static string TrackingEventsEndpoint(Guid bookingId)
    {
        return $"{BookingsEndpoint}/{bookingId}/tracking-events";
    }

    private static RecordCargoTrackingEventRequest CreateValidRequest(string status)
    {
        return new RecordCargoTrackingEventRequest
        {
            Status = status,
            OccurredUtc = DateTime.UtcNow,
            Location = " Terminal 4, Berth 12 ",
        };
    }

    private static async Task<Guid> GetAuthenticatedUserIdAsync(HttpClient client)
    {
        using var response = await client.GetAsync("/api/identity/me", CancellationToken.None);
        var account = await response.Content.ReadFromJsonAsync<JsonElement>(CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return account.GetProperty("id").GetGuid();
    }

    private static async Task<Guid> RegisterBookingAsync(
        HttpClient client,
        string bookingNumber,
        bool isHazardous = false)
    {
        var request = new RegisterCargoBookingRequest
        {
            BookingNumber = bookingNumber,
            VoyageId = VoyageId,
            Description = "Steel coils",
            CargoType = "General Cargo",
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
