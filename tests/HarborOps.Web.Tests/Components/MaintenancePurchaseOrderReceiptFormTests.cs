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

public sealed class MaintenancePurchaseOrderReceiptFormTests : BunitContext
{
    private const string SupplierId = "a0000000-0000-0000-0000-00000000000a";
    private const string OrderId = "b0000000-0000-0000-0000-00000000000b";
    private const string FilterItemId = "c0000000-0000-0000-0000-00000000000c";
    private const string ImpellerItemId = "e0000000-0000-0000-0000-00000000000e";
    private const string FilterLineId = "f0000000-0000-0000-0000-00000000000f";
    private const string ImpellerLineId = "f1000000-0000-0000-0000-00000000001f";
    private const string ConcurrencyToken = "a3000000-0000-0000-0000-00000000003a";

    private const string OrderPath = $"/api/maintenance/purchase-orders/{OrderId}";

    private readonly List<(HttpMethod Method, Uri Uri, string? Body)> requests = [];
    private readonly List<string> postedMessages = [];
    private int closedCount;

    private Func<HttpRequestMessage, Task<HttpResponseMessage>> respond = Default;

    private Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>? respondWithToken;

    [Fact]
    public void Every_line_is_listed_and_only_outstanding_lines_take_a_quantity()
    {
        var form = RenderForm();

        var rows = form.FindAll(".receipt-lines-table tbody tr");

        Assert.Equal(2, rows.Count);
        Assert.NotNull(rows[0].QuerySelector("input[type=number]"));
        Assert.Null(rows[1].QuerySelector("input[type=number]"));
        Assert.Contains("Fulfilled", rows[1].TextContent, StringComparison.Ordinal);
        Assert.Contains("4 unit(s) outstanding", form.Find(".receipt-form-heading").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void Posting_waits_until_the_receipt_was_checked()
    {
        var form = RenderForm();

        EnterQuantity(form, "3");

        Assert.True(form.Find(".stocktake-post").HasAttribute("disabled"));
        Assert.DoesNotContain(requests, request => request.Method == HttpMethod.Post);
    }

    [Fact]
    public void Checking_sends_the_receipt_time_and_only_the_entered_lines_then_shows_the_result()
    {
        var form = RenderForm();

        form.Find("#receipt-received-utc").Change("2026-09-01T06:30:00");
        EnterQuantity(form, "3");
        Check(form);

        form.WaitForAssertion(() =>
        {
            var (_, uri, body) = requests.Last(request => request.Method == HttpMethod.Post);

            Assert.Equal($"{OrderPath}/receipts/preview", uri.AbsolutePath);
            Assert.Contains("\"receivedUtc\":\"2026-09-01T06:30:00Z\"", body, StringComparison.Ordinal);
            Assert.Contains($"\"purchaseOrderLineId\":\"{FilterLineId}\"", body, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("\"quantity\":3", body, StringComparison.Ordinal);
            Assert.DoesNotContain(ImpellerLineId, body, StringComparison.OrdinalIgnoreCase);

            var preview = form.Find(".receipt-preview-table tbody tr");

            Assert.Contains("3 pcs", preview.TextContent, StringComparison.Ordinal);
            Assert.Contains("13", preview.TextContent, StringComparison.Ordinal);
            Assert.Contains("Partially received", form.Find(".receipt-form .procurement-hint").TextContent, StringComparison.Ordinal);
            Assert.False(form.Find(".stocktake-post").HasAttribute("disabled"));
        });
    }

    [Fact]
    public void Blockers_are_listed_and_keep_posting_disabled()
    {
        respond = request => request.RequestUri!.AbsolutePath == $"{OrderPath}/receipts/preview"
            ? Json(
                HttpStatusCode.OK,
                Preview(
                    canPost: false,
                    blockers: $$"""
                        {"purchaseOrderLineId":"{{FilterLineId}}","code":"ExceedsOutstanding",
                        "message":"Receiving 5 of 'PN-1' exceeds the outstanding quantity 4."}
                        """))
            : Default(request);

        var form = RenderForm();

        EnterQuantity(form, "5");
        Check(form);

        form.WaitForAssertion(() =>
        {
            Assert.Contains("Above outstanding", form.Find(".stocktake-blockers").TextContent, StringComparison.Ordinal);
            Assert.Contains("exceeds the outstanding quantity 4", form.Find(".stocktake-blockers").TextContent, StringComparison.Ordinal);
            Assert.Contains("stocktake-line-stale", form.Find(".receipt-lines-table tbody tr").ClassName, StringComparison.Ordinal);
            Assert.True(form.Find(".stocktake-post").HasAttribute("disabled"));
        });
    }

    [Fact]
    public void Posting_is_confirmed_first_and_then_sends_the_checked_receipt()
    {
        var form = RenderForm();

        EnterQuantity(form, "3");
        form.Find("#receipt-delivery-reference").Change("DN-4471");
        Check(form);

        form.Find(".stocktake-post").Click();

        Assert.Contains(
            "Stock on hand increases by 3 unit(s) across 1 part, and the order becomes partially received.",
            Regex.Replace(form.Find(".procurement-confirm").TextContent, @"\s+", " "),
            StringComparison.Ordinal);
        Assert.DoesNotContain(requests, request => request.Uri.AbsolutePath == $"{OrderPath}/receipts");

        form.Find(".procurement-confirm .maintenance-button").Click();

        form.WaitForAssertion(() =>
        {
            var (_, _, body) = requests.Single(request => request.Uri.AbsolutePath == $"{OrderPath}/receipts");

            Assert.Contains("\"clientRequestId\":\"", body, StringComparison.Ordinal);
            Assert.Contains($"\"expectedConcurrencyToken\":\"{ConcurrencyToken}\"", body, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("\"previewToken\":\"PREVIEW-1\"", body, StringComparison.Ordinal);
            Assert.Contains("\"deliveryReference\":\"DN-4471\"", body, StringComparison.Ordinal);
            Assert.Contains($"\"purchaseOrderLineId\":\"{FilterLineId}\"", body, StringComparison.OrdinalIgnoreCase);

            Assert.Equal(["Receipt posted. 3 unit(s) received. The order is now partially received."], postedMessages);
        });
    }

    [Fact]
    public void Backing_out_of_the_confirmation_sends_nothing()
    {
        var form = RenderForm();

        EnterQuantity(form, "3");
        Check(form);

        form.Find(".stocktake-post").Click();
        form.Find(".procurement-confirm .maintenance-reset").Click();

        Assert.Empty(form.FindAll(".procurement-confirm"));
        Assert.DoesNotContain(requests, request => request.Uri.AbsolutePath == $"{OrderPath}/receipts");
    }

    [Fact]
    public void A_replayed_delivery_is_reported_as_already_posted()
    {
        respond = request => request.RequestUri!.AbsolutePath == $"{OrderPath}/receipts" && request.Method == HttpMethod.Post
            ? Json(HttpStatusCode.OK, Posted(alreadyPosted: true))
            : Default(request);

        var form = RenderForm();

        EnterQuantity(form, "3");
        Check(form);
        Post(form);

        form.WaitForAssertion(() => Assert.Equal(
            ["This delivery had already been posted. Stock was not changed again."],
            postedMessages));
    }

    [Fact]
    public void Changing_the_receipt_after_the_check_requires_a_new_check()
    {
        var form = RenderForm();

        EnterQuantity(form, "3");
        Check(form);

        form.Find("#receipt-notes").Change("Second pallet");

        Assert.True(form.Find(".stocktake-post").HasAttribute("disabled"));
        Assert.Contains(
            "changed since it was checked",
            form.Find(".stocktake-hint strong").TextContent,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Validation_errors_are_shown_next_to_the_part_they_belong_to()
    {
        respond = request => request.RequestUri!.AbsolutePath == $"{OrderPath}/receipts/preview"
            ? Json(
                HttpStatusCode.BadRequest,
                """
                {"title":"One or more validation errors occurred.",
                "errors":{"lines[0].quantity":["Received quantity supports at most 3 decimal places."],
                "deliveryReference":["Delivery reference must not exceed 100 characters."]}}
                """)
            : Default(request);

        var form = RenderForm();

        EnterQuantity(form, "1.0005");
        Check(form);

        form.WaitForAssertion(() =>
        {
            var errors = form.Find(".stocktake-validation").TextContent;

            Assert.Contains("PN-1", errors, StringComparison.Ordinal);
            Assert.Contains("at most 3 decimal places", errors, StringComparison.Ordinal);
            Assert.Contains("Delivery reference", errors, StringComparison.Ordinal);
            Assert.Contains(
                "at most 3 decimal places",
                form.Find(".receipt-lines-table .maintenance-inline-error").TextContent,
                StringComparison.Ordinal);
        });
    }

    [Fact]
    public void A_conflict_on_posting_reloads_the_order_and_keeps_what_was_typed()
    {
        respond = request => request.RequestUri!.AbsolutePath == $"{OrderPath}/receipts" && request.Method == HttpMethod.Post
            ? Json(
                HttpStatusCode.Conflict,
                """
                {"title":"Purchase order receipt preview is out of date.",
                "detail":"Purchase order or its inventory changed after the receipt preview."}
                """)
            : Default(request);

        var form = RenderForm();

        EnterQuantity(form, "3");
        Check(form);

        var orderLoadsBefore = requests.Count(request => request.Uri.AbsolutePath == OrderPath);

        Post(form);

        form.WaitForAssertion(() =>
        {
            Assert.Contains("preview is out of date", form.Find(".maintenance-conflict").TextContent, StringComparison.Ordinal);
            Assert.True(requests.Count(request => request.Uri.AbsolutePath == OrderPath) > orderLoadsBefore);
            Assert.Equal("3", form.Find(".receipt-lines-table input[type=number]").GetAttribute("value"));
            Assert.True(form.Find(".stocktake-post").HasAttribute("disabled"));
            Assert.Empty(postedMessages);
        });
    }

    [Fact]
    public void A_lost_response_keeps_the_check_so_the_same_request_can_be_sent_again()
    {
        var attempts = 0;
        respond = request =>
        {
            if (request.RequestUri!.AbsolutePath == $"{OrderPath}/receipts" && request.Method == HttpMethod.Post
                && ++attempts == 1)
            {
                throw new HttpRequestException("The connection was reset.");
            }

            return Default(request);
        };

        var form = RenderForm();

        EnterQuantity(form, "3");
        Check(form);
        Post(form);

        form.WaitForAssertion(() => Assert.Contains(
            "Posting again is safe",
            form.Find(".receipt-form .maintenance-inline-error").TextContent,
            StringComparison.Ordinal));

        Assert.False(form.Find(".stocktake-post").HasAttribute("disabled"));

        Post(form);

        form.WaitForAssertion(() =>
        {
            var bodies = requests
                .Where(request => request.Uri.AbsolutePath == $"{OrderPath}/receipts")
                .Select(request => ClientRequestId(request.Body!))
                .ToList();

            Assert.Equal(2, bodies.Count);
            Assert.Equal(bodies[0], bodies[1]);
            Assert.Single(postedMessages);
        });
    }

    [Fact]
    public void A_new_check_uses_a_new_client_request_id()
    {
        var form = RenderForm();

        EnterQuantity(form, "3");
        Check(form);
        Post(form);
        form.WaitForAssertion(() => Assert.Single(postedMessages));

        EnterQuantity(form, "1");
        Check(form);
        Post(form);

        form.WaitForAssertion(() =>
        {
            var ids = requests
                .Where(request => request.Uri.AbsolutePath == $"{OrderPath}/receipts")
                .Select(request => ClientRequestId(request.Body!))
                .ToList();

            Assert.Equal(2, ids.Count);
            Assert.NotEqual(ids[0], ids[1]);
        });
    }

    [Fact]
    public void A_receipt_time_in_the_future_is_rejected_without_calling_the_api()
    {
        var form = RenderForm();

        form.Find("#receipt-received-utc").Change(DateTime.UtcNow.AddDays(2).ToString("yyyy-MM-ddTHH:mm:ss", System.Globalization.CultureInfo.InvariantCulture));
        EnterQuantity(form, "3");
        form.Find(".receipt-form .maintenance-button").Click();

        Assert.Contains("cannot be received in the future", form.Find(".stocktake-validation").TextContent, StringComparison.Ordinal);
        Assert.DoesNotContain(requests, request => request.Uri.AbsolutePath.Contains("preview", StringComparison.Ordinal));
    }

    [Fact]
    public void Receiving_the_rest_checks_the_outstanding_quantities_and_posts_to_the_receive_action()
    {
        var form = RenderForm(receiveRemaining: true);

        Assert.Equal("Receive remaining quantities", form.Find("#receipt-form-title").TextContent.Trim());
        Assert.Single(form.FindAll(".receipt-lines-table tbody tr"));
        Assert.Empty(form.FindAll(".receipt-lines-table input"));

        Check(form);

        form.WaitForAssertion(() => Assert.Contains(
            requests,
            request => request.Method == HttpMethod.Get && request.Uri.AbsolutePath == $"{OrderPath}/receive/preview"));

        Post(form);

        form.WaitForAssertion(() =>
        {
            var (_, _, body) = requests.Single(request => request.Uri.AbsolutePath == $"{OrderPath}/receive");

            Assert.Contains("\"previewToken\":\"PREVIEW-1\"", body, StringComparison.Ordinal);
            Assert.DoesNotContain("\"lines\"", body, StringComparison.Ordinal);
            Assert.Equal(["Receipt posted. 3 unit(s) received. The order is now received."], postedMessages);
        });
    }

    [Fact]
    public void A_failed_load_offers_a_retry_and_a_way_back()
    {
        respond = request => request.RequestUri!.AbsolutePath == OrderPath
            ? Json(HttpStatusCode.NotFound, """{"title":"Purchase order not found.","detail":"It may have been removed."}""")
            : Default(request);

        var form = RenderFormWithoutWaiting();

        form.WaitForAssertion(() => Assert.Contains(
            "Purchase order not found.",
            form.Find(".api-error-state").TextContent,
            StringComparison.Ordinal));

        form.Find(".receipt-form .maintenance-reset").Click();

        Assert.Equal(1, closedCount);
    }

    [Fact]
    public void Back_to_order_leaves_without_posting()
    {
        var form = RenderForm();

        EnterQuantity(form, "3");
        form.FindAll(".receipt-form .maintenance-reset").Last().Click();

        Assert.Equal(1, closedCount);
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

    private static void EnterQuantity(IRenderedComponent<PurchaseOrderReceiptForm> form, string quantity)
    {
        form.Find(".receipt-lines-table input[type=number]").Change(quantity);
    }

    private static void Check(IRenderedComponent<PurchaseOrderReceiptForm> form)
    {
        form.Find(".receipt-form .maintenance-button").Click();
        form.WaitForAssertion(() => Assert.NotEmpty(form.FindAll(".receipt-preview-table, .stocktake-blockers, .stocktake-validation")));
    }

    private static void Post(IRenderedComponent<PurchaseOrderReceiptForm> form)
    {
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

    private IRenderedComponent<PurchaseOrderReceiptForm> RenderForm(bool receiveRemaining = false)
    {
        var form = RenderFormWithoutWaiting(receiveRemaining);

        form.WaitForAssertion(() => Assert.NotEmpty(form.FindAll(".receipt-lines-table")));

        return form;
    }

    private IRenderedComponent<PurchaseOrderReceiptForm> RenderFormWithoutWaiting(bool receiveRemaining = false)
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

        return Render<PurchaseOrderReceiptForm>(parameters => parameters
            .Add(component => component.PurchaseOrderId, Guid.Parse(OrderId))
            .Add(component => component.ReceiveRemaining, receiveRemaining)
            .Add(component => component.OnPosted, (string message) => postedMessages.Add(message))
            .Add(component => component.OnClosed, () => closedCount++));
    }

    private static Task<HttpResponseMessage> Default(HttpRequestMessage request)
    {
        var path = request.RequestUri!.AbsolutePath;

        return path switch
        {
            OrderPath => Json(HttpStatusCode.OK, OrderDetails()),
            $"{OrderPath}/receipts/preview" => Json(HttpStatusCode.OK, Preview()),
            $"{OrderPath}/receive/preview" => Json(HttpStatusCode.OK, Preview(resultingStatus: "Received")),
            $"{OrderPath}/receipts" => Json(HttpStatusCode.OK, Posted(alreadyPosted: false)),
            $"{OrderPath}/receive" => Json(HttpStatusCode.OK, Received()),
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

    // The fuel filter still has 4 outstanding; the impeller was fully received earlier.
    private static string OrderDetails()
    {
        return $$"""
            {"purchaseOrderId":"{{OrderId}}","supplierId":"{{SupplierId}}",
            "supplierName":"Baltic Marine Parts","supplierContactPerson":null,"supplierPhoneEmail":null,
            "orderDate":"2026-08-20","expectedDelivery":"2026-09-01","status":"PartiallyReceived","isOverdue":false,
            "daysUntilDelivery":6,"totalCost":302.00,"allowedTransitions":["Received"],
            "lines":[
            {"lineId":"{{FilterLineId}}","inventoryItemId":"{{FilterItemId}}","inventoryItemName":"Fuel filter",
            "partNumber":"PN-1","quantityOrdered":4,"unitCost":25.50,"lineTotal":102.00,"receivedQuantity":0,
            "legacyReceivedQuantity":0,"returnedQuantity":0,"outstandingQuantity":4,"returnableQuantity":0},
            {"lineId":"{{ImpellerLineId}}","inventoryItemId":"{{ImpellerItemId}}","inventoryItemName":"Impeller",
            "partNumber":"PN-2","quantityOrdered":2,"unitCost":100.00,"lineTotal":200.00,"receivedQuantity":2,
            "legacyReceivedQuantity":0,"returnedQuantity":0,"outstandingQuantity":0,"returnableQuantity":2}],
            "concurrencyToken":"{{ConcurrencyToken}}","totalQuantityOrdered":6,"totalQuantityReceived":2,
            "totalQuantityReturned":0,"totalQuantityOutstanding":4,"receiptCount":1,
            "allowedActions":["ReceivePartial","ReceiveRemaining","CreateReturn"]}
            """;
    }

    private static string Preview(
        bool canPost = true,
        string resultingStatus = "PartiallyReceived",
        string blockers = "")
    {
        return $$"""
            {"purchaseOrderId":"{{OrderId}}","orderStatus":"PartiallyReceived","concurrencyToken":"{{ConcurrencyToken}}",
            "previewToken":"PREVIEW-1","canPost":{{(canPost ? "true" : "false")}},"resultingOrderStatus":"{{resultingStatus}}",
            "lines":[{"purchaseOrderLineId":"{{FilterLineId}}","inventoryItemId":"{{FilterItemId}}","name":"Fuel filter",
            "partNumber":"PN-1","unitAbbreviation":"pcs","quantityOrdered":4,"receivedQuantity":0,
            "outstandingQuantity":4,"quantityToReceive":3,"outstandingQuantityAfter":1,"onHandQuantity":10,
            "onHandQuantityAfter":13}],
            "blockers":[{{blockers}}],"generatedUtc":"2026-09-01T08:00:00Z"}
            """;
    }

    private static string Receipt(string statusAfter)
    {
        return $$"""
            {"receiptId":"d0000000-0000-0000-0000-00000000000d","purchaseOrderId":"{{OrderId}}",
            "clientRequestId":"a1000000-0000-0000-0000-00000000001a","receivedUtc":"2026-09-01T06:00:00Z",
            "deliveryReference":null,"notes":null,"recordedByUserId":"a2000000-0000-0000-0000-00000000002a",
            "recordedUtc":"2026-09-01T08:00:00Z","orderStatusBefore":"PartiallyReceived",
            "orderStatusAfter":"{{statusAfter}}","totalQuantity":3,"lines":[],"movements":[]}
            """;
    }

    private static string Posted(bool alreadyPosted)
    {
        return $$"""
            {"receipt":{{Receipt("PartiallyReceived")}},"alreadyPosted":{{(alreadyPosted ? "true" : "false")}},
            "orderStatus":"PartiallyReceived","concurrencyToken":"a5000000-0000-0000-0000-00000000005a",
            "allowedActions":["ReceivePartial","ReceiveRemaining","CreateReturn"]}
            """;
    }

    private static string Received()
    {
        return $$"""
            {"purchaseOrderId":"{{OrderId}}","previousStatus":"PartiallyReceived","status":"Received",
            "allowedTransitions":[],"stockAdjustments":[],"receipt":{{Receipt("Received")}},"alreadyPosted":false}
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
