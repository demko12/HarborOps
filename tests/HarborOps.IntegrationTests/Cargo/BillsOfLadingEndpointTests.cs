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

public sealed class BillsOfLadingEndpointTests : IntegrationTestBase
{
    private const string BookingsEndpoint = "/api/cargo/bookings";
    private const string BillsEndpoint = "/api/cargo/bills-of-lading";

    private static readonly Guid VoyageId = Guid.Parse("2b1a0d9e-8f7a-4b6c-9d5e-4f3a2b1c0d9e");

    private readonly HarborOpsApiFactory _application;

    public BillsOfLadingEndpointTests(HarborOpsApiFactory application)
        : base(application)
    {
        _application = application;
    }

    [Fact]
    public async Task Create_returns_created_with_the_normalized_bill_number()
    {
        using var client = await CreateManageClientAsync();
        var seeded = await SeedAsync(client, "BK-BOL-INT-0001");

        using var response = await client.PostAsJsonAsync(
            BillsForBooking(seeded.BookingId),
            Request("  bl-2026-0001  ", [seeded.ContainerId]),
            CancellationToken.None);
        var details = await response.Content.ReadFromJsonAsync<BillOfLadingDetails>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(details);
        Assert.Equal("BL-2026-0001", details.BillNumber);
        Assert.Equal(nameof(BillOfLadingStatus.Draft), details.Status);
        Assert.Single(details.Containers);
    }

    [Fact]
    public async Task Create_returns_bad_request_for_a_missing_shipper()
    {
        using var client = await CreateManageClientAsync();
        var seeded = await SeedAsync(client, "BK-BOL-INT-0002");

        using var response = await client.PostAsJsonAsync(
            BillsForBooking(seeded.BookingId),
            Request("BL-2026-0001", null) with { ShipperName = "   " },
            CancellationToken.None);
        var problem = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotNull(problem);
        Assert.True(problem.Errors.ContainsKey("shipperName"));
    }

    [Fact]
    public async Task Create_returns_bad_request_when_totals_exceed_the_booking()
    {
        using var client = await CreateManageClientAsync();
        var seeded = await SeedAsync(client, "BK-BOL-INT-0027");

        using var response = await client.PostAsJsonAsync(
            BillsForBooking(seeded.BookingId),
            Request("BL-2026-0001", null) with { PackageCount = 481 },
            CancellationToken.None);
        var problem = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotNull(problem);
        Assert.True(problem.Errors.ContainsKey("packageCount"));
    }

