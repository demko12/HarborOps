using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.RegularExpressions;
using Bunit;
using HarborOps.Web.Api;
using HarborOps.Web.Api.Maintenance;
using HarborOps.Web.Components.Pages.Maintenance;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace HarborOps.Web.Tests.Components;

public sealed class MaintenancePurchaseOrderClosureFormTests : BunitContext
{
    private const string OrderId = "b0000000-0000-0000-0000-00000000000b";
    private const string FilterItemId = "c0000000-0000-0000-0000-00000000000c";
    private const string ImpellerItemId = "e0000000-0000-0000-0000-00000000000e";
    private const string FilterLineId = "f0000000-0000-0000-0000-00000000000f";
    private const string ImpellerLineId = "f1000000-0000-0000-0000-00000000001f";
    private const string ConcurrencyToken = "a3000000-0000-0000-0000-00000000003a";
    private const string RefreshedToken = "a4000000-0000-0000-0000-00000000004a";
    private const string Reason = "Supplier discontinued the part";

    private const string OrderPath = $"/api/maintenance/purchase-orders/{OrderId}";
    private const string PreviewPath = $"{OrderPath}/close/preview";
    private const string ClosePath = $"{OrderPath}/close";

    private readonly List<(HttpMethod Method, Uri Uri, string? Body)> requests = [];
    private readonly List<string> closedMessages = [];
    private int dismissedCount;

    private Func<HttpRequestMessage, Task<HttpResponseMessage>> respond = Default;

    private Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>? respondWithToken;

    [Fact]
    public void The_form_shows_received_and_undelivered_amounts_per_line()
    {
        var form = RenderForm();

        var totals = form.Find(".closure-totals").TextContent;
        var rows = form.FindAll(".closure-lines-table tbody tr");

        Assert.Contains("6 unit(s) stay in stock", totals, StringComparison.Ordinal);
        Assert.Contains("6 unit(s) will be closed without delivery", totals, StringComparison.Ordinal);
        Assert.Equal(2, rows.Count);
        Assert.Contains("6 pcs", rows[0].TextContent, StringComparison.Ordinal);
        Assert.Contains("Fully received", rows[1].TextContent, StringComparison.Ordinal);
        Assert.Contains(requests, request => request.Method == HttpMethod.Get && request.Uri.AbsolutePath == PreviewPath);
    }

    [Fact]
    public void Previously_returned_quantities_are_shown_next_to_what_was_received()
    {
        var form = RenderForm();

        var cells = form.FindAll(".closure-lines-table tbody tr")[0].QuerySelectorAll("td.numeric");

        Assert.Equal(["10", "4", "1", "6 pcs"], cells.Select(cell => cell.TextContent.Trim()));
    }

    [Fact]
    public void Blockers_are_listed_and_closing_stays_disabled()
    {
        respond = request => request.RequestUri!.AbsolutePath == PreviewPath
            ? Json(
                HttpStatusCode.OK,
                Preview(
                    canClose: false,
                    blockers: """
                        {"purchaseOrderLineId":null,"code":"OrderNotClosable",
                        "message":"Purchase order is Ordered with nothing received; cancel it instead of closing it short."}
                        """))
            : Default(request);

        var form = RenderForm();

        var blockers = form.Find(".stocktake-blockers").TextContent;

        Assert.Contains("Order not closable", blockers, StringComparison.Ordinal);
        Assert.Contains("cancel it instead", blockers, StringComparison.Ordinal);
        Assert.Empty(form.FindAll("#closure-reason"));
        Assert.True(form.Find(".stocktake-post").HasAttribute("disabled"));
    }

