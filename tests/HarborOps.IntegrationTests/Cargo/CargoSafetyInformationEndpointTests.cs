using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HarborOps.IntegrationTests.Authentication;
using HarborOps.Modules.Cargo.Contracts;
using HarborOps.Modules.Cargo.Domain;
using HarborOps.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace HarborOps.IntegrationTests.Cargo;

public sealed class CargoSafetyInformationEndpointTests : IntegrationTestBase
{
    private const string BookingsEndpoint = "/api/cargo/bookings";

    private static readonly Guid VoyageId = Guid.Parse("2b1a0c9d-8e7f-4a6b-9c5d-4e3f2a1b0c9d");

    private readonly HarborOpsApiFactory _application;

    public CargoSafetyInformationEndpointTests(HarborOpsApiFactory application)
        : base(application)
    {
        _application = application;
    }

    [Fact]
    public async Task Get_returns_a_draft_declaration_with_an_empty_timeline()
    {
        using var client = await CreateManageClientAsync();
        var bookingId = await RegisterHazardousBookingAsync(client, "BK-SAFETY-INT-0001");
        await DeclareAsync(client, bookingId);

        using var response = await client.GetAsync(
            SafetyInformationEndpoint(bookingId),
            CancellationToken.None);
        var result = await response.Content.ReadFromJsonAsync<GetCargoSafetyInformationResponse>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(result);
        Assert.Equal("3", result.Declaration.ImdgClass);
        Assert.Equal("UN1203", result.Declaration.UnNumber);
        Assert.Equal("Keep below 25C", result.Declaration.StorageConditions);
        Assert.Equal(nameof(CargoSafetyReviewStatus.Draft), result.Declaration.ReviewStatus);
        Assert.Null(result.Declaration.SubmittedByUserId);
        Assert.Empty(result.Timeline);
    }

    [Fact]
    public async Task Get_is_available_to_a_reader()
    {
        using var manageClient = await CreateManageClientAsync();
        var bookingId = await RegisterHazardousBookingAsync(manageClient, "BK-SAFETY-INT-0002");
        await DeclareAsync(manageClient, bookingId);

        using var readClient = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Cargo.Read);
        using var response = await readClient.GetAsync(
            SafetyInformationEndpoint(bookingId),
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Get_is_forbidden_without_the_cargo_read_permission()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Fleet.Read);

