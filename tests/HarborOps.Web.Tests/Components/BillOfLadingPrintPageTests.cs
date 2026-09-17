using System.Net;
using System.Security.Claims;
using System.Text;
using Bunit;
using HarborOps.Web.Api;
using HarborOps.Web.Api.Cargo;
using HarborOps.Web.Components.Pages.Cargo;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace HarborOps.Web.Tests.Components;

public sealed class BillOfLadingPrintPageTests : BunitContext
{
    private const string BookingId = "20000000-0000-0000-0000-000000000001";
    private const string BillId = "80000000-0000-0000-0000-000000000008";

    private const string ScriptPayload = "<script>alert('x')</script>";

    private const string DraftPrint = """
        {"billOfLadingId":"80000000-0000-0000-0000-000000000008","billNumber":"BL-2026-0001","status":"Draft","watermark":"Draft","shipperName":"Adriatic Trade","shipperAddress":"Bulevar Revolucije 1, Bar","consigneeName":"Nordic Imports","consigneeAddress":"Hamngatan 4, Goteborg","notifyPartyName":null,"notifyPartyAddress":null,"placeOfReceipt":"Bar","portOfLoading":"Rotterdam","portOfDischarge":"Bar","placeOfDelivery":"Podgorica","freightTerms":"Prepaid","cargoMarks":"ADT/2026/0001","cargoDescription":"Machine parts, palletised","packageCount":480,"weightKg":12400.000,"issuePlace":"Bar","requestedIssueDate":"2026-09-09","bookingNumber":null,"issuedByUserId":null,"issuedAtUtc":null,"voidedByUserId":null,"voidedAtUtc":null,"voidReason":null,"containers":[{"containerId":"90000000-0000-0000-0000-000000000009","containerNumber":"MSKU1111111","containerType":"40HC","isSnapshotted":false}]}
        """;

    private const string IssuedPrint = """
        {"billOfLadingId":"80000000-0000-0000-0000-000000000008","billNumber":"BL-2026-0001","status":"Issued","watermark":null,"shipperName":"Adriatic Trade","shipperAddress":"Bulevar Revolucije 1, Bar","consigneeName":"Nordic Imports","consigneeAddress":"Hamngatan 4, Goteborg","notifyPartyName":"Port Agency","notifyPartyAddress":"Obala 2, Bar","placeOfReceipt":"Bar","portOfLoading":"Rotterdam","portOfDischarge":"Bar","placeOfDelivery":"Podgorica","freightTerms":"Prepaid","cargoMarks":"ADT/2026/0001","cargoDescription":"Machine parts, palletised","packageCount":480,"weightKg":12400.000,"issuePlace":"Bar","requestedIssueDate":"2026-09-09","bookingNumber":"BK-2026-0143","issuedByUserId":"70000000-0000-0000-0000-000000000006","issuedAtUtc":"2026-09-09T10:30:00Z","voidedByUserId":null,"voidedAtUtc":null,"voidReason":null,"containers":[{"containerId":"90000000-0000-0000-0000-000000000009","containerNumber":"MSKU1111111","containerType":"40HC","isSnapshotted":true}]}
        """;

    private const string VoidedPrint = """
        {"billOfLadingId":"80000000-0000-0000-0000-000000000008","billNumber":"BL-2026-0001","status":"Voided","watermark":"Voided","shipperName":"Adriatic Trade","shipperAddress":"Bulevar Revolucije 1, Bar","consigneeName":"Nordic Imports","consigneeAddress":"Hamngatan 4, Goteborg","notifyPartyName":null,"notifyPartyAddress":null,"placeOfReceipt":"Bar","portOfLoading":"Rotterdam","portOfDischarge":"Bar","placeOfDelivery":"Podgorica","freightTerms":"Prepaid","cargoMarks":"ADT/2026/0001","cargoDescription":"Machine parts, palletised","packageCount":480,"weightKg":12400.000,"issuePlace":"Bar","requestedIssueDate":"2026-09-09","bookingNumber":"BK-2026-0143","issuedByUserId":"70000000-0000-0000-0000-000000000006","issuedAtUtc":"2026-09-09T10:30:00Z","voidedByUserId":"70000000-0000-0000-0000-000000000006","voidedAtUtc":"2026-09-09T14:45:00Z","voidReason":"Wrong consignee","containers":[]}
        """;

    private static readonly string HostilePrint = $$"""
        {"billOfLadingId":"80000000-0000-0000-0000-000000000008","billNumber":"BL-2026-0001","status":"Draft","watermark":"Draft","shipperName":"{{ScriptPayload}}","shipperAddress":"Bar & Sons <Ltd>","consigneeName":"Nordic Imports","consigneeAddress":"Hamngatan 4, Goteborg","notifyPartyName":null,"notifyPartyAddress":null,"placeOfReceipt":"Bar","portOfLoading":"Rotterdam","portOfDischarge":"Bar","placeOfDelivery":"Podgorica","freightTerms":"Prepaid","cargoMarks":"ADT/2026/0001","cargoDescription":"{{ScriptPayload}}","packageCount":480,"weightKg":12400.000,"issuePlace":"Bar","requestedIssueDate":"2026-09-09","bookingNumber":null,"issuedByUserId":null,"issuedAtUtc":null,"voidedByUserId":null,"voidedAtUtc":null,"voidReason":null,"containers":[]}
        """;

