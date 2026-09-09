using System.Net;
using System.Net.Http.Json;
using HarborOps.IntegrationTests.Authentication;
using HarborOps.Modules.Cargo.Contracts;
using HarborOps.Modules.Cargo.Domain;
using HarborOps.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace HarborOps.IntegrationTests.Cargo;

public sealed class ContainerControlEndpointTests : IntegrationTestBase
{
    private const string BookingsEndpoint = "/api/cargo/bookings";

    private static readonly Guid VoyageId = Guid.Parse("3c2b1a0d-9e8f-4b7a-8c6d-5e4f3a2b1c0d");

    private readonly HarborOpsApiFactory _application;

    public ContainerControlEndpointTests(HarborOpsApiFactory application)
        : base(application)
    {
        _application = application;
    }

    [Fact]
    public async Task Get_returns_a_pending_control_for_a_new_container()
    {
        using var client = await CreateManageClientAsync();
        var containerId = await AddContainerAsync(client, "BK-CTRL-INT-0001");

        using var response = await client.GetAsync(
            ControlEndpoint(containerId),
            CancellationToken.None);
        var result = await response.Content.ReadFromJsonAsync<GetContainerControlResponse>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(result);
        Assert.Equal("MSKU1234567", result.Control.ContainerNumber);
        Assert.Null(result.Control.SealNumber);
        Assert.Equal(nameof(ContainerSealStatus.None), result.Control.SealStatus);
        Assert.Equal(nameof(ContainerControlState.Pending), result.Control.State);
        Assert.False(result.Control.CanRelease);
        Assert.Empty(result.History);
    }

    [Fact]
    public async Task Get_is_available_to_a_reader()
    {
        using var manageClient = await CreateManageClientAsync();
        var containerId = await AddContainerAsync(manageClient, "BK-CTRL-INT-0002");

        using var readClient = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Cargo.Read);
        using var response = await readClient.GetAsync(
            ControlEndpoint(containerId),
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Get_is_forbidden_without_the_cargo_read_permission()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Fleet.Read);