    [Fact]
    public void The_reason_length_is_counted_while_typing()
    {
        var form = RenderForm();

        form.Find("#closure-reason").Input("  Late  ");

        Assert.Contains("4 / 500 characters", form.Find("#closure-reason-hint").TextContent, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("", "Explain why the remaining goods will not arrive.")]
    [InlineData("   Too late  ", "at least 10 characters")]
    public void A_missing_or_short_reason_is_rejected_without_calling_the_api(string reason, string expected)
    {
        var form = RenderForm();

        form.Find("#closure-reason").Input(reason);
        form.Find(".stocktake-post").Click();

        Assert.Contains(expected, form.Find(".stocktake-validation").TextContent, StringComparison.Ordinal);
        Assert.Empty(form.FindAll(".procurement-confirm"));
        Assert.DoesNotContain(requests, request => request.Method == HttpMethod.Post);
    }

    [Fact]
    public void Closing_is_confirmed_first_and_then_sends_the_previewed_tokens_and_the_trimmed_reason()
    {
        var form = RenderForm();

        form.Find("#closure-reason").Input($"  {Reason}  ");
        form.Find(".stocktake-post").Click();

        Assert.Contains(
            "6 undelivered unit(s) are recorded as closed without delivery. The 6 received unit(s), their receipts, and stock on hand do not change",
            Regex.Replace(form.Find(".procurement-confirm").TextContent, @"\s+", " "),
            StringComparison.Ordinal);
        Assert.DoesNotContain(requests, request => request.Method == HttpMethod.Post);

        form.Find(".procurement-confirm .maintenance-button").Click();

        form.WaitForAssertion(() =>
        {
            var (_, _, body) = requests.Single(request => request.Uri.AbsolutePath == ClosePath);

            Assert.Contains("\"clientRequestId\":\"", body, StringComparison.Ordinal);
            Assert.Contains($"\"expectedConcurrencyToken\":\"{ConcurrencyToken}\"", body, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("\"previewToken\":\"PREVIEW-1\"", body, StringComparison.Ordinal);
            Assert.Contains($"\"reason\":\"{Reason}\"", body, StringComparison.Ordinal);
            Assert.Equal(
                ["Order closed short. 6 unit(s) received, 6 unit(s) closed without delivery."],
                closedMessages);
        });
    }

    [Fact]
    public void Backing_out_of_the_confirmation_sends_nothing()
    {
        var form = RenderForm();

        form.Find("#closure-reason").Input(Reason);
        form.Find(".stocktake-post").Click();
        form.Find(".procurement-confirm .maintenance-reset").Click();

        Assert.Empty(form.FindAll(".procurement-confirm"));
        Assert.DoesNotContain(requests, request => request.Method == HttpMethod.Post);
    }

    [Fact]
    public void A_replayed_close_is_reported_as_already_closed()
    {
        respond = request => request.RequestUri!.AbsolutePath == ClosePath
            ? Json(HttpStatusCode.OK, Closed(alreadyClosed: true))
            : Default(request);

        var form = RenderForm();

        Close(form);

        form.WaitForAssertion(() => Assert.Equal(
            ["This order had already been closed by the same request. Nothing changed again."],
            closedMessages));
    }

    [Fact]
    public void A_stale_close_shows_the_refreshed_amounts_and_keeps_the_reason()
    {
        respond = request => request.RequestUri!.AbsolutePath == ClosePath
            ? Json(
                HttpStatusCode.Conflict,
                $$"""
                {"title":"Purchase order was modified concurrently.",
                "detail":"Purchase order was changed by another request.",
                "preview":{{Preview(filterReceived: 5m, token: RefreshedToken, previewToken: "PREVIEW-2")}}}
                """)
            : Default(request);

        var form = RenderForm();

        Close(form);

        form.WaitForAssertion(() =>
        {
            Assert.Contains("modified concurrently", form.Find(".maintenance-conflict").TextContent, StringComparison.Ordinal);
            Assert.Contains("5 unit(s) will be closed without delivery", form.Find(".closure-totals").TextContent, StringComparison.Ordinal);
            Assert.Equal(Reason, form.Find("#closure-reason").GetAttribute("value"));
            Assert.Empty(closedMessages);
        });

        // The refreshed preview came with the conflict, so nothing had to be reloaded.
        Assert.Single(requests, request => request.Uri.AbsolutePath == PreviewPath);

        respond = Default;
        Close(form, typeReason: false);

        form.WaitForAssertion(() =>
        {
            var bodies = requests.Where(request => request.Uri.AbsolutePath == ClosePath).Select(request => request.Body!).ToList();

            Assert.Equal(2, bodies.Count);
            Assert.Contains($"\"expectedConcurrencyToken\":\"{RefreshedToken}\"", bodies[1], StringComparison.OrdinalIgnoreCase);
            Assert.Contains("\"previewToken\":\"PREVIEW-2\"", bodies[1], StringComparison.Ordinal);
            Assert.NotEqual(ClientRequestId(bodies[0]), ClientRequestId(bodies[1]));
            Assert.Single(closedMessages);
        });
    }

    [Fact]
    public void A_conflict_without_a_refreshed_preview_reloads_the_amounts()
    {
        respond = request => request.RequestUri!.AbsolutePath == ClosePath
            ? Json(
                HttpStatusCode.Conflict,
                """{"title":"Purchase order cannot be closed.","detail":"Purchase order is ClosedShort."}""")
            : Default(request);

        var form = RenderForm();

        Close(form);

        form.WaitForAssertion(() =>
        {
            Assert.Contains("cannot be closed", form.Find(".maintenance-conflict").TextContent, StringComparison.Ordinal);
            Assert.Equal(2, requests.Count(request => request.Uri.AbsolutePath == PreviewPath));
        });
    }

    [Fact]
    public void Validation_errors_from_the_api_are_listed_by_field()
    {
        respond = request => request.RequestUri!.AbsolutePath == ClosePath
            ? Json(
                HttpStatusCode.BadRequest,
                """
                {"title":"One or more validation errors occurred.",
                "errors":{"reason":["Reason must be at least 10 characters and explain why the remaining goods will not arrive."]}}
                """)
            : Default(request);

        var form = RenderForm();

        Close(form);

        form.WaitForAssertion(() =>
        {
            var errors = form.Find(".stocktake-validation").TextContent;

            Assert.Contains("Reason", errors, StringComparison.Ordinal);
            Assert.Contains("at least 10 characters", errors, StringComparison.Ordinal);
            Assert.Empty(closedMessages);
        });
    }

    [Fact]
    public void A_lost_response_keeps_the_preview_so_the_same_request_can_be_sent_again()
    {
        var attempts = 0;
        respond = request =>
        {
            if (request.RequestUri!.AbsolutePath == ClosePath && ++attempts == 1)
            {
                throw new HttpRequestException("The connection was reset.");
            }

            return Default(request);
        };

        var form = RenderForm();

        Close(form);

        form.WaitForAssertion(() => Assert.Contains(
            "Closing again is safe",
            form.Find(".closure-form .maintenance-inline-error").TextContent,
            StringComparison.Ordinal));

        Close(form, typeReason: false);

        form.WaitForAssertion(() =>
        {
            var ids = requests
                .Where(request => request.Uri.AbsolutePath == ClosePath)
                .Select(request => ClientRequestId(request.Body!))
                .ToList();

            Assert.Equal(2, ids.Count);
            Assert.Equal(ids[0], ids[1]);
            Assert.Single(closedMessages);
        });
    }

    [Fact]
    public void Refreshing_reloads_the_amounts_and_clears_an_earlier_failure()
    {
        respond = request => request.RequestUri!.AbsolutePath == ClosePath
            ? Json(HttpStatusCode.InternalServerError, """{"title":"Server error.","detail":"Something failed."}""")
            : Default(request);

        var form = RenderForm();

        Close(form);
        form.WaitForAssertion(() => Assert.NotEmpty(form.FindAll(".closure-form .maintenance-inline-error")));

        form.FindAll(".stocktake-actions .maintenance-button")[1].Click();

        form.WaitForAssertion(() =>
        {
            Assert.Empty(form.FindAll(".closure-form .maintenance-inline-error"));
            Assert.Equal(2, requests.Count(request => request.Uri.AbsolutePath == PreviewPath));
        });
    }

    [Fact]
    public void A_failed_load_offers_a_retry_and_a_way_back()
    {
        respond = request => request.RequestUri!.AbsolutePath == PreviewPath
            ? Json(HttpStatusCode.NotFound, """{"title":"Purchase order not found.","detail":"It may have been removed."}""")
            : Default(request);

        var form = RenderFormWithoutWaiting();

        form.WaitForAssertion(() => Assert.Contains(
            "Purchase order not found.",
            form.Find(".api-error-state").TextContent,
            StringComparison.Ordinal));

        form.Find(".closure-form .maintenance-reset").Click();

        Assert.Equal(1, dismissedCount);
    }

    [Fact]
    public void Back_to_order_leaves_without_closing()
    {
        var form = RenderForm();

        form.Find("#closure-reason").Input(Reason);
        form.Find(".stocktake-actions .maintenance-reset").Click();

        Assert.Equal(1, dismissedCount);
        Assert.DoesNotContain(requests, request => request.Method == HttpMethod.Post);
    }

    [Fact]
    public async Task Disposing_the_form_cancels_the_in_flight_request()
    {
        var started = new TaskCompletionSource();
        CancellationToken observed = default;

        respondWithToken = (_, token) =>
        {
            observed = token;
            started.TrySetResult();

            return new TaskCompletionSource<HttpResponseMessage>().Task;
        };

        RenderFormWithoutWaiting();

        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await DisposeComponentsAsync();

        Assert.True(observed.IsCancellationRequested);
    }

    private static void Close(IRenderedComponent<PurchaseOrderClosureForm> form, bool typeReason = true)
    {
        if (typeReason)
        {
            form.Find("#closure-reason").Input(Reason);
        }

        form.WaitForAssertion(() => Assert.False(form.Find(".stocktake-post").HasAttribute("disabled")));
        form.Find(".stocktake-post").Click();
        form.Find(".procurement-confirm .maintenance-button").Click();
    }

    private static string ClientRequestId(string body)
    {
        const string Marker = "\"clientRequestId\":\"";
        var start = body.IndexOf(Marker, StringComparison.Ordinal) + Marker.Length;

        return body[start..body.IndexOf('"', start)];
    }

    private IRenderedComponent<PurchaseOrderClosureForm> RenderForm()
    {
        var form = RenderFormWithoutWaiting();

        form.WaitForAssertion(() => Assert.NotEmpty(form.FindAll(".closure-lines-table")));

        return form;
    }

    private IRenderedComponent<PurchaseOrderClosureForm> RenderFormWithoutWaiting()
    {
        var httpClient = new HttpClient(new StubHttpMessageHandler(async (request, token) =>
        {
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(token);
            requests.Add((request.Method, request.RequestUri!, body));

            return respondWithToken is not null
                ? await respondWithToken(request, token)
                : await respond(request);
        }))
        {
            BaseAddress = new Uri("https://api.harborops.test/"),
        };

        Services.AddSingleton(new MaintenanceApiClient(httpClient));
        Services.AddSingleton(CreateAuthorizationState());

        return Render<PurchaseOrderClosureForm>(parameters => parameters
            .Add(component => component.PurchaseOrderId, Guid.Parse(OrderId))
            .Add(component => component.OnOrderClosed, (string message) => closedMessages.Add(message))
            .Add(component => component.OnDismissed, () => dismissedCount++));
    }

    private static Task<HttpResponseMessage> Default(HttpRequestMessage request)
    {
        return request.RequestUri!.AbsolutePath switch
        {
            PreviewPath => Json(HttpStatusCode.OK, Preview()),
            ClosePath => Json(HttpStatusCode.OK, Closed(alreadyClosed: false)),
            _ => Json(HttpStatusCode.NotFound, """{"title":"Not found."}"""),
        };
    }

    private static CurrentUserAuthorizationState CreateAuthorizationState()
    {
        var identity = new ClaimsIdentity("test");
        identity.AddClaim(new Claim(CurrentUserAuthorizationState.PermissionClaimType, "maintenance.manage"));

        var context = new DefaultHttpContext { User = new ClaimsPrincipal(identity) };

        return new CurrentUserAuthorizationState(new HttpContextAccessor { HttpContext = context });
    }

    // The fuel filter received 4 of 10 and had 1 returned, leaving 6 to close; the impeller was fully received.
    private static string Preview(
        bool canClose = true,
        decimal filterReceived = 4m,
        string token = ConcurrencyToken,
        string previewToken = "PREVIEW-1",
        string blockers = "")
    {
        return $$"""
            {"purchaseOrderId":"{{OrderId}}","orderStatus":"PartiallyReceived","concurrencyToken":"{{token}}",
            "previewToken":"{{previewToken}}","canClose":{{(canClose ? "true" : "false")}},
            "resultingOrderStatus":"{{(canClose ? "ClosedShort" : "PartiallyReceived")}}",
            "totalQuantityReceived":{{filterReceived + 2m}},"totalQuantityToClose":{{10m - filterReceived}},
            "lines":[
            {"purchaseOrderLineId":"{{FilterLineId}}","inventoryItemId":"{{FilterItemId}}","name":"Fuel filter",
            "partNumber":"PN-1","unitAbbreviation":"pcs","quantityOrdered":10,"receivedQuantity":{{filterReceived}},
            "returnedQuantity":1,"quantityToClose":{{10m - filterReceived}}},
            {"purchaseOrderLineId":"{{ImpellerLineId}}","inventoryItemId":"{{ImpellerItemId}}","name":"Impeller",
            "partNumber":"PN-2","unitAbbreviation":"pcs","quantityOrdered":2,"receivedQuantity":2,
            "returnedQuantity":0,"quantityToClose":0}],
            "blockers":[{{blockers}}],"generatedUtc":"2026-10-01T08:00:00Z"}
            """;
    }

    private static string Closed(bool alreadyClosed)
    {
        return $$"""
            {"closure":{"closureId":"d0000000-0000-0000-0000-00000000000d","purchaseOrderId":"{{OrderId}}",
            "clientRequestId":"a1000000-0000-0000-0000-00000000001a","reason":"{{Reason}}",
            "closedByUserId":"a2000000-0000-0000-0000-00000000002a","closedUtc":"2026-10-01T08:00:00Z",
            "orderStatusBefore":"PartiallyReceived","orderStatusAfter":"ClosedShort",
            "totalQuantityReceived":6,"totalQuantityClosed":6,"lines":[]},
            "alreadyClosed":{{(alreadyClosed ? "true" : "false")}},"orderStatus":"ClosedShort",
            "concurrencyToken":"a5000000-0000-0000-0000-00000000005a","allowedActions":["CreateReturn"]}
            """;
    }

    private static Task<HttpResponseMessage> Json(HttpStatusCode statusCode, string json)
    {
        return Task.FromResult(new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        });
    }

    private sealed class StubHttpMessageHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responseFactory) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => responseFactory(request, cancellationToken);
    }
}
