using System.Net;
using System.Security.Claims;
using System.Text;
using Bunit;
using HarborOps.Web.Api;
using HarborOps.Web.Api.Maintenance;
using HarborOps.Web.Components.Pages.Maintenance;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace HarborOps.Web.Tests.Components;

public sealed class MaintenanceSupplierReturnEditorTests : BunitContext
{
    private const string ReturnId = "a0000000-0000-0000-0000-00000000000a";
    private const string OrderId = "b0000000-0000-0000-0000-00000000000b";
    private const string FilterLineId = "c0000000-0000-0000-0000-00000000000c";
    private const string GasketLineId = "c1000000-0000-0000-0000-00000000001c";
    private const string FilterId = "d0000000-0000-0000-0000-00000000000d";
    private const string GasketId = "d1000000-0000-0000-0000-00000000001d";
    private const string WorkOrderId = "e0000000-0000-0000-0000-00000000000e";
    private const string ConcurrencyToken = "f0000000-0000-0000-0000-00000000000f";

    private const string ReturnPath = "/api/maintenance/supplier-returns/" + ReturnId;
    private const string OrderPath = "/api/maintenance/purchase-orders/" + OrderId;

    private readonly List<HttpRequestMessage> requests = [];
    private readonly List<string> requestBodies = [];

    private string detailsJson = Details();
    private string previewJson = Preview();
    private string orderJson = Order();
    private Func<HttpRequestMessage, Task<HttpResponseMessage>>? respond;