        using var response = await client.GetAsync(
            ControlEndpoint(Guid.NewGuid()),
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Get_returns_not_found_for_an_unknown_container()
    {
        using var client = await CreateManageClientAsync();

        using var response = await client.GetAsync(
            ControlEndpoint(Guid.NewGuid()),
            CancellationToken.None);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.NotNull(problem);
        Assert.Equal("Container not found.", problem.Title);
    }

    [Fact]
    public async Task Seal_normalizes_the_seal_number()
    {
        using var client = await CreateManageClientAsync();
        var containerId = await AddContainerAsync(client, "BK-CTRL-INT-0003");

        using var response = await client.PostAsJsonAsync(
            SealEndpoint(containerId),
            new AssignContainerSealRequest { SealNumber = "  ml 45 8821 " },
            CancellationToken.None);
        var result = await response.Content.ReadFromJsonAsync<ContainerControlDetails>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(result);
        Assert.Equal("ML458821", result.SealNumber);
        Assert.Equal(nameof(ContainerSealStatus.Intact), result.SealStatus);
    }

    [Fact]
    public async Task Seal_is_forbidden_for_a_reader()
    {
        using var manageClient = await CreateManageClientAsync();
        var containerId = await AddContainerAsync(manageClient, "BK-CTRL-INT-0004");

        using var readClient = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Cargo.Read);
        using var response = await readClient.PostAsJsonAsync(
            SealEndpoint(containerId),
            new AssignContainerSealRequest { SealNumber = "ML458821" },
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Seal_returns_bad_request_for_an_invalid_seal_number()
    {
        using var client = await CreateManageClientAsync();
        var containerId = await AddContainerAsync(client, "BK-CTRL-INT-0005");

        using var response = await client.PostAsJsonAsync(
            SealEndpoint(containerId),
            new AssignContainerSealRequest { SealNumber = "  " },
            CancellationToken.None);
        var problem = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotNull(problem);
        Assert.True(problem.Errors.ContainsKey("sealNumber"));
    }

    [Fact]
    public async Task Seal_returns_conflict_for_a_seal_that_is_active_elsewhere()
    {
        using var client = await CreateManageClientAsync();
        var firstContainerId = await AddContainerAsync(client, "BK-CTRL-INT-0006");
        var secondContainerId = await AddContainerAsync(
            client,
            "BK-CTRL-INT-0007",
            containerNumber: "MSKU7654321");

        await AssignSealAsync(client, firstContainerId, "ML458821");

        using var response = await client.PostAsJsonAsync(
            SealEndpoint(secondContainerId),
            new AssignContainerSealRequest { SealNumber = "ml458821" },
            CancellationToken.None);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.NotNull(problem);
        Assert.Equal(StatusCodes.Status409Conflict, problem.Status);
        Assert.Equal("Seal number is already in use.", problem.Title);
    }

    [Fact]
    public async Task Seal_returns_conflict_for_a_seal_the_container_already_carries()
    {
        using var client = await CreateManageClientAsync();
        var containerId = await AddContainerAsync(client, "BK-CTRL-INT-0016");

        await AssignSealAsync(client, containerId, "ML458821");

        using var response = await client.PostAsJsonAsync(
            SealEndpoint(containerId),
            new AssignContainerSealRequest { SealNumber = "ml 458 821" },
            CancellationToken.None);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.NotNull(problem);
        Assert.Equal(StatusCodes.Status409Conflict, problem.Status);
        Assert.Equal("Container control transition is not allowed.", problem.Title);
    }

    [Fact]
    public async Task Seal_replacement_revokes_an_existing_release()
    {
        using var client = await CreateManageClientAsync();
        var containerId = await AddContainerAsync(client, "BK-CTRL-INT-0017");

        await AssignSealAsync(client, containerId, "ML458821");
        await RecordInspectionAsync(client, containerId, ContainerInspectionResult.Passed);
        await ReleaseAsync(client, containerId);

        using var response = await client.PostAsJsonAsync(
            SealEndpoint(containerId),
            new AssignContainerSealRequest { SealNumber = "ML999000" },
            CancellationToken.None);
        var details = await response.Content.ReadFromJsonAsync<ContainerControlDetails>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(details);
        Assert.Equal("ML999000", details.SealNumber);
        Assert.Equal(nameof(ContainerControlState.Pending), details.State);
    }

    [Fact]
    public async Task Seal_returns_not_found_for_an_unknown_container()
    {
        using var client = await CreateManageClientAsync();

        using var response = await client.PostAsJsonAsync(
            SealEndpoint(Guid.NewGuid()),
            new AssignContainerSealRequest { SealNumber = "ML458821" },
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Inspections_returns_created_with_the_recorded_outcome()
    {
        using var client = await CreateManageClientAsync();
        var containerId = await AddContainerAsync(client, "BK-CTRL-INT-0008");
        await AssignSealAsync(client, containerId, "ML458821");

        using var response = await client.PostAsJsonAsync(
            InspectionsEndpoint(containerId),
            new RecordContainerInspectionRequest
            {
                Result = nameof(ContainerInspectionResult.Passed),
                ConditionNotes = "  Doors aligned  ",
            },
            CancellationToken.None);
        var result = await response.Content.ReadFromJsonAsync<ContainerControlDetails>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(result);
        Assert.Equal(nameof(ContainerInspectionResult.Passed), result.LatestInspectionResult);
        Assert.Equal("Doors aligned", result.LatestInspectionNotes);
        Assert.NotNull(result.LatestInspectedAtUtc);
        Assert.True(result.CanRelease);
    }

    [Fact]
    public async Task Inspections_records_a_temperature_for_a_reefer_container()
    {
        using var client = await CreateManageClientAsync();
        var containerId = await AddContainerAsync(
            client,
            "BK-CTRL-INT-0009",
            containerType: "40RH");
        await AssignSealAsync(client, containerId, "ML458821");

        using var response = await client.PostAsJsonAsync(
            InspectionsEndpoint(containerId),
            new RecordContainerInspectionRequest
            {
                Result = nameof(ContainerInspectionResult.Passed),
                ConditionNotes = "Cooling steady",
                TemperatureCelsius = -18.5m,
            },
            CancellationToken.None);
        var result = await response.Content.ReadFromJsonAsync<ContainerControlDetails>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(result);
        Assert.True(result.IsReefer);
        Assert.Equal(-18.5m, result.LatestInspectionTemperatureCelsius);
    }

    [Fact]
    public async Task Inspections_returns_bad_request_for_a_temperature_on_a_dry_container()
    {
        using var client = await CreateManageClientAsync();
        var containerId = await AddContainerAsync(client, "BK-CTRL-INT-0010");
        await AssignSealAsync(client, containerId, "ML458821");

        using var response = await client.PostAsJsonAsync(
            InspectionsEndpoint(containerId),
            new RecordContainerInspectionRequest
            {
                Result = nameof(ContainerInspectionResult.Passed),
                TemperatureCelsius = -18.5m,
            },
            CancellationToken.None);
        var problem = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotNull(problem);
        Assert.True(problem.Errors.ContainsKey("temperatureCelsius"));
    }

    [Fact]
    public async Task Release_succeeds_for_a_sealed_and_passed_container()
    {
        using var client = await CreateManageClientAsync();
        var containerId = await AddContainerAsync(client, "BK-CTRL-INT-0011");
        await AssignSealAsync(client, containerId, "ML458821");
        await RecordInspectionAsync(client, containerId, ContainerInspectionResult.Passed);

        using var response = await client.PostAsJsonAsync(
            ReleaseEndpoint(containerId),
            new ReleaseContainerRequest { Notes = "Cleared for loading" },
            CancellationToken.None);
        var result = await response.Content.ReadFromJsonAsync<ContainerControlDetails>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(result);
        Assert.Equal(nameof(ContainerControlState.Released), result.State);
        Assert.Equal("Cleared for loading", result.StateReason);
        Assert.NotNull(result.StateChangedByUserId);
    }

    [Fact]
    public async Task Release_returns_conflict_without_an_inspection()
    {
        using var client = await CreateManageClientAsync();
        var containerId = await AddContainerAsync(client, "BK-CTRL-INT-0012");
        await AssignSealAsync(client, containerId, "ML458821");

        using var response = await client.PostAsJsonAsync(
            ReleaseEndpoint(containerId),
            new ReleaseContainerRequest(),
            CancellationToken.None);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.NotNull(problem);
        Assert.Equal("Container control transition is not allowed.", problem.Title);
        Assert.Contains("has not been inspected yet", problem.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Release_returns_conflict_after_a_failed_inspection()
    {
        using var client = await CreateManageClientAsync();
        var containerId = await AddContainerAsync(client, "BK-CTRL-INT-0013");
        await AssignSealAsync(client, containerId, "ML458821");
        await RecordInspectionAsync(client, containerId, ContainerInspectionResult.Failed);

        using var response = await client.PostAsJsonAsync(
            ReleaseEndpoint(containerId),
            new ReleaseContainerRequest(),
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Release_returns_conflict_while_the_container_is_held()
    {
        using var client = await CreateManageClientAsync();
        var containerId = await AddContainerAsync(client, "BK-CTRL-INT-0014");
        await AssignSealAsync(client, containerId, "ML458821");
        await RecordInspectionAsync(client, containerId, ContainerInspectionResult.Passed);
        await HoldAsync(client, containerId, "Damaged corner post");

        using var response = await client.PostAsJsonAsync(
            ReleaseEndpoint(containerId),
            new ReleaseContainerRequest(),
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Release_is_forbidden_for_a_reader()
    {
        using var manageClient = await CreateManageClientAsync();
        var containerId = await AddContainerAsync(manageClient, "BK-CTRL-INT-0015");

        using var readClient = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Cargo.Read);
        using var response = await readClient.PostAsJsonAsync(
            ReleaseEndpoint(containerId),
            new ReleaseContainerRequest(),
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Hold_removes_release_eligibility_and_records_the_reason()
    {
        using var client = await CreateManageClientAsync();
        var containerId = await AddContainerAsync(client, "BK-CTRL-INT-0016");
        await AssignSealAsync(client, containerId, "ML458821");
        await RecordInspectionAsync(client, containerId, ContainerInspectionResult.Passed);
        await ReleaseAsync(client, containerId);

        using var response = await client.PostAsJsonAsync(
            HoldEndpoint(containerId),
            new HoldContainerRequest { Reason = "Damaged corner post" },
            CancellationToken.None);
        var result = await response.Content.ReadFromJsonAsync<ContainerControlDetails>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(result);
        Assert.Equal(nameof(ContainerControlState.Held), result.State);
        Assert.Equal("Damaged corner post", result.StateReason);
        Assert.False(result.CanRelease);
    }

    [Fact]
    public async Task Hold_returns_bad_request_without_a_reason()
    {
        using var client = await CreateManageClientAsync();
        var containerId = await AddContainerAsync(client, "BK-CTRL-INT-0017");

        using var response = await client.PostAsJsonAsync(
            HoldEndpoint(containerId),
            new HoldContainerRequest { Reason = "   " },
            CancellationToken.None);
        var problem = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotNull(problem);
        Assert.True(problem.Errors.ContainsKey("reason"));
    }

    [Fact]
    public async Task Hold_returns_conflict_when_the_container_is_already_held()
    {
        using var client = await CreateManageClientAsync();
        var containerId = await AddContainerAsync(client, "BK-CTRL-INT-0018");
        await HoldAsync(client, containerId, "Damaged corner post");

        using var response = await client.PostAsJsonAsync(
            HoldEndpoint(containerId),
            new HoldContainerRequest { Reason = "Still damaged" },
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task The_history_records_every_action_in_order()
    {
        using var client = await CreateManageClientAsync();
        var containerId = await AddContainerAsync(client, "BK-CTRL-INT-0019");
        await AssignSealAsync(client, containerId, "ML458821");
        await RecordInspectionAsync(client, containerId, ContainerInspectionResult.Passed);
        await ReleaseAsync(client, containerId);
        await HoldAsync(client, containerId, "Damaged corner post");

        using var response = await client.GetAsync(
            ControlEndpoint(containerId),
            CancellationToken.None);
        var result = await response.Content.ReadFromJsonAsync<GetContainerControlResponse>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(result);
        Assert.Equal(
            [
                nameof(ContainerControlAction.SealAssigned),
                nameof(ContainerControlAction.InspectionRecorded),
                nameof(ContainerControlAction.Released),
                nameof(ContainerControlAction.Held),
            ],
            result.History.Select(entry => entry.Action));
        Assert.All(result.History, entry => Assert.NotEqual(Guid.Empty, entry.ActorUserId));
    }

    private static string ControlEndpoint(Guid containerId)
    {
        return $"/api/cargo/containers/{containerId}/control";
    }

    private static string SealEndpoint(Guid containerId)
    {
        return $"/api/cargo/containers/{containerId}/seal";
    }

    private static string InspectionsEndpoint(Guid containerId)
    {
        return $"/api/cargo/containers/{containerId}/inspections";
    }

    private static string ReleaseEndpoint(Guid containerId)
    {
        return $"/api/cargo/containers/{containerId}/release";
    }

    private static string HoldEndpoint(Guid containerId)
    {
        return $"/api/cargo/containers/{containerId}/hold";
    }

    [Fact]
    public async Task A_passing_inspection_lifts_a_hold_and_names_it_in_the_history()
    {
        using var client = await CreateManageClientAsync();
        var containerId = await AddContainerAsync(client, "BK-CTRL-INT-0019");

        await AssignSealAsync(client, containerId, "ML458821");
        await HoldAsync(client, containerId, "Damaged corner post");
        await RecordInspectionAsync(client, containerId, ContainerInspectionResult.Passed);

        using var response = await client.GetAsync(ControlEndpoint(containerId), CancellationToken.None);
        var view = await response.Content.ReadFromJsonAsync<GetContainerControlResponse>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(view);
        Assert.Equal(nameof(ContainerControlState.Pending), view.Control.State);

        var lifted = Assert.Single(
            view.History,
            entry => entry.Action == nameof(ContainerControlAction.HoldLifted));

        Assert.Equal(nameof(ContainerControlState.Held), lifted.FromState);
        Assert.Equal(nameof(ContainerControlState.Pending), lifted.ToState);
        Assert.Equal("Damaged corner post", lifted.Notes);
    }

    [Fact]
    public async Task Breaking_a_seal_marks_it_broken_and_records_the_action()
    {
        using var client = await CreateManageClientAsync();
        var containerId = await AddContainerAsync(client, "BK-CTRL-INT-0020");
        await AssignSealAsync(client, containerId, "ML458821");

        using var response = await client.PostAsJsonAsync(
            SealBreakEndpoint(containerId),
            new BreakContainerSealRequest { Notes = "Opened for customs" },
            CancellationToken.None);
        var details = await response.Content.ReadFromJsonAsync<ContainerControlDetails>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(details);
        Assert.Equal(nameof(ContainerSealStatus.Broken), details.SealStatus);
        Assert.False(details.CanRelease);
    }

    [Fact]
    public async Task Breaking_a_seal_withdraws_an_existing_release()
    {
        using var client = await CreateManageClientAsync();
        var containerId = await AddContainerAsync(client, "BK-CTRL-INT-0021");
        await AssignSealAsync(client, containerId, "ML458821");
        await RecordInspectionAsync(client, containerId, ContainerInspectionResult.Passed);
        await ReleaseAsync(client, containerId);

        using var response = await client.PostAsJsonAsync(
            SealBreakEndpoint(containerId),
            new BreakContainerSealRequest(),
            CancellationToken.None);
        var details = await response.Content.ReadFromJsonAsync<ContainerControlDetails>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(details);
        Assert.Equal(nameof(ContainerControlState.Pending), details.State);
    }

    [Fact]
    public async Task Breaking_a_seal_is_forbidden_for_a_reader()
    {
        using var manageClient = await CreateManageClientAsync();
        var containerId = await AddContainerAsync(manageClient, "BK-CTRL-INT-0022");
        await AssignSealAsync(manageClient, containerId, "ML458821");

        using var readerClient = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Cargo.Read);

        using var response = await readerClient.PostAsJsonAsync(
            SealBreakEndpoint(containerId),
            new BreakContainerSealRequest(),
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Breaking_a_seal_returns_conflict_without_an_intact_seal()
    {
        using var client = await CreateManageClientAsync();
        var containerId = await AddContainerAsync(client, "BK-CTRL-INT-0023");

        using var response = await client.PostAsJsonAsync(
            SealBreakEndpoint(containerId),
            new BreakContainerSealRequest(),
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Breaking_a_seal_returns_not_found_for_an_unknown_container()
    {
        using var client = await CreateManageClientAsync();

        using var response = await client.PostAsJsonAsync(
            SealBreakEndpoint(Guid.NewGuid()),
            new BreakContainerSealRequest(),
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private static string SealBreakEndpoint(Guid containerId)
    {
        return $"/api/cargo/containers/{containerId}/seal/break";
    }

    private Task<HttpClient> CreateManageClientAsync()
    {
        return _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Cargo.Manage,
            Permissions.Cargo.Read);
    }

    private static async Task AssignSealAsync(
        HttpClient client,
        Guid containerId,
        string sealNumber)
    {
        using var response = await client.PostAsJsonAsync(
            SealEndpoint(containerId),
            new AssignContainerSealRequest { SealNumber = sealNumber },
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static async Task RecordInspectionAsync(
        HttpClient client,
        Guid containerId,
        ContainerInspectionResult result)
    {
        using var response = await client.PostAsJsonAsync(
            InspectionsEndpoint(containerId),
            new RecordContainerInspectionRequest
            {
                Result = result.ToString(),
                ConditionNotes = "Doors aligned",
            },
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    private static async Task ReleaseAsync(HttpClient client, Guid containerId)
    {
        using var response = await client.PostAsJsonAsync(
            ReleaseEndpoint(containerId),
            new ReleaseContainerRequest(),
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static async Task HoldAsync(HttpClient client, Guid containerId, string reason)
    {
        using var response = await client.PostAsJsonAsync(
            HoldEndpoint(containerId),
            new HoldContainerRequest { Reason = reason },
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static async Task<Guid> AddContainerAsync(
        HttpClient client,
        string bookingNumber,
        string containerNumber = "MSKU1234567",
        string containerType = "40HC")
    {
        var bookingId = await RegisterBookingAsync(client, bookingNumber);

        using var response = await client.PostAsJsonAsync(
            $"{BookingsEndpoint}/{bookingId}/containers",
            new AddCargoContainerRequest
            {
                ContainerNumber = containerNumber,
                ContainerType = containerType,
            },
            CancellationToken.None);
        var result = await response.Content.ReadFromJsonAsync<AddCargoContainerResponse>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(result);

        return result.Id;
    }

    private static async Task<Guid> RegisterBookingAsync(HttpClient client, string bookingNumber)
    {
        using var response = await client.PostAsJsonAsync(
            BookingsEndpoint,
            new RegisterCargoBookingRequest
            {
                BookingNumber = bookingNumber,
                VoyageId = VoyageId,
                Description = "Machine parts",
                CargoType = "General Cargo",
                WeightKg = 5000.000m,
                PackageCount = 10,
                IsHazardous = false,
            },
            CancellationToken.None);
        var result = await response.Content.ReadFromJsonAsync<RegisterCargoBookingResponse>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(result);

        return result.Id;
    }
}
