using System.Net;
using System.Net.Http.Json;
using HarborOps.IntegrationTests.Authentication;
using HarborOps.Modules.Cargo.Contracts;
using HarborOps.Modules.Cargo.Domain;
using HarborOps.Modules.Identity.Contracts;

namespace HarborOps.IntegrationTests.Cargo;

public sealed class BillsOfLadingSecurityTests : IntegrationTestBase
{
    private const string BookingsEndpoint = "/api/cargo/bookings";
    private const string BillsEndpoint = "/api/cargo/bills-of-lading";

    private const string ScriptPayload = "<script>alert('x')</script>";
    private const string QuotePayload = "Bar \"Adriatic\" & Sons <Ltd>";

    private static readonly Guid VoyageId = Guid.Parse("0d9e8f7a-6b5c-4d4e-9f3a-2b1c0d9e8f7a");

    private readonly HarborOpsApiFactory _application;

    public BillsOfLadingSecurityTests(HarborOpsApiFactory application)
        : base(application)
    {
        _application = application;
    }

    [Fact]
    public async Task An_anonymous_caller_cannot_read_a_printable_bill()
    {
        using var client = await CreateManageClientAsync();
        var seeded = await SeedAsync(client, "BK-BOL-SEC-0001");
        var draft = await CreateDraftAsync(client, seeded.BookingId, [seeded.ContainerId]);

        using var anonymousClient = _application.CreateClient();
        using var response = await anonymousClient.GetAsync(
            $"{BillsEndpoint}/{draft.BillOfLadingId}/print",
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task An_anonymous_caller_cannot_read_a_bill_or_its_list()
    {
        using var client = await CreateManageClientAsync();
        var seeded = await SeedAsync(client, "BK-BOL-SEC-0002");
        var draft = await CreateDraftAsync(client, seeded.BookingId, [seeded.ContainerId]);

        using var anonymousClient = _application.CreateClient();

        using var detailResponse = await anonymousClient.GetAsync(
            $"{BillsEndpoint}/{draft.BillOfLadingId}",
            CancellationToken.None);
        using var listResponse = await anonymousClient.GetAsync(
            $"{BookingsEndpoint}/{seeded.BookingId}/bills-of-lading",
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Unauthorized, detailResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, listResponse.StatusCode);
    }

    [Fact]
    public async Task An_anonymous_caller_cannot_change_a_bill()
    {
        using var client = await CreateManageClientAsync();
        var seeded = await SeedAsync(client, "BK-BOL-SEC-0003");
        var draft = await CreateDraftAsync(client, seeded.BookingId, [seeded.ContainerId]);

        using var anonymousClient = _application.CreateClient();

        using var issueResponse = await anonymousClient.PostAsync(
            $"{BillsEndpoint}/{draft.BillOfLadingId}/issue",
            null,
            CancellationToken.None);
        using var voidResponse = await anonymousClient.PostAsJsonAsync(
            $"{BillsEndpoint}/{draft.BillOfLadingId}/void",
            new VoidBillOfLadingRequest { Reason = "Wrong consignee" },
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Unauthorized, issueResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, voidResponse.StatusCode);
    }

    [Fact]
    public async Task A_caller_of_another_module_cannot_reach_a_printable_bill()
    {
        using var client = await CreateManageClientAsync();
        var seeded = await SeedAsync(client, "BK-BOL-SEC-0004");
        var draft = await CreateDraftAsync(client, seeded.BookingId, [seeded.ContainerId]);

        using var otherClient = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Fleet.Read);
        using var response = await otherClient.GetAsync(
            $"{BillsEndpoint}/{draft.BillOfLadingId}/print",
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task A_reader_cannot_issue_or_void_a_bill()
    {
        using var client = await CreateManageClientAsync();
        var seeded = await SeedAsync(client, "BK-BOL-SEC-0005");
        var draft = await CreateDraftAsync(client, seeded.BookingId, [seeded.ContainerId]);

        using var readClient = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Cargo.Read);

        using var issueResponse = await readClient.PostAsync(
            $"{BillsEndpoint}/{draft.BillOfLadingId}/issue",
            null,
            CancellationToken.None);
        using var voidResponse = await readClient.PostAsJsonAsync(
            $"{BillsEndpoint}/{draft.BillOfLadingId}/void",
            new VoidBillOfLadingRequest { Reason = "Wrong consignee" },
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Forbidden, issueResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, voidResponse.StatusCode);
    }

    [Fact]
    public async Task Party_content_is_stored_verbatim_and_served_as_json()
    {
        using var client = await CreateManageClientAsync();
        var seeded = await SeedAsync(client, "BK-BOL-SEC-0006");

        var draft = await CreateDraftAsync(
            client,
            seeded.BookingId,
            [seeded.ContainerId],
            shipperName: ScriptPayload,
            consigneeName: QuotePayload);

        Assert.Equal(ScriptPayload, draft.ShipperName);
        Assert.Equal(QuotePayload, draft.ConsigneeName);

        using var response = await client.GetAsync(
            $"{BillsEndpoint}/{draft.BillOfLadingId}",
            CancellationToken.None);
        var result = await response.Content.ReadFromJsonAsync<GetBillOfLadingResponse>(
            CancellationToken.None);

        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.NotNull(result);
        Assert.Equal(ScriptPayload, result.BillOfLading.ShipperName);
        Assert.Equal(QuotePayload, result.BillOfLading.ConsigneeName);
    }

    [Fact]
    public async Task Printable_content_is_served_as_json_without_raw_markup()
    {
        using var client = await CreateManageClientAsync();
        var seeded = await SeedAsync(client, "BK-BOL-SEC-0007");

        var draft = await CreateDraftAsync(
            client,
            seeded.BookingId,
            [seeded.ContainerId],
            shipperName: ScriptPayload,
            cargoDescription: ScriptPayload);

        using var response = await client.GetAsync(
            $"{BillsEndpoint}/{draft.BillOfLadingId}/print",
            CancellationToken.None);
        var model = await response.Content.ReadFromJsonAsync<BillOfLadingPrintModel>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.NotEqual("text/html", response.Content.Headers.ContentType?.MediaType);
        Assert.NotNull(model);
        Assert.Equal(ScriptPayload, model.ShipperName);
        Assert.Equal(ScriptPayload, model.CargoDescription);
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
        IReadOnlyList<Guid> containerIds,
        string shipperName = "Adriatic Trade",
        string consigneeName = "Nordic Imports",
        string cargoDescription = "Machine parts, palletised")
    {
        using var response = await client.PostAsJsonAsync(
            $"{BookingsEndpoint}/{bookingId}/bills-of-lading",
            new SaveBillOfLadingDraftRequest
            {
                BillNumber = "BL-2026-0001",
                ShipperName = shipperName,
                ShipperAddress = "Bulevar Revolucije 1, Bar",
                ConsigneeName = consigneeName,
                ConsigneeAddress = "Hamngatan 4, Goteborg",
                PlaceOfReceipt = "Bar",
                PortOfLoading = "Rotterdam",
                PortOfDischarge = "Bar",
                PlaceOfDelivery = "Podgorica",
                FreightTerms = nameof(FreightTerms.Prepaid),
                CargoMarks = "ADT/2026/0001",
                CargoDescription = cargoDescription,
                PackageCount = 480,
                WeightKg = 12400.000m,
                IssuePlace = "Bar",
                RequestedIssueDate = new DateOnly(2026, 9, 9),
                ContainerIds = containerIds,
            },
            CancellationToken.None);
        var details = await response.Content.ReadFromJsonAsync<BillOfLadingDetails>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(details);

        return details;
    }

    private static async Task<SeededBooking> SeedAsync(HttpClient client, string bookingNumber)
    {
        using var bookingResponse = await client.PostAsJsonAsync(
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
        var booking = await bookingResponse.Content
            .ReadFromJsonAsync<RegisterCargoBookingResponse>(CancellationToken.None);

        Assert.Equal(HttpStatusCode.Created, bookingResponse.StatusCode);
        Assert.NotNull(booking);

        using var containerResponse = await client.PostAsJsonAsync(
            $"{BookingsEndpoint}/{booking.Id}/containers",
            new AddCargoContainerRequest
            {
                ContainerNumber = "MSKU1111111",
                ContainerType = "40HC",
                PortOfLoadingCode = "NLRTM",
                PortOfDischargeCode = "USNYC",
                WeightKg = 12500m,
            },
            CancellationToken.None);
        var container = await containerResponse.Content
            .ReadFromJsonAsync<AddCargoContainerResponse>(CancellationToken.None);

        Assert.Equal(HttpStatusCode.Created, containerResponse.StatusCode);
        Assert.NotNull(container);

        return new SeededBooking(booking.Id, container.Id);
    }

    private sealed record SeededBooking(Guid BookingId, Guid ContainerId);
}
