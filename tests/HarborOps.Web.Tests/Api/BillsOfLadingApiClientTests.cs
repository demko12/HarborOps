using System.Net;
using System.Text;
using HarborOps.Web.Api.Cargo;

namespace HarborOps.Web.Tests.Api;

public sealed class BillsOfLadingApiClientTests
{
    private const string BookingId = "20000000-0000-0000-0000-000000000001";
    private const string BillId = "80000000-0000-0000-0000-000000000008";

    private const string BillJson = """
        {"billOfLadingId":"80000000-0000-0000-0000-000000000008","cargoBookingId":"20000000-0000-0000-0000-000000000001","billNumber":"BL-2026-0001","status":"Draft","shipperName":"Adriatic Trade","shipperAddress":"Bulevar Revolucije 1, Bar","consigneeName":"Nordic Imports","consigneeAddress":"Hamngatan 4, Goteborg","notifyPartyName":"Port Agency","notifyPartyAddress":"Obala 2, Bar","placeOfReceipt":"Bar","portOfLoading":"Rotterdam","portOfDischarge":"Bar","placeOfDelivery":"Podgorica","freightTerms":"Prepaid","cargoMarks":"ADT/2026/0001","cargoDescription":"Machine parts, palletised","packageCount":480,"weightKg":12400.000,"issuePlace":"Bar","requestedIssueDate":"2026-09-09","issuedByUserId":null,"issuedAtUtc":null,"voidedByUserId":null,"voidedAtUtc":null,"voidReason":null,"snapshotBookingNumber":null,"snapshotVoyageId":null,"snapshotCargoType":null,"snapshotBookingDescription":null,"snapshotBookingPackageCount":null,"snapshotBookingWeightKg":null,"snapshotBookingStatus":null,"isEditable":true,"canIssue":true,"issueBlockedReason":null,"createdUtc":"2026-09-09T08:00:00Z","containers":[{"containerId":"90000000-0000-0000-0000-000000000009","containerNumber":"MSKU1111111","containerType":"40HC","isSnapshotted":false}]}
        """;

    private const string ListJson = """
        {"cargoBookingId":"20000000-0000-0000-0000-000000000001","bookingNumber":"BK-2026-0143","bookingStatus":"Loaded","billsOfLading":[{"billOfLadingId":"80000000-0000-0000-0000-000000000008","billNumber":"BL-2026-0001","status":"Draft","shipperName":"Adriatic Trade","consigneeName":"Nordic Imports","portOfLoading":"Rotterdam","portOfDischarge":"Bar","containerCount":1,"issuedByUserId":null,"issuedAtUtc":null,"voidedAtUtc":null,"createdUtc":"2026-09-09T08:00:00Z"}]}
        """;

    private const string PrintJson = """
        {"billOfLadingId":"80000000-0000-0000-0000-000000000008","billNumber":"BL-2026-0001","status":"Draft","watermark":"Draft","shipperName":"Adriatic Trade","shipperAddress":"Bulevar Revolucije 1, Bar","consigneeName":"Nordic Imports","consigneeAddress":"Hamngatan 4, Goteborg","notifyPartyName":null,"notifyPartyAddress":null,"placeOfReceipt":"Bar","portOfLoading":"Rotterdam","portOfDischarge":"Bar","placeOfDelivery":"Podgorica","freightTerms":"Prepaid","cargoMarks":"ADT/2026/0001","cargoDescription":"Machine parts, palletised","packageCount":480,"weightKg":12400.000,"issuePlace":"Bar","requestedIssueDate":"2026-09-09","bookingNumber":null,"issuedByUserId":null,"issuedAtUtc":null,"voidedByUserId":null,"voidedAtUtc":null,"voidReason":null,"containers":[{"containerId":"90000000-0000-0000-0000-000000000009","containerNumber":"MSKU1111111","containerType":"40HC","isSnapshotted":false}]}
        """;