    [Fact]
    public void Opening_a_draft_loads_the_return_the_order_and_the_posting_preview()
    {
        var editor = RenderEditor();

        Assert.Contains(ReturnPath, Paths(), StringComparer.Ordinal);
        Assert.Contains(OrderPath, Paths(), StringComparer.Ordinal);
        Assert.Contains(ReturnPath + "/preview", Paths(), StringComparer.Ordinal);
        Assert.Contains("North Sea Marine Supply", editor.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void A_draft_lists_every_order_line_with_the_allowance_of_the_returned_one()
    {
        var editor = RenderEditor();

        var rows = editor.FindAll(".supplier-return-lines-table tbody tr");

        Assert.Equal(2, rows.Count);

        var returned = rows[0].QuerySelectorAll("td");
        var notReturned = rows[1].QuerySelectorAll("td");

        Assert.Equal("FILTER-AIR-110", returned[1].TextContent.Trim());
        Assert.Contains("10", returned[2].TextContent, StringComparison.Ordinal);
        Assert.Equal("1", returned[3].TextContent.Trim());
        Assert.Equal("6.5", returned[4].TextContent.Trim());
        Assert.Equal("12", returned[5].TextContent.Trim());
        Assert.Equal("2", returned[6].TextContent.Trim());
        Assert.Equal("GASKET-22", notReturned[1].TextContent.Trim());
        Assert.Equal("—", notReturned[4].TextContent.Trim());
        Assert.Equal(string.Empty, notReturned[7].QuerySelector("input")!.GetAttribute("value") ?? string.Empty);
    }

    [Fact]
    public void The_draft_offers_saving_posting_and_cancelling()
    {
        var editor = RenderEditor();

        var actions = editor.FindAll(".stocktake-actions button");

        Assert.Equal(["Save and check", "Post return", "Cancel return"], actions.Select(action => action.TextContent.Trim()));
    }

    [Fact]
    public void A_new_return_loads_only_the_order_and_offers_creating_the_draft()
    {
        var editor = RenderNewEditor();

        Assert.Contains(OrderPath, Paths(), StringComparer.Ordinal);
        Assert.DoesNotContain(Paths(), path => path.StartsWith("/api/maintenance/supplier-returns", StringComparison.Ordinal));
        Assert.Equal(["Create draft"], editor.FindAll(".stocktake-actions button").Select(action => action.TextContent.Trim()));
        Assert.Equal("New supplier return", editor.Find("#supplier-return-editor-title").TextContent.Trim());
    }

    [Fact]
    public void A_new_return_for_an_order_that_was_not_received_is_explained()
    {
        orderJson = Order(status: "Ordered");

        var editor = RenderNewEditor(waitForLines: false);

        editor.WaitForAssertion(() =>
        {
            Assert.Contains("This purchase order cannot be returned", editor.Markup, StringComparison.Ordinal);
            Assert.Empty(editor.FindAll(".supplier-return-lines-table"));
        });
    }

    [Fact]
    public void Creating_a_draft_sends_only_the_lines_with_a_quantity_and_switches_to_the_draft()
    {
        var editor = RenderNewEditor();

        editor.FindAll(".supplier-return-lines-table tbody tr")[0].QuerySelector("input[type=number]")!.Change("2.5");
        editor.FindAll(".supplier-return-lines-table tbody tr")[0].QuerySelector("input[type=text]")!.Change("Wrong size");
        editor.Find("#supplier-return-notes").Change("RMA 1042");
        editor.Find(".stocktake-actions .maintenance-button").Click();

        editor.WaitForAssertion(() =>
        {
            var request = requests.Last(candidate => candidate.Method == HttpMethod.Post);
            var body = requestBodies.Last(candidate => candidate.Contains("purchaseOrderId", StringComparison.Ordinal));

            Assert.Equal("/api/maintenance/supplier-returns", request.RequestUri!.AbsolutePath);
            Assert.Contains(OrderId, body, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(FilterLineId, body, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(GasketLineId, body, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("\"quantity\":2.5", body, StringComparison.Ordinal);
            Assert.Contains("RMA 1042", body, StringComparison.Ordinal);
            Assert.Contains("Draft return created.", editor.Markup, StringComparison.Ordinal);
            Assert.Equal("Supplier return", editor.Find("#supplier-return-editor-title").TextContent.Trim());
            Assert.Contains(ReturnPath + "/preview", Paths(), StringComparer.Ordinal);
        });
    }

    [Fact]
    public void Validation_errors_are_listed_once_above_the_table()
    {
        respond = request => request.Method == HttpMethod.Put
            ? Json(
                HttpStatusCode.BadRequest,
                """
                {"title":"One or more validation errors occurred.","status":400,
                "errors":{"lines[0].quantity":["Return quantity exceeds the remaining returnable quantity 0."]}}
                """)
            : DefaultResponse(request);

        var editor = RenderEditor();

        editor.Find(".stocktake-actions .maintenance-button").Click();

        editor.WaitForAssertion(() =>
        {
            var error = Assert.Single(editor.FindAll(".stocktake-validation li"));

            Assert.Contains("'FILTER-AIR-110'", error.TextContent, StringComparison.Ordinal);
            Assert.Contains(
                "Return quantity exceeds the remaining returnable quantity 0.",
                error.TextContent,
                StringComparison.Ordinal);
            Assert.Empty(editor.FindAll(".supplier-return-lines-table .maintenance-inline-error"));
            Assert.DoesNotContain(
                "remaining returnable",
                editor.Find(".supplier-return-lines-table").TextContent,
                StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Saving_a_draft_sends_the_expected_concurrency_token_and_rechecks()
    {
        var editor = RenderEditor();
        var previewsBefore = Paths().Count(path => path.EndsWith("/preview", StringComparison.Ordinal));

        editor.FindAll(".supplier-return-lines-table tbody tr")[1].QuerySelector("input[type=number]")!.Change("1");
        editor.FindAll(".supplier-return-lines-table tbody tr")[1].QuerySelector("input[type=text]")!.Change("Torn");
        editor.Find(".stocktake-actions .maintenance-button").Click();

        editor.WaitForAssertion(() =>
        {
            var request = requests.Last(candidate => candidate.Method == HttpMethod.Put);
            var body = requestBodies.Last();

            Assert.Equal(ReturnPath, request.RequestUri!.AbsolutePath);
            Assert.Contains(ConcurrencyToken, body, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(FilterLineId, body, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(GasketLineId, body, StringComparison.OrdinalIgnoreCase);
            Assert.True(Paths().Count(path => path.EndsWith("/preview", StringComparison.Ordinal)) > previewsBefore);
            Assert.Contains("Draft saved.", editor.Markup, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void A_stale_save_reports_the_conflict_and_reloads_the_return()
    {
        var editor = RenderEditor();
        var loadsBefore = Paths().Count(path => path == ReturnPath);

        respond = request => request.Method == HttpMethod.Put
            ? Json(
                HttpStatusCode.Conflict,
                """{"title":"Supplier return was modified concurrently.","detail":"Reload and try again."}""")
            : DefaultResponse(request);

        editor.Find(".stocktake-actions .maintenance-button").Click();

        editor.WaitForAssertion(() =>
        {
            Assert.Contains("Supplier return was modified concurrently.", editor.Find(".maintenance-conflict").TextContent, StringComparison.Ordinal);
            Assert.True(Paths().Count(path => path == ReturnPath) > loadsBefore);
        });
    }

    [Fact]
    public void A_reserved_stock_blocker_names_the_work_orders_and_keeps_posting_disabled()
    {
        previewJson = Preview(canPost: "false", blockers: ReservedStockBlocker());

        var editor = RenderEditor();

        var blocker = editor.Find(".stocktake-blockers li");

        Assert.Contains("Reserved for maintenance", blocker.TextContent, StringComparison.Ordinal);
        Assert.Contains(WorkOrderId, blocker.InnerHtml, StringComparison.OrdinalIgnoreCase);
        Assert.True(editor.Find(".stocktake-post").HasAttribute("disabled"));
        Assert.Contains("stocktake-line-stale", editor.FindAll(".supplier-return-lines-table tbody tr")[0].ClassName, StringComparison.Ordinal);
    }

    [Fact]
    public void Unsaved_changes_keep_posting_disabled_until_the_draft_is_saved()
    {
        var editor = RenderEditor();

        Assert.False(editor.Find(".stocktake-post").HasAttribute("disabled"));

        editor.FindAll(".supplier-return-lines-table tbody tr")[0].QuerySelector("input[type=number]")!.Change("3");

        Assert.True(editor.Find(".stocktake-post").HasAttribute("disabled"));
        Assert.Contains("Save your changes before posting.", editor.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Posting_asks_for_confirmation_with_the_stock_change_before_it_sends_anything()
    {
        var editor = RenderEditor();

        editor.Find(".stocktake-post").Click();

        var dialog = editor.Find(".maintenance-modal");

        Assert.Contains("Confirm posting", dialog.TextContent, StringComparison.Ordinal);
        Assert.Contains("FILTER-AIR-110: 12", dialog.TextContent, StringComparison.Ordinal);
        Assert.Contains("9.5", dialog.TextContent, StringComparison.Ordinal);
        Assert.Contains("cannot be edited or reversed", dialog.TextContent, StringComparison.Ordinal);
        Assert.DoesNotContain(Paths(), path => path.EndsWith("/post", StringComparison.Ordinal));
    }

    [Fact]
    public void Confirming_the_post_sends_the_preview_token_and_reports_the_movements()
    {
        var editor = RenderEditor();
        editor.Find(".stocktake-post").Click();

        editor.Find(".maintenance-modal .maintenance-button").Click();

        editor.WaitForAssertion(() =>
        {
            var body = requestBodies.Last(candidate => candidate.Contains("previewToken", StringComparison.Ordinal));

            Assert.Contains("TOKEN", body, StringComparison.Ordinal);
            Assert.Contains(ConcurrencyToken, body, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Return posted. 1 stock movement(s) recorded.", editor.Markup, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void A_repeated_post_says_that_stock_was_not_changed_again()
    {
        respond = request => request.RequestUri!.AbsolutePath.EndsWith("/post", StringComparison.Ordinal)
            ? Json(HttpStatusCode.OK, PostResult(alreadyPosted: true))
            : DefaultResponse(request);

        var editor = RenderEditor();
        editor.Find(".stocktake-post").Click();

        editor.Find(".maintenance-modal .maintenance-button").Click();

        editor.WaitForAssertion(() => Assert.Contains(
            "had already been posted",
            editor.Markup,
            StringComparison.Ordinal));
    }

    [Fact]
    public void A_stale_preview_on_posting_is_reported_and_the_return_is_reloaded()
    {
        var editor = RenderEditor();
        editor.Find(".stocktake-post").Click();
        var loadsBefore = Paths().Count(path => path == ReturnPath);

        respond = request => request.RequestUri!.AbsolutePath.EndsWith("/post", StringComparison.Ordinal)
            ? Json(
                HttpStatusCode.Conflict,
                """{"title":"Supplier return posting preview is out of date.","detail":"Review a new preview."}""")
            : DefaultResponse(request);

        editor.Find(".maintenance-modal .maintenance-button").Click();

        editor.WaitForAssertion(() =>
        {
            Assert.Contains("Supplier return posting preview is out of date.", editor.Markup, StringComparison.Ordinal);
            Assert.True(Paths().Count(path => path == ReturnPath) > loadsBefore);
            Assert.Empty(editor.FindAll(".maintenance-modal"));
        });
    }

    [Fact]
    public void Cancelling_requires_a_reason_before_it_is_sent()
    {
        var editor = RenderEditor();

        editor.FindAll(".stocktake-actions button").Last().Click();

        Assert.True(editor.Find(".maintenance-modal button[type=submit]").HasAttribute("disabled"));

        editor.Find("#supplier-return-cancel-reason").Change("Supplier issued a credit");
        editor.Find(".maintenance-modal button[type=submit]").Click();

        editor.WaitForAssertion(() =>
        {
            var request = requests.Last(candidate => candidate.Method == HttpMethod.Post);

            Assert.EndsWith("/cancel", request.RequestUri!.AbsolutePath, StringComparison.Ordinal);
            Assert.Contains("Supplier issued a credit", requestBodies.Last(), StringComparison.Ordinal);
            Assert.Contains("Return cancelled. Stock was not changed.", editor.Markup, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void A_posted_return_is_read_only_history_with_its_movements()
    {
        detailsJson = Details(status: "Posted", movements: Movement());

        var editor = RenderEditor();

        Assert.Empty(editor.FindAll(".stocktake-actions"));
        Assert.Empty(editor.FindAll("input"));
        Assert.DoesNotContain(Paths(), path => path.EndsWith("/preview", StringComparison.Ordinal));
        Assert.DoesNotContain(OrderPath, Paths());
        Assert.Single(editor.FindAll(".supplier-return-lines-table tbody tr"));

        var movement = editor.Find(".stocktake-adjustments-table tbody tr");

        Assert.Contains("FILTER-AIR-110", movement.TextContent, StringComparison.Ordinal);
        Assert.Contains("14.5", movement.TextContent, StringComparison.Ordinal);
        Assert.Contains("-2.5", movement.TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void A_read_only_planner_sees_the_draft_and_its_blockers_but_cannot_change_it()
    {
        detailsJson = Details(blockers: ReservedStockBlocker());

        var editor = RenderEditor("maintenance.read");

        Assert.Empty(editor.FindAll(".stocktake-actions"));
        Assert.Empty(editor.FindAll(".supplier-return-lines-table input"));
        Assert.DoesNotContain(Paths(), path => path.EndsWith("/preview", StringComparison.Ordinal));
        Assert.Contains("Reserved for maintenance", editor.Find(".stocktake-blockers").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void A_forbidden_return_explains_the_missing_permission()
    {
        respond = _ => Json(HttpStatusCode.Forbidden, """{"title":"Forbidden","detail":"No access"}""");

        var editor = RenderEditor(null, waitForLines: false, "maintenance.read");

        editor.WaitForAssertion(() =>
        {
            Assert.Contains("You cannot view this supplier return", editor.Markup, StringComparison.Ordinal);
            Assert.Empty(editor.FindAll(".api-error-state"));
        });
    }

    [Fact]
    public void Closing_the_editor_notifies_the_panel()
    {
        var closed = false;
        var editor = RenderEditor(parameters => parameters.Add(component => component.OnClosed, () => closed = true));

        editor.Find(".maintenance-card-heading .maintenance-reset").Click();

        Assert.True(closed);
    }

    private IEnumerable<string> Paths()
    {
        return requests.Select(request => request.RequestUri!.AbsolutePath).ToList();
    }

    private IRenderedComponent<SupplierReturnEditor> RenderEditor(params string[] permissions)
    {
        return RenderEditor(null, waitForLines: true, permissions);
    }

    private IRenderedComponent<SupplierReturnEditor> RenderEditor(
        Action<ComponentParameterCollectionBuilder<SupplierReturnEditor>>? parameterBuilder,
        bool waitForLines = true,
        params string[] permissions)
    {
        return Render(
            parameters =>
            {
                parameters.Add(component => component.SupplierReturnId, Guid.Parse(ReturnId));
                parameters.Add(component => component.PurchaseOrderId, Guid.Parse(OrderId));
                parameterBuilder?.Invoke(parameters);
            },
            waitForLines,
            permissions);
    }

    private IRenderedComponent<SupplierReturnEditor> RenderNewEditor(bool waitForLines = true)
    {
        return Render(
            parameters => parameters.Add(component => component.PurchaseOrderId, Guid.Parse(OrderId)),
            waitForLines,
            []);
    }

    private IRenderedComponent<SupplierReturnEditor> Render(
        Action<ComponentParameterCollectionBuilder<SupplierReturnEditor>> parameterBuilder,
        bool waitForLines,
        string[] permissions)
    {
        var httpClient = new HttpClient(new StubHttpMessageHandler(async (request, token) =>
        {
            requests.Add(request);

            if (request.Content is not null)
            {
                requestBodies.Add(await request.Content.ReadAsStringAsync(token));
            }

            return respond is not null
                ? await respond(request)
                : await DefaultResponse(request);
        }))
        {
            BaseAddress = new Uri("https://api.harborops.test/"),
        };

        Services.AddSingleton(new MaintenanceApiClient(httpClient));
        Services.AddSingleton(CreateAuthorizationState(
            permissions.Length == 0 ? ["maintenance.read", "maintenance.manage"] : permissions));

        var editor = Render<SupplierReturnEditor>(parameterBuilder);

        if (waitForLines)
        {
            editor.WaitForAssertion(() => Assert.NotEmpty(editor.FindAll(".supplier-return-lines-table")));
        }

        return editor;
    }

    private static CurrentUserAuthorizationState CreateAuthorizationState(IEnumerable<string> permissions)
    {
        var identity = new ClaimsIdentity("test");
        foreach (var permission in permissions)
        {
            identity.AddClaim(new Claim(CurrentUserAuthorizationState.PermissionClaimType, permission));
        }

        var context = new DefaultHttpContext { User = new ClaimsPrincipal(identity) };

        return new CurrentUserAuthorizationState(new HttpContextAccessor { HttpContext = context });
    }

    private Task<HttpResponseMessage> DefaultResponse(HttpRequestMessage request)
    {
        var path = request.RequestUri!.AbsolutePath;

        if (path == OrderPath)
        {
            return Json(HttpStatusCode.OK, orderJson);
        }

        if (path.EndsWith("/preview", StringComparison.Ordinal))
        {
            return Json(HttpStatusCode.OK, previewJson);
        }

        if (path.EndsWith("/post", StringComparison.Ordinal))
        {
            return Json(HttpStatusCode.OK, PostResult(alreadyPosted: false));
        }

        if (path.EndsWith("/cancel", StringComparison.Ordinal))
        {
            return Json(HttpStatusCode.OK, Details(status: "Cancelled"));
        }

        if (path == "/api/maintenance/supplier-returns" && request.Method == HttpMethod.Post)
        {
            return Json(HttpStatusCode.Created, Details());
        }

        return Json(HttpStatusCode.OK, detailsJson);
    }

    private static string Order(string status = "Received")
    {
        return $$"""
            {"purchaseOrderId":"{{OrderId}}","supplierId":"{{FilterId}}",
            "supplierName":"North Sea Marine Supply","supplierContactPerson":null,
            "supplierPhoneEmail":null,"orderDate":"2026-09-01","expectedDelivery":null,
            "status":"{{status}}","isOverdue":false,"daysUntilDelivery":null,"totalCost":250.00,
            "allowedTransitions":[],
            "lines":[
            {"lineId":"{{FilterLineId}}","inventoryItemId":"{{FilterId}}",
            "inventoryItemName":"Main engine air filter","partNumber":"FILTER-AIR-110",
            "quantityOrdered":10,"unitCost":20.00,"lineTotal":200.00},
            {"lineId":"{{GasketLineId}}","inventoryItemId":"{{GasketId}}",
            "inventoryItemName":"Cylinder head gasket","partNumber":"GASKET-22",
            "quantityOrdered":5,"unitCost":10.00,"lineTotal":50.00}]}
            """;
    }

    private static string Line()
    {
        return $$"""
            {"supplierReturnLineId":"{{WorkOrderId}}","purchaseOrderLineId":"{{FilterLineId}}",
            "inventoryItemId":"{{FilterId}}","name":"Main engine air filter","partNumber":"FILTER-AIR-110",
            "unitAbbreviation":"pcs","receivedQuantity":10,"previouslyReturnedQuantity":1,
            "remainingReturnableQuantity":6.5,"onHandQuantity":12,"reservedQuantity":2,
            "quantity":2.5,"reason":"Wrong size"}
            """;
    }

    private static string Details(string status = "Draft", string blockers = "", string movements = "")
    {
        return $$"""
            {"supplierReturnId":"{{ReturnId}}","purchaseOrderId":"{{OrderId}}",
            "purchaseOrderStatus":"Received","supplierId":"{{FilterId}}",
            "supplierName":"North Sea Marine Supply","status":"{{status}}","notes":null,
            "concurrencyToken":"{{ConcurrencyToken}}","createdByUserId":"{{FilterId}}",
            "createdUtc":"2026-09-24T08:00:00Z","lastModifiedByUserId":null,"lastModifiedUtc":null,
            "postedByUserId":null,"postedUtc":{{(status == "Posted" ? "\"2026-09-24T09:00:00Z\"" : "null")}},
            "cancelledByUserId":null,"cancelledUtc":{{(status == "Cancelled" ? "\"2026-09-24T09:00:00Z\"" : "null")}},
            "cancellationReason":{{(status == "Cancelled" ? "\"Supplier issued a credit\"" : "null")}},
            "lines":[{{Line()}}],
            "blockers":[{{blockers}}],
            "movements":[{{movements}}]}
            """;
    }

    private static string Preview(string canPost = "true", string blockers = "")
    {
        return $$"""
            {"supplierReturnId":"{{ReturnId}}","concurrencyToken":"{{ConcurrencyToken}}",
            "previewToken":"TOKEN","canPost":{{canPost}},
            "lines":[{{Line()}}],
            "items":[{"inventoryItemId":"{{FilterId}}","name":"Main engine air filter",
            "partNumber":"FILTER-AIR-110","onHandQuantity":12,"reservedQuantity":2,
            "availableQuantity":10,"returnQuantity":2.5,"quantityAfter":9.5}],
            "blockers":[{{blockers}}],"generatedUtc":"2026-09-24T08:00:00Z"}
            """;
    }

    private static string ReservedStockBlocker()
    {
        return $$"""
            {"purchaseOrderLineId":null,"inventoryItemId":"{{FilterId}}","code":"ReservedStock",
            "message":"Returning 2.500 of 'FILTER-AIR-110' would consume stock reserved for maintenance.",
            "reservingWorkOrderIds":["{{WorkOrderId}}"]}
            """;
    }

    private static string Movement()
    {
        return $$"""
            {"movementId":"{{WorkOrderId}}","inventoryItemId":"{{FilterId}}",
            "supplierReturnId":"{{ReturnId}}","supplierReturnLineId":"{{WorkOrderId}}",
            "quantityBefore":14.5,"quantityAfter":12,"quantityDelta":-2.5,"reason":"Wrong size",
            "actorUserId":"{{FilterId}}","movedUtc":"2026-09-24T09:00:00Z"}
            """;
    }

    private static string PostResult(bool alreadyPosted)
    {
        return $$"""
            {"supplierReturnId":"{{ReturnId}}","status":"Posted","concurrencyToken":"{{ConcurrencyToken}}",
            "postedByUserId":"{{FilterId}}","postedUtc":"2026-09-24T09:00:00Z",
            "alreadyPosted":{{(alreadyPosted ? "true" : "false")}},
            "movements":[{{Movement()}}]}
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
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responseFactory)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => responseFactory(request, cancellationToken);
    }
}