        using var response = await client.GetAsync(
            SafetyInformationEndpoint(Guid.NewGuid()),
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Get_returns_not_found_for_an_unknown_booking()
    {
        using var client = await CreateManageClientAsync();

        using var response = await client.GetAsync(
            SafetyInformationEndpoint(Guid.NewGuid()),
            CancellationToken.None);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(CancellationToken.None);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.NotNull(problem);
        Assert.Equal("Cargo booking not found.", problem.Title);
    }

    [Fact]
    public async Task Get_returns_not_found_when_nothing_was_declared_yet()
    {
        using var client = await CreateManageClientAsync();
        var bookingId = await RegisterHazardousBookingAsync(client, "BK-SAFETY-INT-0003");

        using var response = await client.GetAsync(
            SafetyInformationEndpoint(bookingId),
            CancellationToken.None);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(CancellationToken.None);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.NotNull(problem);
        Assert.Equal("Cargo safety information not found.", problem.Title);
    }

    [Fact]
    public async Task Get_returns_conflict_for_a_booking_that_is_not_hazardous()
    {
        using var client = await CreateManageClientAsync();
        var bookingId = await RegisterBookingAsync(client, "BK-SAFETY-INT-0004", isHazardous: false);

        using var response = await client.GetAsync(
            SafetyInformationEndpoint(bookingId),
            CancellationToken.None);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(CancellationToken.None);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.NotNull(problem);
        Assert.Equal(StatusCodes.Status409Conflict, problem.Status);
        Assert.Equal("Cargo booking is not hazardous.", problem.Title);
    }

    [Fact]
    public async Task Put_declares_the_safety_information_and_normalizes_it()
    {
        using var client = await CreateManageClientAsync();
        var bookingId = await RegisterHazardousBookingAsync(client, "BK-SAFETY-INT-0005");

        using var response = await client.PutAsJsonAsync(
            SafetyInformationEndpoint(bookingId),
            new UpdateCargoSafetyInfoRequest
            {
                ImdgClass = " 1.4s ",
                UnNumber = " un 1203 ",
                StorageConditions = "  Keep below 25C  ",
                HandlingNotes = "  Away from heat  ",
            },
            CancellationToken.None);
        var result = await response.Content.ReadFromJsonAsync<CargoSafetyInfoDetails>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(result);
        Assert.Equal("1.4S", result.ImdgClass);
        Assert.Equal("UN1203", result.UnNumber);
        Assert.Equal("Keep below 25C", result.StorageConditions);
        Assert.Equal("Away from heat", result.HandlingNotes);
        Assert.Equal(nameof(CargoSafetyReviewStatus.Draft), result.ReviewStatus);
    }

    [Fact]
    public async Task Put_replaces_an_existing_draft()
    {
        using var client = await CreateManageClientAsync();
        var bookingId = await RegisterHazardousBookingAsync(client, "BK-SAFETY-INT-0006");
        var declared = await DeclareAsync(client, bookingId);

        using var response = await client.PutAsJsonAsync(
            SafetyInformationEndpoint(bookingId),
            new UpdateCargoSafetyInfoRequest
            {
                ImdgClass = "2.1",
                UnNumber = "UN1950",
                StorageConditions = "Ventilated hold",
                HandlingNotes = null,
            },
            CancellationToken.None);
        var result = await response.Content.ReadFromJsonAsync<CargoSafetyInfoDetails>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(result);
        Assert.Equal(declared.CargoSafetyInfoId, result.CargoSafetyInfoId);
        Assert.Equal("UN1950", result.UnNumber);
        Assert.Null(result.HandlingNotes);
    }

    [Fact]
    public async Task Put_returns_bad_request_for_an_invalid_declaration()
    {
        using var client = await CreateManageClientAsync();
        var bookingId = await RegisterHazardousBookingAsync(client, "BK-SAFETY-INT-0007");

        using var response = await client.PutAsJsonAsync(
            SafetyInformationEndpoint(bookingId),
            new UpdateCargoSafetyInfoRequest
            {
                ImdgClass = "not-a-class",
                UnNumber = "12A4",
                StorageConditions = "   ",
                HandlingNotes = null,
            },
            CancellationToken.None);
        var problem = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotNull(problem);
        Assert.Contains("imdgClass", problem.Errors.Keys);
        Assert.Contains("unNumber", problem.Errors.Keys);
        Assert.Contains("storageConditions", problem.Errors.Keys);
    }

    [Fact]
    public async Task Put_is_forbidden_for_a_reader()
    {
        using var manageClient = await CreateManageClientAsync();
        var bookingId = await RegisterHazardousBookingAsync(manageClient, "BK-SAFETY-INT-0008");

        using var readClient = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Cargo.Read);
        using var response = await readClient.PutAsJsonAsync(
            SafetyInformationEndpoint(bookingId),
            CreateValidDeclaration(),
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Put_returns_not_found_for_an_unknown_booking()
    {
        using var client = await CreateManageClientAsync();

        using var response = await client.PutAsJsonAsync(
            SafetyInformationEndpoint(Guid.NewGuid()),
            CreateValidDeclaration(),
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Put_returns_conflict_for_a_booking_that_is_not_hazardous()
    {
        using var client = await CreateManageClientAsync();
        var bookingId = await RegisterBookingAsync(client, "BK-SAFETY-INT-0009", isHazardous: false);

        using var response = await client.PutAsJsonAsync(
            SafetyInformationEndpoint(bookingId),
            CreateValidDeclaration(),
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Put_returns_conflict_once_the_declaration_is_approved()
    {
        using var client = await CreateManageClientAsync();
        var bookingId = await RegisterHazardousBookingAsync(client, "BK-SAFETY-INT-0010");
        await DeclareAsync(client, bookingId);
        await SubmitAsync(client, bookingId);
        await ReviewAsync(client, bookingId, CargoSafetyReviewStatus.Approved);

        using var response = await client.PutAsJsonAsync(
            SafetyInformationEndpoint(bookingId),
            CreateValidDeclaration(),
            CancellationToken.None);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(CancellationToken.None);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.NotNull(problem);
        Assert.Equal("Cargo safety information transition is not allowed.", problem.Title);
    }

    [Fact]
    public async Task Post_submit_moves_the_declaration_into_review_for_the_authenticated_user()
    {
        using var client = await CreateManageClientAsync();
        var bookingId = await RegisterHazardousBookingAsync(client, "BK-SAFETY-INT-0011");
        await DeclareAsync(client, bookingId);
        var userId = await GetAuthenticatedUserIdAsync(client);

        using var response = await client.PostAsync(
            $"{SafetyInformationEndpoint(bookingId)}/submit",
            content: null,
            CancellationToken.None);
        var result = await response.Content.ReadFromJsonAsync<CargoSafetyInfoDetails>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(result);
        Assert.Equal(nameof(CargoSafetyReviewStatus.Submitted), result.ReviewStatus);
        Assert.Equal(userId, result.SubmittedByUserId);
        Assert.NotNull(result.SubmittedAtUtc);
    }

    [Fact]
    public async Task Post_submit_returns_conflict_when_the_declaration_already_awaits_review()
    {
        using var client = await CreateManageClientAsync();
        var bookingId = await RegisterHazardousBookingAsync(client, "BK-SAFETY-INT-0012");
        await DeclareAsync(client, bookingId);
        await SubmitAsync(client, bookingId);

        using var response = await client.PostAsync(
            $"{SafetyInformationEndpoint(bookingId)}/submit",
            content: null,
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Post_submit_returns_not_found_when_nothing_was_declared_yet()
    {
        using var client = await CreateManageClientAsync();
        var bookingId = await RegisterHazardousBookingAsync(client, "BK-SAFETY-INT-0013");

        using var response = await client.PostAsync(
            $"{SafetyInformationEndpoint(bookingId)}/submit",
            content: null,
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Post_submit_is_forbidden_for_a_reader()
    {
        using var manageClient = await CreateManageClientAsync();
        var bookingId = await RegisterHazardousBookingAsync(manageClient, "BK-SAFETY-INT-0014");
        await DeclareAsync(manageClient, bookingId);

        using var readClient = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Cargo.Read);
        using var response = await readClient.PostAsync(
            $"{SafetyInformationEndpoint(bookingId)}/submit",
            content: null,
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Post_review_approves_the_declaration_for_the_authenticated_user()
    {
        using var client = await CreateManageClientAsync();
        var bookingId = await RegisterHazardousBookingAsync(client, "BK-SAFETY-INT-0015");
        await DeclareAsync(client, bookingId);
        await SubmitAsync(client, bookingId);
        var userId = await GetAuthenticatedUserIdAsync(client);

        using var response = await client.PostAsJsonAsync(
            $"{SafetyInformationEndpoint(bookingId)}/review",
            CreateReview(CargoSafetyReviewStatus.Approved, "Checked against the manifest."),
            CancellationToken.None);
        var result = await response.Content.ReadFromJsonAsync<CargoSafetyInfoDetails>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(result);
        Assert.Equal(nameof(CargoSafetyReviewStatus.Approved), result.ReviewStatus);
        Assert.Equal(userId, result.ReviewedByUserId);
        Assert.NotNull(result.ReviewedAtUtc);
        Assert.Equal("Checked against the manifest.", result.ReviewNotes);
    }

    [Fact]
    public async Task Post_review_rejects_the_declaration_with_the_reason()
    {
        using var client = await CreateManageClientAsync();
        var bookingId = await RegisterHazardousBookingAsync(client, "BK-SAFETY-INT-0016");
        await DeclareAsync(client, bookingId);
        await SubmitAsync(client, bookingId);

        using var response = await client.PostAsJsonAsync(
            $"{SafetyInformationEndpoint(bookingId)}/review",
            CreateReview(CargoSafetyReviewStatus.Rejected, "Storage conditions are unclear."),
            CancellationToken.None);
        var result = await response.Content.ReadFromJsonAsync<CargoSafetyInfoDetails>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(result);
        Assert.Equal(nameof(CargoSafetyReviewStatus.Rejected), result.ReviewStatus);
        Assert.Equal("Storage conditions are unclear.", result.ReviewNotes);
    }

    [Fact]
    public async Task Post_review_returns_bad_request_when_a_rejection_carries_no_notes()
    {
        using var client = await CreateManageClientAsync();
        var bookingId = await RegisterHazardousBookingAsync(client, "BK-SAFETY-INT-0017");
        await DeclareAsync(client, bookingId);
        await SubmitAsync(client, bookingId);

        using var response = await client.PostAsJsonAsync(
            $"{SafetyInformationEndpoint(bookingId)}/review",
            CreateReview(CargoSafetyReviewStatus.Rejected, notes: null),
            CancellationToken.None);
        var problem = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotNull(problem);
        Assert.Contains("notes", problem.Errors.Keys);
    }

    [Fact]
    public async Task Post_review_returns_bad_request_for_a_decision_it_does_not_accept()
    {
        using var client = await CreateManageClientAsync();
        var bookingId = await RegisterHazardousBookingAsync(client, "BK-SAFETY-INT-0018");
        await DeclareAsync(client, bookingId);
        await SubmitAsync(client, bookingId);

        using var response = await client.PostAsJsonAsync(
            $"{SafetyInformationEndpoint(bookingId)}/review",
            new ReviewCargoSafetyInfoRequest { Status = "Draft", Notes = null },
            CancellationToken.None);
        var problem = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotNull(problem);
        Assert.Contains("status", problem.Errors.Keys);
    }

    [Fact]
    public async Task Post_review_returns_conflict_while_the_declaration_is_still_a_draft()
    {
        using var client = await CreateManageClientAsync();
        var bookingId = await RegisterHazardousBookingAsync(client, "BK-SAFETY-INT-0019");
        await DeclareAsync(client, bookingId);

        using var response = await client.PostAsJsonAsync(
            $"{SafetyInformationEndpoint(bookingId)}/review",
            CreateReview(CargoSafetyReviewStatus.Approved),
            CancellationToken.None);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(CancellationToken.None);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.NotNull(problem);
        Assert.Equal("Cargo safety information transition is not allowed.", problem.Title);
    }

    [Fact]
    public async Task Post_review_is_forbidden_for_a_reader()
    {
        using var manageClient = await CreateManageClientAsync();
        var bookingId = await RegisterHazardousBookingAsync(manageClient, "BK-SAFETY-INT-0020");
        await DeclareAsync(manageClient, bookingId);
        await SubmitAsync(manageClient, bookingId);

        using var readClient = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Cargo.Read);
        using var response = await readClient.PostAsJsonAsync(
            $"{SafetyInformationEndpoint(bookingId)}/review",
            CreateReview(CargoSafetyReviewStatus.Approved),
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task The_timeline_records_every_transition_with_its_actor()
    {
        using var client = await CreateManageClientAsync();
        var bookingId = await RegisterHazardousBookingAsync(client, "BK-SAFETY-INT-0021");
        var userId = await GetAuthenticatedUserIdAsync(client);
        await DeclareAsync(client, bookingId);
        await SubmitAsync(client, bookingId);
        await ReviewAsync(client, bookingId, CargoSafetyReviewStatus.Rejected, "Storage conditions are unclear.");
        await DeclareAsync(client, bookingId);
        await SubmitAsync(client, bookingId);
        await ReviewAsync(client, bookingId, CargoSafetyReviewStatus.Approved);

        using var response = await client.GetAsync(
            SafetyInformationEndpoint(bookingId),
            CancellationToken.None);
        var result = await response.Content.ReadFromJsonAsync<GetCargoSafetyInformationResponse>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(result);
        Assert.Equal(nameof(CargoSafetyReviewStatus.Approved), result.Declaration.ReviewStatus);
        Assert.Equal(4, result.Timeline.Count);
        Assert.All(result.Timeline, entry => Assert.Equal(userId, entry.ActorUserId));
        Assert.Equal(nameof(CargoSafetyReviewStatus.Submitted), result.Timeline[0].ToStatus);
        Assert.Equal(nameof(CargoSafetyReviewStatus.Rejected), result.Timeline[1].ToStatus);
        Assert.Equal("Storage conditions are unclear.", result.Timeline[1].Notes);
        Assert.Equal(nameof(CargoSafetyReviewStatus.Submitted), result.Timeline[2].ToStatus);
        Assert.Equal(nameof(CargoSafetyReviewStatus.Approved), result.Timeline[3].ToStatus);
    }

    private static UpdateCargoSafetyInfoRequest CreateValidDeclaration()
    {
        return new UpdateCargoSafetyInfoRequest
        {
            ImdgClass = "3",
            UnNumber = "UN1203",
            StorageConditions = "Keep below 25C",
            HandlingNotes = null,
        };
    }

    private static ReviewCargoSafetyInfoRequest CreateReview(
        CargoSafetyReviewStatus decision,
        string? notes = null)
    {
        return new ReviewCargoSafetyInfoRequest
        {
            Status = decision.ToString(),
            Notes = notes,
        };
    }

    private static string SafetyInformationEndpoint(Guid bookingId)
    {
        return $"{BookingsEndpoint}/{bookingId}/safety-information";
    }

    private static async Task<CargoSafetyInfoDetails> DeclareAsync(HttpClient client, Guid bookingId)
    {
        using var response = await client.PutAsJsonAsync(
            SafetyInformationEndpoint(bookingId),
            CreateValidDeclaration(),
            CancellationToken.None);
        var result = await response.Content.ReadFromJsonAsync<CargoSafetyInfoDetails>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(result);

        return result;
    }

    private static async Task SubmitAsync(HttpClient client, Guid bookingId)
    {
        using var response = await client.PostAsync(
            $"{SafetyInformationEndpoint(bookingId)}/submit",
            content: null,
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static async Task ReviewAsync(
        HttpClient client,
        Guid bookingId,
        CargoSafetyReviewStatus decision,
        string? notes = null)
    {
        using var response = await client.PostAsJsonAsync(
            $"{SafetyInformationEndpoint(bookingId)}/review",
            CreateReview(decision, notes),
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static async Task<Guid> GetAuthenticatedUserIdAsync(HttpClient client)
    {
        using var response = await client.GetAsync("/api/identity/me", CancellationToken.None);
        var account = await response.Content.ReadFromJsonAsync<JsonElement>(CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return account.GetProperty("id").GetGuid();
    }

    private Task<HttpClient> CreateManageClientAsync()
    {
        return _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Cargo.Manage,
            Permissions.Cargo.Read);
    }

    private static Task<Guid> RegisterHazardousBookingAsync(HttpClient client, string bookingNumber)
    {
        return RegisterBookingAsync(client, bookingNumber, isHazardous: true);
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
            Description = "Solvent drums",
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