    private const string ViewJson = $$"""
        {"billOfLading":{{BillJson}},"history":[{"billOfLadingEventId":"a0000000-0000-0000-0000-00000000000a","action":"Drafted","fromStatus":"Draft","toStatus":"Draft","notes":null,"actorUserId":"70000000-0000-0000-0000-000000000006","occurredUtc":"2026-09-09T08:00:00Z"}]}
        """;

    [Fact]
    public async Task GetBillsOfLadingAsync_requests_the_booking_path()
    {
        Uri? requestedUri = null;
        using var httpClient = CreateHttpClient((request, _) =>
        {
            requestedUri = request.RequestUri;

            return Json(HttpStatusCode.OK, ListJson);
        });

        var result = await CreateClient(httpClient).GetBillsOfLadingAsync(
            Guid.Parse(BookingId),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(
            $"https://api.harborops.test/api/cargo/bookings/{BookingId}/bills-of-lading",
            requestedUri?.ToString());
        Assert.Equal("BK-2026-0143", result.Value!.BookingNumber);
        Assert.Equal("BL-2026-0001", Assert.Single(result.Value.BillsOfLading).BillNumber);
    }

    [Fact]
    public async Task GetBillOfLadingAsync_returns_the_bill_and_its_history()
    {
        using var httpClient = CreateHttpClient((_, _) => Json(HttpStatusCode.OK, ViewJson));

        var result = await CreateClient(httpClient).GetBillOfLadingAsync(
            Guid.Parse(BillId),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("BL-2026-0001", result.Value!.BillOfLading.BillNumber);
        Assert.True(result.Value.BillOfLading.IsEditable);
        Assert.Equal("MSKU1111111", Assert.Single(result.Value.BillOfLading.Containers).ContainerNumber);
        Assert.Equal("Drafted", Assert.Single(result.Value.History).Action);
    }

    [Fact]
    public async Task GetBillOfLadingPrintAsync_requests_the_print_path()
    {
        Uri? requestedUri = null;
        using var httpClient = CreateHttpClient((request, _) =>
        {
            requestedUri = request.RequestUri;

            return Json(HttpStatusCode.OK, PrintJson);
        });

        var result = await CreateClient(httpClient).GetBillOfLadingPrintAsync(
            Guid.Parse(BillId),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(
            $"https://api.harborops.test/api/cargo/bills-of-lading/{BillId}/print",
            requestedUri?.ToString());
        Assert.Equal("Draft", result.Value!.Watermark);
    }

    [Fact]
    public async Task CreateBillOfLadingAsync_posts_to_the_booking_path()
    {
        HttpMethod? method = null;
        Uri? requestedUri = null;
        using var httpClient = CreateHttpClient((request, _) =>
        {
            method = request.Method;
            requestedUri = request.RequestUri;

            return Json(HttpStatusCode.Created, BillJson);
        });

        var result = await CreateClient(httpClient).CreateBillOfLadingAsync(
            Guid.Parse(BookingId),
            Request(),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(HttpMethod.Post, method);
        Assert.Equal(
            $"https://api.harborops.test/api/cargo/bookings/{BookingId}/bills-of-lading",
            requestedUri?.ToString());
        Assert.Equal("BL-2026-0001", result.Value!.BillNumber);
    }

    [Fact]
    public async Task UpdateBillOfLadingAsync_puts_to_the_bill_path()
    {
        HttpMethod? method = null;
        Uri? requestedUri = null;
        using var httpClient = CreateHttpClient((request, _) =>
        {
            method = request.Method;
            requestedUri = request.RequestUri;

            return Json(HttpStatusCode.OK, BillJson);
        });

        var result = await CreateClient(httpClient).UpdateBillOfLadingAsync(
            Guid.Parse(BillId),
            Request(),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(HttpMethod.Put, method);
        Assert.Equal(
            $"https://api.harborops.test/api/cargo/bills-of-lading/{BillId}",
            requestedUri?.ToString());
    }

    [Fact]
    public async Task IssueBillOfLadingAsync_posts_without_a_body()
    {
        HttpContent? content = null;
        Uri? requestedUri = null;
        using var httpClient = CreateHttpClient((request, _) =>
        {
            content = request.Content;
            requestedUri = request.RequestUri;

            return Json(HttpStatusCode.OK, BillJson);
        });

        var result = await CreateClient(httpClient).IssueBillOfLadingAsync(
            Guid.Parse(BillId),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(content);
        Assert.Equal(
            $"https://api.harborops.test/api/cargo/bills-of-lading/{BillId}/issue",
            requestedUri?.ToString());
    }

    [Fact]
    public async Task VoidBillOfLadingAsync_posts_the_reason()
    {
        Uri? requestedUri = null;
        using var httpClient = CreateHttpClient((request, _) =>
        {
            requestedUri = request.RequestUri;

            return Json(HttpStatusCode.OK, BillJson);
        });

        var result = await CreateClient(httpClient).VoidBillOfLadingAsync(
            Guid.Parse(BillId),
            new VoidBillOfLadingWebRequest("Wrong consignee"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(
            $"https://api.harborops.test/api/cargo/bills-of-lading/{BillId}/void",
            requestedUri?.ToString());
    }

    [Fact]
    public async Task A_validation_failure_is_reported_with_its_field_errors()
    {
        using var httpClient = CreateHttpClient((_, _) => Json(
            HttpStatusCode.BadRequest,
            """{"title":"One or more validation errors occurred.","status":400,"errors":{"shipperName":["A shipper name is required."]}}"""));

        var result = await CreateClient(httpClient).CreateBillOfLadingAsync(
            Guid.Parse(BookingId),
            Request(),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.True(result.Failure!.Errors.ContainsKey("shipperName"));
    }

    [Fact]
    public async Task A_conflict_is_reported_as_a_conflict()
    {
        using var httpClient = CreateHttpClient((_, _) => Json(
            HttpStatusCode.Conflict,
            """{"title":"Bill number is already in use.","status":409}"""));

        var result = await CreateClient(httpClient).CreateBillOfLadingAsync(
            Guid.Parse(BookingId),
            Request(),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.True(result.Failure!.IsConflict);
    }

    [Fact]
    public async Task An_unknown_bill_is_reported_as_not_found()
    {
        using var httpClient = CreateHttpClient((_, _) => Json(
            HttpStatusCode.NotFound,
            """{"title":"Bill of lading not found.","status":404}"""));

        var result = await CreateClient(httpClient).GetBillOfLadingAsync(
            Guid.Parse(BillId),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.True(result.Failure!.IsNotFound);
    }

    [Fact]
    public async Task An_unreachable_api_is_reported_as_a_failure()
    {
        using var httpClient = CreateHttpClient(
            (_, _) => throw new HttpRequestException("offline"));

        var result = await CreateClient(httpClient).GetBillsOfLadingAsync(
            Guid.Parse(BookingId),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.NotNull(result.Failure);
    }

    private static SaveBillOfLadingWebRequest Request()
    {
        return new SaveBillOfLadingWebRequest(
            "BL-2026-0001",
            "Adriatic Trade",
            "Bulevar Revolucije 1, Bar",
            "Nordic Imports",
            "Hamngatan 4, Goteborg",
            "Port Agency",
            "Obala 2, Bar",
            "Bar",
            "Rotterdam",
            "Bar",
            "Podgorica",
            "Prepaid",
            "ADT/2026/0001",
            "Machine parts, palletised",
            480,
            12400m,
            "Bar",
            new DateOnly(2026, 9, 9),
            [Guid.Parse("90000000-0000-0000-0000-000000000009")]);
    }

    private static CargoBookingsApiClient CreateClient(HttpClient httpClient)
    {
        return new CargoBookingsApiClient(httpClient);
    }

    private static HttpClient CreateHttpClient(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responseFactory)
    {
        return new HttpClient(new StubHttpMessageHandler(responseFactory))
        {
            BaseAddress = new Uri("https://api.harborops.test/"),
        };
    }

    private static Task<HttpResponseMessage> Json(HttpStatusCode statusCode, string json)
    {
        return Task.FromResult(new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/problem+json"),
        });
    }

    private sealed class StubHttpMessageHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responseFactory)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return responseFactory(request, cancellationToken);
        }
    }
}