    private readonly List<HttpRequestMessage> requests = [];

    private Func<HttpRequestMessage, Task<HttpResponseMessage>> respond =
        _ => Json(HttpStatusCode.OK, DraftPrint);

    public BillOfLadingPrintPageTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public void The_printable_bill_is_requested_from_the_print_route()
    {
        RenderPage();

        Assert.Equal(
            $"/api/cargo/bills-of-lading/{BillId}/print",
            requests[0].RequestUri!.AbsolutePath);
    }

    [Fact]
    public void A_draft_carries_a_draft_watermark()
    {
        var page = RenderPage();

        Assert.Equal("Draft", page.Find(".print-watermark").TextContent.Trim());
    }

    [Fact]
    public void A_voided_bill_carries_a_voided_watermark_and_its_reason()
    {
        respond = _ => Json(HttpStatusCode.OK, VoidedPrint);

        var page = RenderPage();

        Assert.Equal("Voided", page.Find(".print-watermark").TextContent.Trim());
        Assert.Contains(
            "Wrong consignee",
            page.Find(".print-void-reason").TextContent,
            StringComparison.Ordinal);
    }

    [Fact]
    public void An_issued_bill_carries_no_watermark()
    {
        respond = _ => Json(HttpStatusCode.OK, IssuedPrint);

        var page = RenderPage();

        Assert.Empty(page.FindAll(".print-watermark"));
        Assert.Contains("BK-2026-0143", page.Find(".print-foot").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void The_sheet_shows_the_parties_the_routing_and_the_totals()
    {
        respond = _ => Json(HttpStatusCode.OK, IssuedPrint);

        var page = RenderPage();

        Assert.Equal("BL-2026-0001", page.Find(".print-number").TextContent.Trim());
        Assert.Contains("Adriatic Trade", page.Find(".print-parties").TextContent, StringComparison.Ordinal);
        Assert.Contains("Port Agency", page.Find(".print-parties").TextContent, StringComparison.Ordinal);
        Assert.Contains("Rotterdam", page.Find(".print-routing").TextContent, StringComparison.Ordinal);
        Assert.Contains("480", page.Find(".print-totals").TextContent, StringComparison.Ordinal);
        Assert.Contains("MSKU1111111", page.Find(".print-table").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void A_missing_notify_party_is_stated_rather_than_left_blank()
    {
        var page = RenderPage();

        Assert.Contains(
            "Not stated",
            page.Find(".print-party-empty").TextContent,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Party_and_cargo_content_is_html_encoded()
    {
        respond = _ => Json(HttpStatusCode.OK, HostilePrint);

        var page = RenderPage();

        Assert.DoesNotContain("<script>", page.Markup, StringComparison.Ordinal);
        Assert.Contains("&lt;script&gt;", page.Markup, StringComparison.Ordinal);
        Assert.Equal(ScriptPayload, page.Find(".print-party-name").TextContent);
        Assert.Contains(
            "Bar & Sons <Ltd>",
            page.Find(".print-party-address").TextContent,
            StringComparison.Ordinal);
    }

    [Fact]
    public void A_failed_load_can_be_retried()
    {
        respond = _ => Json(
            HttpStatusCode.ServiceUnavailable,
            """{"title":"Unable to load the printable bill of lading","status":503}""");

        var page = RenderPage();

        Assert.NotEmpty(page.FindAll(".api-error-state"));

        respond = _ => Json(HttpStatusCode.OK, DraftPrint);
        page.Find(".api-error-state button").Click();

        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll(".print-sheet")));
    }

    [Fact]
    public void The_toolbar_links_back_to_the_booking()
    {
        var page = RenderPage();

        Assert.Equal($"/cargo/{BookingId}", page.Find(".print-back").GetAttribute("href"));
    }

    private IRenderedComponent<BillOfLadingPrint> RenderPage()
    {
        var httpClient = new HttpClient(new StubHttpMessageHandler((request, _) =>
        {
            requests.Add(request);

            return respond(request);
        }))
        {
            BaseAddress = new Uri("https://api.harborops.test/"),
        };

        Services.AddSingleton(new CargoBookingsApiClient(httpClient));
        ConfigureAuthorization(CurrentUserAuthorizationState.CargoReadPermission);

        var page = Render<BillOfLadingPrint>(parameters => parameters
            .Add(component => component.BookingId, Guid.Parse(BookingId))
            .Add(component => component.BillOfLadingId, Guid.Parse(BillId)));
        page.WaitForAssertion(() => Assert.NotEmpty(requests));

        return page;
    }

    private void ConfigureAuthorization(params string[] permissions)
    {
        Services.RemoveAll<IHttpContextAccessor>();
        Services.RemoveAll<CurrentUserAuthorizationState>();
        Services.AddSingleton<IHttpContextAccessor>(new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    permissions
                        .Select(permission => new Claim(
                            CurrentUserAuthorizationState.PermissionClaimType,
                            permission))
                        .ToArray(),
                    "TestAuthentication")),
            },
        });
        Services.AddScoped<CurrentUserAuthorizationState>();
    }

    private static Task<HttpResponseMessage> Json(HttpStatusCode statusCode, string json)
    {
        return Task.FromResult(new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
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