    [Fact]
    public async Task Create_returns_not_found_for_an_unknown_booking()
    {
        using var client = await CreateManageClientAsync();

        using var response = await client.PostAsJsonAsync(
            BillsForBooking(Guid.NewGuid()),
            Request("BL-2026-0001", null),
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Create_returns_conflict_for_a_duplicate_bill_number()
    {
        using var client = await CreateManageClientAsync();
        var seeded = await SeedAsync(client, "BK-BOL-INT-0003");
        await CreateDraftAsync(client, seeded.BookingId, "BL-2026-0001", null);

        using var response = await client.PostAsJsonAsync(
            BillsForBooking(seeded.BookingId),
            Request("BL-2026-0001", null),
            CancellationToken.None);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.NotNull(problem);
        Assert.Equal("Bill number is already in use.", problem.Title);
    }

    [Fact]
    public async Task Create_returns_conflict_for_a_container_of_another_booking()
    {
        using var client = await CreateManageClientAsync();
        var seeded = await SeedAsync(client, "BK-BOL-INT-0004");
        var other = await SeedAsync(client, "BK-BOL-INT-0005", "TCLU2222222");

        using var response = await client.PostAsJsonAsync(
            BillsForBooking(seeded.BookingId),
            Request("BL-2026-0001", [other.ContainerId]),
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Create_returns_conflict_for_a_container_already_on_a_bill()
    {
        using var client = await CreateManageClientAsync();
        var seeded = await SeedAsync(client, "BK-BOL-INT-0006");
        await CreateDraftAsync(client, seeded.BookingId, "BL-2026-0001", [seeded.ContainerId]);

        using var response = await client.PostAsJsonAsync(
            BillsForBooking(seeded.BookingId),
            Request("BL-2026-0002", [seeded.ContainerId]),
            CancellationToken.None);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.NotNull(problem);
        Assert.Equal("Container is already on another bill of lading.", problem.Title);
    }

    [Fact]
    public async Task Create_is_forbidden_for_a_reader()
    {
        using var manageClient = await CreateManageClientAsync();
        var seeded = await SeedAsync(manageClient, "BK-BOL-INT-0007");

        using var readClient = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Cargo.Read);
        using var response = await readClient.PostAsJsonAsync(
            BillsForBooking(seeded.BookingId),
            Request("BL-2026-0001", null),
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task The_list_returns_the_bills_of_a_booking()
    {
        using var client = await CreateManageClientAsync();
        var seeded = await SeedAsync(client, "BK-BOL-INT-0008");
        await CreateDraftAsync(client, seeded.BookingId, "BL-2026-0002", null);
        await CreateDraftAsync(client, seeded.BookingId, "BL-2026-0001", null);

        using var response = await client.GetAsync(
            BillsForBooking(seeded.BookingId),
            CancellationToken.None);
        var result = await response.Content.ReadFromJsonAsync<GetBillsOfLadingResponse>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(result);
        Assert.Equal("BK-BOL-INT-0008", result.BookingNumber);
        Assert.Equal(
            ["BL-2026-0001", "BL-2026-0002"],
            result.BillsOfLading.Select(bill => bill.BillNumber));
    }

    [Fact]
    public async Task The_list_is_available_to_a_reader()
    {
        using var manageClient = await CreateManageClientAsync();
        var seeded = await SeedAsync(manageClient, "BK-BOL-INT-0009");

        using var readClient = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Cargo.Read);
        using var response = await readClient.GetAsync(
            BillsForBooking(seeded.BookingId),
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task The_list_is_forbidden_without_the_read_permission()
    {
        using var manageClient = await CreateManageClientAsync();
        var seeded = await SeedAsync(manageClient, "BK-BOL-INT-0010");

        using var otherClient = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Fleet.Read);
        using var response = await otherClient.GetAsync(
            BillsForBooking(seeded.BookingId),
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task The_detail_returns_the_bill_and_its_history()
    {
        using var client = await CreateManageClientAsync();
        var seeded = await SeedAsync(client, "BK-BOL-INT-0011");
        var draft = await CreateDraftAsync(
            client,
            seeded.BookingId,
            "BL-2026-0001",
            [seeded.ContainerId]);

        using var response = await client.GetAsync(
            $"{BillsEndpoint}/{draft.BillOfLadingId}",
            CancellationToken.None);
        var result = await response.Content.ReadFromJsonAsync<GetBillOfLadingResponse>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(result);
        Assert.Equal("BL-2026-0001", result.BillOfLading.BillNumber);
        Assert.Equal(
            nameof(BillOfLadingAction.Drafted),
            Assert.Single(result.History).Action);
    }

    [Fact]
    public async Task The_detail_returns_not_found_for_an_unknown_bill()
    {
        using var client = await CreateManageClientAsync();

        using var response = await client.GetAsync(
            $"{BillsEndpoint}/{Guid.NewGuid()}",
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Update_replaces_the_draft_values()
    {
        using var client = await CreateManageClientAsync();
        var seeded = await SeedAsync(client, "BK-BOL-INT-0012");
        var draft = await CreateDraftAsync(client, seeded.BookingId, "BL-2026-0001", null);

        using var response = await client.PutAsJsonAsync(
            $"{BillsEndpoint}/{draft.BillOfLadingId}",
            Request("BL-2026-0001", [seeded.ContainerId]) with { ConsigneeName = "Baltic Freight" },
            CancellationToken.None);
        var details = await response.Content.ReadFromJsonAsync<BillOfLadingDetails>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(details);
        Assert.Equal("Baltic Freight", details.ConsigneeName);
        Assert.Single(details.Containers);
    }

    [Fact]
    public async Task Update_returns_conflict_once_the_bill_is_issued()
    {
        using var client = await CreateManageClientAsync();
        var seeded = await SeedAsync(client, "BK-BOL-INT-0013");
        var draft = await CreateDraftAsync(
            client,
            seeded.BookingId,
            "BL-2026-0001",
            [seeded.ContainerId]);

        await AdvanceBookingToAsync(seeded.BookingId, CargoStatus.Loaded);
        await IssueAsync(client, draft.BillOfLadingId);

        using var response = await client.PutAsJsonAsync(
            $"{BillsEndpoint}/{draft.BillOfLadingId}",
            Request("BL-2026-0001", [seeded.ContainerId]),
            CancellationToken.None);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.NotNull(problem);
        Assert.Equal("Bill of lading transition is not allowed.", problem.Title);
    }

    [Fact]
    public async Task Update_is_forbidden_for_a_reader()
    {
        using var manageClient = await CreateManageClientAsync();
        var seeded = await SeedAsync(manageClient, "BK-BOL-INT-0014");
        var draft = await CreateDraftAsync(manageClient, seeded.BookingId, "BL-2026-0001", null);

        using var readClient = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Cargo.Read);
        using var response = await readClient.PutAsJsonAsync(
            $"{BillsEndpoint}/{draft.BillOfLadingId}",
            Request("BL-2026-0001", null),
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Issue_locks_the_bill_and_records_the_issuer()
    {
        using var client = await CreateManageClientAsync();
        var seeded = await SeedAsync(client, "BK-BOL-INT-0015");
        var draft = await CreateDraftAsync(
            client,
            seeded.BookingId,
            "BL-2026-0001",
            [seeded.ContainerId]);

        await AdvanceBookingToAsync(seeded.BookingId, CargoStatus.Loaded);

        using var response = await client.PostAsync(
            $"{BillsEndpoint}/{draft.BillOfLadingId}/issue",
            null,
            CancellationToken.None);
        var details = await response.Content.ReadFromJsonAsync<BillOfLadingDetails>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(details);
        Assert.Equal(nameof(BillOfLadingStatus.Issued), details.Status);
        Assert.False(details.IsEditable);
        Assert.NotNull(details.IssuedByUserId);
        Assert.Equal("BK-BOL-INT-0015", details.SnapshotBookingNumber);
        Assert.True(Assert.Single(details.Containers).IsSnapshotted);
    }

    [Fact]
    public async Task Issue_returns_conflict_before_the_booking_is_loaded()
    {
        using var client = await CreateManageClientAsync();
        var seeded = await SeedAsync(client, "BK-BOL-INT-0016");
        var draft = await CreateDraftAsync(
            client,
            seeded.BookingId,
            "BL-2026-0001",
            [seeded.ContainerId]);

        using var response = await client.PostAsync(
            $"{BillsEndpoint}/{draft.BillOfLadingId}/issue",
            null,
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Issue_returns_conflict_without_a_container()
    {
        using var client = await CreateManageClientAsync();
        var seeded = await SeedAsync(client, "BK-BOL-INT-0017");
        var draft = await CreateDraftAsync(client, seeded.BookingId, "BL-2026-0001", null);

        await AdvanceBookingToAsync(seeded.BookingId, CargoStatus.Loaded);

        using var response = await client.PostAsync(
            $"{BillsEndpoint}/{draft.BillOfLadingId}/issue",
            null,
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Issue_returns_conflict_when_it_is_repeated()
    {
        using var client = await CreateManageClientAsync();
        var seeded = await SeedAsync(client, "BK-BOL-INT-0018");
        var draft = await CreateDraftAsync(
            client,
            seeded.BookingId,
            "BL-2026-0001",
            [seeded.ContainerId]);

        await AdvanceBookingToAsync(seeded.BookingId, CargoStatus.Loaded);
        await IssueAsync(client, draft.BillOfLadingId);

        using var response = await client.PostAsync(
            $"{BillsEndpoint}/{draft.BillOfLadingId}/issue",
            null,
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Issue_returns_not_found_for_an_unknown_bill()
    {
        using var client = await CreateManageClientAsync();

        using var response = await client.PostAsync(
            $"{BillsEndpoint}/{Guid.NewGuid()}/issue",
            null,
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Issue_is_forbidden_for_a_reader()
    {
        using var manageClient = await CreateManageClientAsync();
        var seeded = await SeedAsync(manageClient, "BK-BOL-INT-0019");
        var draft = await CreateDraftAsync(
            manageClient,
            seeded.BookingId,
            "BL-2026-0001",
            [seeded.ContainerId]);

        using var readClient = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Cargo.Read);
        using var response = await readClient.PostAsync(
            $"{BillsEndpoint}/{draft.BillOfLadingId}/issue",
            null,
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Void_records_the_reason_and_frees_the_containers()
    {
        using var client = await CreateManageClientAsync();
        var seeded = await SeedAsync(client, "BK-BOL-INT-0020");
        var draft = await CreateDraftAsync(
            client,
            seeded.BookingId,
            "BL-2026-0001",
            [seeded.ContainerId]);

        await AdvanceBookingToAsync(seeded.BookingId, CargoStatus.Loaded);
        await IssueAsync(client, draft.BillOfLadingId);

        using var response = await client.PostAsJsonAsync(
            $"{BillsEndpoint}/{draft.BillOfLadingId}/void",
            new VoidBillOfLadingRequest { Reason = "Wrong consignee" },
            CancellationToken.None);
        var details = await response.Content.ReadFromJsonAsync<BillOfLadingDetails>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(details);
        Assert.Equal(nameof(BillOfLadingStatus.Voided), details.Status);
        Assert.Equal("Wrong consignee", details.VoidReason);
        Assert.Equal("BK-BOL-INT-0020", details.SnapshotBookingNumber);

        var replacement = await CreateDraftAsync(
            client,
            seeded.BookingId,
            "BL-2026-0002",
            [seeded.ContainerId]);

        Assert.Single(replacement.Containers);
    }

    [Fact]
    public async Task Void_returns_bad_request_without_a_reason()
    {
        using var client = await CreateManageClientAsync();
        var seeded = await SeedAsync(client, "BK-BOL-INT-0021");
        var draft = await CreateDraftAsync(
            client,
            seeded.BookingId,
            "BL-2026-0001",
            [seeded.ContainerId]);

        await AdvanceBookingToAsync(seeded.BookingId, CargoStatus.Loaded);
        await IssueAsync(client, draft.BillOfLadingId);

        using var response = await client.PostAsJsonAsync(
            $"{BillsEndpoint}/{draft.BillOfLadingId}/void",
            new VoidBillOfLadingRequest { Reason = "   " },
            CancellationToken.None);
        var problem = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotNull(problem);
        Assert.True(problem.Errors.ContainsKey("reason"));
    }

    [Fact]
    public async Task Void_returns_conflict_for_a_draft()
    {
        using var client = await CreateManageClientAsync();
        var seeded = await SeedAsync(client, "BK-BOL-INT-0022");
        var draft = await CreateDraftAsync(client, seeded.BookingId, "BL-2026-0001", null);

        using var response = await client.PostAsJsonAsync(
            $"{BillsEndpoint}/{draft.BillOfLadingId}/void",
            new VoidBillOfLadingRequest { Reason = "Wrong consignee" },
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Void_is_forbidden_for_a_reader()
    {
        using var manageClient = await CreateManageClientAsync();
        var seeded = await SeedAsync(manageClient, "BK-BOL-INT-0023");
        var draft = await CreateDraftAsync(
            manageClient,
            seeded.BookingId,
            "BL-2026-0001",
            [seeded.ContainerId]);

        using var readClient = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Cargo.Read);
        using var response = await readClient.PostAsJsonAsync(
            $"{BillsEndpoint}/{draft.BillOfLadingId}/void",
            new VoidBillOfLadingRequest { Reason = "Wrong consignee" },
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task The_print_model_marks_a_draft_and_is_available_to_a_reader()
    {
        using var manageClient = await CreateManageClientAsync();
        var seeded = await SeedAsync(manageClient, "BK-BOL-INT-0024");
        var draft = await CreateDraftAsync(
            manageClient,
            seeded.BookingId,
            "BL-2026-0001",
            [seeded.ContainerId]);

        using var readClient = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Cargo.Read);
        using var response = await readClient.GetAsync(
            $"{BillsEndpoint}/{draft.BillOfLadingId}/print",
            CancellationToken.None);
        var model = await response.Content.ReadFromJsonAsync<BillOfLadingPrintModel>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(model);
        Assert.Equal(nameof(BillOfLadingStatus.Draft), model.Watermark);
        Assert.Equal("Adriatic Trade", model.ShipperName);
    }

    [Fact]
    public async Task The_print_model_of_an_issued_bill_has_no_watermark()
    {
        using var client = await CreateManageClientAsync();
        var seeded = await SeedAsync(client, "BK-BOL-INT-0025");
        var draft = await CreateDraftAsync(
            client,
            seeded.BookingId,
            "BL-2026-0001",
            [seeded.ContainerId]);

        await AdvanceBookingToAsync(seeded.BookingId, CargoStatus.Loaded);
        await IssueAsync(client, draft.BillOfLadingId);

        using var response = await client.GetAsync(
            $"{BillsEndpoint}/{draft.BillOfLadingId}/print",
            CancellationToken.None);
        var model = await response.Content.ReadFromJsonAsync<BillOfLadingPrintModel>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(model);
        Assert.Null(model.Watermark);
        Assert.Equal("BK-BOL-INT-0025", model.BookingNumber);
    }

    [Fact]
    public async Task The_print_model_is_forbidden_without_the_read_permission()
    {
        using var manageClient = await CreateManageClientAsync();
        var seeded = await SeedAsync(manageClient, "BK-BOL-INT-0026");
        var draft = await CreateDraftAsync(manageClient, seeded.BookingId, "BL-2026-0001", null);

        using var otherClient = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Fleet.Read);
        using var response = await otherClient.GetAsync(
            $"{BillsEndpoint}/{draft.BillOfLadingId}/print",
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task The_print_model_returns_not_found_for_an_unknown_bill()
    {
        using var client = await CreateManageClientAsync();

        using var response = await client.GetAsync(
            $"{BillsEndpoint}/{Guid.NewGuid()}/print",
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private static string BillsForBooking(Guid bookingId)
    {
        return $"{BookingsEndpoint}/{bookingId}/bills-of-lading";
    }

    private Task<HttpClient> CreateManageClientAsync()
    {
        return _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Cargo.Manage,
            Permissions.Cargo.Read);
    }

    private static async Task<BillOfLadingDetails> CreateDraftAsync(
        HttpClient client,
        Guid bookingId,
        string billNumber,
        IReadOnlyList<Guid>? containerIds)
    {
        using var response = await client.PostAsJsonAsync(
            BillsForBooking(bookingId),
            Request(billNumber, containerIds),
            CancellationToken.None);
        var details = await response.Content.ReadFromJsonAsync<BillOfLadingDetails>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(details);

        return details;
    }

    private static async Task IssueAsync(HttpClient client, Guid billOfLadingId)
    {
        using var response = await client.PostAsync(
            $"{BillsEndpoint}/{billOfLadingId}/issue",
            null,
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
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

    private static SaveBillOfLadingDraftRequest Request(
        string billNumber,
        IReadOnlyList<Guid>? containerIds)
    {
        return new SaveBillOfLadingDraftRequest
        {
            BillNumber = billNumber,
            ShipperName = "Adriatic Trade",
            ShipperAddress = "Bulevar Revolucije 1, Bar",
            ConsigneeName = "Nordic Imports",
            ConsigneeAddress = "Hamngatan 4, Goteborg",
            NotifyPartyName = "Port Agency",
            NotifyPartyAddress = "Obala 2, Bar",
            PlaceOfReceipt = "Bar",
            PortOfLoading = "Rotterdam",
            PortOfDischarge = "Bar",
            PlaceOfDelivery = "Podgorica",
            FreightTerms = nameof(FreightTerms.Prepaid),
            CargoMarks = "ADT/2026/0001",
            CargoDescription = "Machine parts, palletised",
            PackageCount = 480,
            WeightKg = 12400.000m,
            IssuePlace = "Bar",
            RequestedIssueDate = new DateOnly(2026, 9, 9),
            ContainerIds = containerIds,
        };
    }

    private static async Task<SeededBooking> SeedAsync(
        HttpClient client,
        string bookingNumber,
        string containerNumber = "MSKU1111111")
    {
        var bookingId = await RegisterBookingAsync(client, bookingNumber);

        using var response = await client.PostAsJsonAsync(
            $"{BookingsEndpoint}/{bookingId}/containers",
            new AddCargoContainerRequest
            {
                ContainerNumber = containerNumber,
                ContainerType = "40HC",
                PortOfLoadingCode = "NLRTM",
                PortOfDischargeCode = "USNYC",
                WeightKg = 12500m,
            },
            CancellationToken.None);
        var result = await response.Content.ReadFromJsonAsync<AddCargoContainerResponse>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(result);

        return new SeededBooking(bookingId, result.Id);
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
                WeightKg = 12400.000m,
                PackageCount = 480,
                IsHazardous = false,
            },
            CancellationToken.None);
        var result = await response.Content.ReadFromJsonAsync<RegisterCargoBookingResponse>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(result);

        return result.Id;
    }

    private sealed record SeededBooking(Guid BookingId, Guid ContainerId);
}
