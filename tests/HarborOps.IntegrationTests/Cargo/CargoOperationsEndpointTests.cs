using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
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

public sealed class CargoOperationsEndpointTests : IntegrationTestBase
{
    private const string BookingsEndpoint = "/api/cargo/bookings";
    private const string OperationsEndpoint = "/api/cargo/operations";

    private static readonly Guid VoyageId = Guid.Parse("2b1a0c9d-8e7f-4a6b-9c5d-4e3f2a1b0c9d");
    private static readonly DateTime PlannedAtUtc = new(2026, 5, 4, 6, 0, 0, DateTimeKind.Utc);

    private readonly HarborOpsApiFactory _application;

    public CargoOperationsEndpointTests(HarborOpsApiFactory application)
        : base(application)
    {
        _application = application;
    }

    [Fact]
    public async Task Get_returns_the_operations_ordered_by_planned_time()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Cargo.Manage);
        var bookingId = await RegisterBookingAsync(client, "BK-OPS-INT-0001");
        await PlanOperationAsync(client, bookingId, "Unload", PlannedAtUtc.AddHours(6));
        await PlanOperationAsync(client, bookingId, "Load", PlannedAtUtc);

        using var response = await client.GetAsync(
            BookingOperationsEndpoint(bookingId),
            CancellationToken.None);
        var result = await response.Content.ReadFromJsonAsync<GetCargoOperationsResponse>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(result);
        Assert.Equal(
            [nameof(CargoOperationType.Load), nameof(CargoOperationType.Unload)],
            result.Items.Select(item => item.OperationType));
        Assert.All(
            result.Items,
            item => Assert.Equal(nameof(CargoOperationState.Planned), item.OperationState));
    }

    [Fact]
    public async Task Get_returns_not_found_for_an_unknown_booking()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Cargo.Read);

        using var response = await client.GetAsync(
            BookingOperationsEndpoint(Guid.NewGuid()),
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Get_is_forbidden_without_a_cargo_permission()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.IdentityUsers.Read);

        using var response = await client.GetAsync(
            BookingOperationsEndpoint(Guid.NewGuid()),
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Post_plans_an_operation_for_the_authenticated_user()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Cargo.Manage);
        var bookingId = await RegisterBookingAsync(client, "BK-OPS-INT-0002");
        var responsibleUserId = await GetAuthenticatedUserIdAsync(client);

        using var response = await client.PostAsJsonAsync(
            BookingOperationsEndpoint(bookingId),
            Request("Load"),
            CancellationToken.None);
        var result = await response.Content.ReadFromJsonAsync<PlanCargoOperationResponse>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(result);
        Assert.Equal(nameof(CargoOperationState.Planned), result.OperationState);

        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();
        var operation = await dbContext
            .Set<LoadUnloadOperation>()
            .SingleAsync(candidate => candidate.Id == result.Id, CancellationToken.None);
        Assert.Equal(responsibleUserId, operation.ResponsibleUserId);
        Assert.Equal(PlannedAtUtc, operation.PlannedAtUtc);
    }

    [Fact]
    public async Task Post_rejects_an_operation_type_outside_the_vocabulary()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Cargo.Manage);
        var bookingId = await RegisterBookingAsync(client, "BK-OPS-INT-0003");

        using var response = await client.PostAsJsonAsync(
            BookingOperationsEndpoint(bookingId),
            Request("Inspect"),
            CancellationToken.None);
        var problem = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotNull(problem);
        Assert.Contains("operationType", problem.Errors.Keys, StringComparer.Ordinal);
    }

    [Fact]
    public async Task Post_rejects_a_completed_booking()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Cargo.Manage);
        var bookingId = await RegisterBookingAsync(client, "BK-OPS-INT-0004");
        await CompleteBookingAsync(bookingId);

        using var response = await client.PostAsJsonAsync(
            BookingOperationsEndpoint(bookingId),
            Request("Load"),
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Post_is_forbidden_with_only_the_cargo_read_permission()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Cargo.Read);

        using var response = await client.PostAsJsonAsync(
            BookingOperationsEndpoint(Guid.NewGuid()),
            Request("Load"),
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Start_marks_the_operation_in_progress()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Cargo.Manage);
        var bookingId = await RegisterBookingAsync(client, "BK-OPS-INT-0005");
        await AddContainerAsync(client, bookingId, "MSKU1234567");
        await AdvanceBookingToAsync(bookingId, CargoStatus.Planned);
        var operationId = await PlanOperationAsync(client, bookingId, "Load", PlannedAtUtc);

        using var response = await client.PostAsync(
            $"{OperationsEndpoint}/{operationId}/start",
            content: null,
            CancellationToken.None);
        var result = await response.Content.ReadFromJsonAsync<CargoOperationTransitionResponse>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(result);
        Assert.Equal(nameof(CargoOperationState.InProgress), result.OperationState);
        Assert.NotNull(result.StartedAtUtc);
        Assert.Null(result.FinishedAtUtc);
    }

    [Fact]
    public async Task Start_rejects_a_repeated_start()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Cargo.Manage);
        var bookingId = await RegisterBookingAsync(client, "BK-OPS-INT-0006");
        await AddContainerAsync(client, bookingId, "MSKU2345678");
        await AdvanceBookingToAsync(bookingId, CargoStatus.Planned);
        var operationId = await PlanOperationAsync(client, bookingId, "Load", PlannedAtUtc);
        await StartOperationAsync(client, operationId);

        using var response = await client.PostAsync(
            $"{OperationsEndpoint}/{operationId}/start",
            content: null,
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Start_returns_not_found_for_an_unknown_operation()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Cargo.Manage);

        using var response = await client.PostAsync(
            $"{OperationsEndpoint}/{Guid.NewGuid()}/start",
            content: null,
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Start_is_forbidden_with_only_the_cargo_read_permission()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Cargo.Read);

        using var response = await client.PostAsync(
            $"{OperationsEndpoint}/{Guid.NewGuid()}/start",
            content: null,
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Complete_settles_the_operation_and_every_booking_container()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Cargo.Manage);
        var bookingId = await RegisterBookingAsync(client, "BK-OPS-INT-0007");
        await AddContainerAsync(client, bookingId, "MSKU3456789");
        await AddContainerAsync(client, bookingId, "TGHU7654321");
        await AdvanceBookingToAsync(bookingId, CargoStatus.Planned);
        var operationId = await PlanOperationAsync(client, bookingId, "Load", PlannedAtUtc);
        await StartOperationAsync(client, operationId);

        using var response = await client.PostAsync(
            $"{OperationsEndpoint}/{operationId}/complete",
            content: null,
            CancellationToken.None);
        var result = await response.Content.ReadFromJsonAsync<CargoOperationTransitionResponse>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(result);
        Assert.Equal(nameof(CargoOperationState.Completed), result.OperationState);
        Assert.NotNull(result.FinishedAtUtc);

        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();
        var containers = await dbContext
            .Set<Container>()
            .Where(container => container.CargoBookingId == bookingId)
            .ToListAsync(CancellationToken.None);
        var booking = await dbContext
            .Set<CargoBooking>()
            .SingleAsync(candidate => candidate.Id == bookingId, CancellationToken.None);
        Assert.All(containers, container => Assert.Equal(CargoStatus.Loaded, container.Status));
        Assert.Equal(CargoStatus.Planned, booking.Status);
    }

    [Fact]
    public async Task Complete_rejects_an_operation_that_never_started()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Cargo.Manage);
        var bookingId = await RegisterBookingAsync(client, "BK-OPS-INT-0008");
        var operationId = await PlanOperationAsync(client, bookingId, "Load", PlannedAtUtc);

        using var response = await client.PostAsync(
            $"{OperationsEndpoint}/{operationId}/complete",
            content: null,
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Complete_returns_not_found_for_an_unknown_operation()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Cargo.Manage);

        using var response = await client.PostAsync(
            $"{OperationsEndpoint}/{Guid.NewGuid()}/complete",
            content: null,
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Start_rejects_an_operation_when_the_manifest_is_empty()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Cargo.Manage);
        var bookingId = await RegisterBookingAsync(client, "BK-OPS-INT-0009");
        await AdvanceBookingToAsync(bookingId, CargoStatus.Planned);
        var operationId = await PlanOperationAsync(client, bookingId, "Load", PlannedAtUtc);

        using var response = await client.PostAsync(
            $"{OperationsEndpoint}/{operationId}/start",
            content: null,
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();
        var operation = await dbContext
            .Set<LoadUnloadOperation>()
            .SingleAsync(candidate => candidate.Id == operationId, CancellationToken.None);
        Assert.Null(operation.StartedAtUtc);
    }

    [Fact]
    public async Task Start_rejects_a_load_while_the_cargo_is_in_transit()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Cargo.Manage);
        var bookingId = await RegisterBookingAsync(client, "BK-OPS-INT-0011");
        await AddContainerAsync(client, bookingId, "MSKU4567890");
        var operationId = await PlanOperationAsync(client, bookingId, "Load", PlannedAtUtc);
        await AdvanceBookingToAsync(bookingId, CargoStatus.InTransit);

        using var response = await client.PostAsync(
            $"{OperationsEndpoint}/{operationId}/start",
            content: null,
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();
        var operation = await dbContext
            .Set<LoadUnloadOperation>()
            .SingleAsync(candidate => candidate.Id == operationId, CancellationToken.None);
        Assert.Null(operation.StartedAtUtc);
    }

    [Fact]
    public async Task Start_rejects_a_second_operation_while_one_is_in_progress()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Cargo.Manage);
        var bookingId = await RegisterBookingAsync(client, "BK-OPS-INT-0010");
        await AddContainerAsync(client, bookingId, "MSKU5678901");
        await AdvanceBookingToAsync(bookingId, CargoStatus.Planned);
        var runningOperationId = await PlanOperationAsync(client, bookingId, "Load", PlannedAtUtc);
        var plannedOperationId = await PlanOperationAsync(client, bookingId, "Load", PlannedAtUtc);
        await StartOperationAsync(client, runningOperationId);

        using var response = await client.PostAsync(
            $"{OperationsEndpoint}/{plannedOperationId}/start",
            content: null,
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();
        var operation = await dbContext
            .Set<LoadUnloadOperation>()
            .SingleAsync(candidate => candidate.Id == plannedOperationId, CancellationToken.None);
        Assert.Null(operation.StartedAtUtc);
    }

    private static string BookingOperationsEndpoint(Guid bookingId)
    {
        return $"{BookingsEndpoint}/{bookingId}/operations";
    }

    private static PlanCargoOperationRequest Request(string operationType, DateTime? plannedAtUtc = null)
    {
        return new PlanCargoOperationRequest
        {
            OperationType = operationType,
            PlannedAtUtc = plannedAtUtc ?? PlannedAtUtc,
        };
    }

    private static async Task<Guid> PlanOperationAsync(
        HttpClient client,
        Guid bookingId,
        string operationType,
        DateTime plannedAtUtc)
    {
        using var response = await client.PostAsJsonAsync(
            BookingOperationsEndpoint(bookingId),
            Request(operationType, plannedAtUtc),
            CancellationToken.None);
        var result = await response.Content.ReadFromJsonAsync<PlanCargoOperationResponse>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(result);

        return result.Id;
    }

    private static async Task StartOperationAsync(HttpClient client, Guid operationId)
    {
        using var response = await client.PostAsync(
            $"{OperationsEndpoint}/{operationId}/start",
            content: null,
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static async Task AddContainerAsync(
        HttpClient client,
        Guid bookingId,
        string containerNumber)
    {
        using var response = await client.PostAsJsonAsync(
            $"{BookingsEndpoint}/{bookingId}/containers",
            new AddCargoContainerRequest
            {
                ContainerNumber = containerNumber,
                ContainerType = "20GP",
            },
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    private Task CompleteBookingAsync(Guid bookingId)
    {
        return AdvanceBookingToAsync(bookingId, CargoStatus.Completed);
    }

    private async Task AdvanceBookingToAsync(Guid bookingId, CargoStatus status)
    {
        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();
        var booking = await dbContext
            .Set<CargoBooking>()
            .SingleAsync(candidate => candidate.Id == bookingId, CancellationToken.None);

        while (booking.Status != status)
        {
            booking.AdvanceStatusTo(booking.Status + 1);
        }

        await dbContext.SaveChangesAsync(CancellationToken.None);
    }

    private static async Task<Guid> GetAuthenticatedUserIdAsync(HttpClient client)
    {
        using var response = await client.GetAsync("/api/identity/me", CancellationToken.None);
        var account = await response.Content.ReadFromJsonAsync<JsonElement>(CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return account.GetProperty("id").GetGuid();
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
