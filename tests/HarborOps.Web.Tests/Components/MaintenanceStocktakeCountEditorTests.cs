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

public sealed class MaintenanceStocktakeCountEditorTests : BunitContext
{
    private const string StocktakeId = "a0000000-0000-0000-0000-00000000000a";
    private const string FilterId = "b0000000-0000-0000-0000-00000000000b";
    private const string WorkOrderId = "c0000000-0000-0000-0000-00000000000c";
    private const string ConcurrencyToken = "d0000000-0000-0000-0000-00000000000d";

    private readonly List<HttpRequestMessage> requests = [];
    private readonly List<string> requestBodies = [];

    private string detailsJson = Details();
    private string previewJson = Preview();
    private Func<HttpRequestMessage, Task<HttpResponseMessage>>? respond;

    [Fact]
    public void Opening_a_draft_loads_the_lines_and_the_posting_preview()
    {
        var editor = RenderEditor();

        Assert.Contains("/api/maintenance/stocktakes/" + StocktakeId, Paths(), StringComparer.Ordinal);
        Assert.Contains("/api/maintenance/stocktakes/" + StocktakeId + "/preview", Paths(), StringComparer.Ordinal);

        var cells = editor.Find(".stocktake-lines-table tbody tr").QuerySelectorAll("td");

        Assert.Contains("Main engine filter", cells[0].TextContent, StringComparison.Ordinal);
        Assert.Equal("FILTER-001", cells[1].TextContent.Trim());
        Assert.Contains("10", cells[2].TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void A_counted_line_shows_its_variance()
    {
        detailsJson = Details(countedQuantity: "8", variance: "-2", varianceReason: "\"Missing\"");

        var editor = RenderEditor();

        var variance = editor.Find(".stocktake-variance");

        Assert.Contains("shortage", variance.ClassName, StringComparison.Ordinal);
        Assert.Equal("-2", variance.TextContent.Trim());
    }

    [Fact]
    public void The_draft_offers_saving_posting_and_cancelling_only()
    {
        var editor = RenderEditor();

        var actions = editor.FindAll(".stocktake-actions button");

        Assert.Equal(3, actions.Count);
        Assert.Equal("Save and check", actions[0].TextContent.Trim());
        Assert.Equal("Post stocktake", actions[1].TextContent.Trim());
        Assert.Equal("Cancel stocktake", actions[2].TextContent.Trim());
        Assert.Empty(editor.FindAll(".stocktake-lines-table input[type=checkbox]"));
    }

    [Fact]
    public void Saving_also_checks_the_stocktake_and_reports_the_result()
    {
        previewJson = Preview(canPost: "true");

        var editor = RenderEditor();
        var previewsBefore = Paths().Count(path => path.EndsWith("/preview", StringComparison.Ordinal));

        editor.Find(".stocktake-actions .maintenance-button").Click();

        editor.WaitForAssertion(() =>
        {
            Assert.True(
                Paths().Count(path => path.EndsWith("/preview", StringComparison.Ordinal)) > previewsBefore);
            Assert.Contains("Counts saved.", editor.Markup, StringComparison.Ordinal);
            Assert.Contains("nothing blocks posting", editor.Markup, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Saving_counts_sends_the_expected_concurrency_token_and_the_typed_values()
    {
        var editor = RenderEditor();

        editor.Find(".stocktake-lines-table tbody tr input[type=number]").Change("8");
        editor.Find(".stocktake-lines-table tbody tr input[type=text]").Change("Missing");
        editor.Find(".stocktake-actions .maintenance-button").Click();

        editor.WaitForAssertion(() =>
        {
            var request = requests.Last(candidate => candidate.Method == HttpMethod.Put);
            var body = requestBodies.Last();

            Assert.EndsWith("/counts", request.RequestUri!.AbsolutePath, StringComparison.Ordinal);
            Assert.Contains(ConcurrencyToken, body, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("\"countedQuantity\":8", body, StringComparison.Ordinal);
            Assert.Contains("Missing", body, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void A_missing_reason_is_reported_on_the_line_that_needs_it()
    {
        respond = request => request.Method == HttpMethod.Put
            ? Json(
                HttpStatusCode.BadRequest,
                """
                {"title":"One or more validation errors occurred.","status":400,
                "errors":{"lines[0].reason":["A reason is required when the count differs from the baseline."]}}
                """)
            : DefaultResponse(request);

        var editor = RenderEditor();

        editor.Find(".stocktake-lines-table tbody tr input[type=number]").Change("8");
        editor.Find(".stocktake-actions .maintenance-button").Click();

        editor.WaitForAssertion(() =>
        {
            var error = editor.Find(".stocktake-validation li");

            Assert.Contains("'FILTER-001'", error.TextContent, StringComparison.Ordinal);
            Assert.Contains(
                "A reason is required when the count differs from the baseline.",
                error.TextContent,
                StringComparison.Ordinal);
            Assert.Empty(editor.FindAll(".api-error-state"));
            Assert.Empty(editor.FindAll(".stocktake-lines-table .maintenance-inline-error"));
        });
    }

    [Fact]
    public void A_validation_error_without_a_line_is_listed_on_the_form()
    {
        respond = request => request.Method == HttpMethod.Put
            ? Json(
                HttpStatusCode.BadRequest,
                """
                {"title":"One or more validation errors occurred.","status":400,
                "errors":{"expectedConcurrencyToken":["Expected concurrency token is required."]}}
                """)
            : DefaultResponse(request);

        var editor = RenderEditor();

        editor.Find(".stocktake-actions .maintenance-button").Click();

        editor.WaitForAssertion(() =>
        {
            var error = editor.Find(".stocktake-validation li");

            Assert.Contains("Stocktake", error.TextContent, StringComparison.Ordinal);
            Assert.Contains(
                "Expected concurrency token is required.",
                error.TextContent,
                StringComparison.Ordinal);
        });
    }

    [Fact]
    public void A_stale_save_reports_the_conflict_and_reloads_the_stocktake()
    {
        var editor = RenderEditor();
        var loadsBefore = Paths().Count(path => path == "/api/maintenance/stocktakes/" + StocktakeId);

        respond = request => request.Method == HttpMethod.Put
            ? Json(
                HttpStatusCode.Conflict,
                """{"title":"Stocktake was modified concurrently.","detail":"Reload and try again."}""")
            : DefaultResponse(request);

        editor.Find(".stocktake-actions .maintenance-button").Click();

        editor.WaitForAssertion(() =>
        {
            Assert.Contains("Stocktake was modified concurrently.", editor.Markup, StringComparison.Ordinal);
            Assert.True(
                Paths().Count(path => path == "/api/maintenance/stocktakes/" + StocktakeId) > loadsBefore);
        });
    }

    [Fact]
    public void Only_a_stale_baseline_offers_a_recount()
    {
        var editor = RenderEditor();

        Assert.Empty(editor.FindAll(".stocktake-recount"));

        previewJson = Preview(blockers: StaleBaselineBlocker());
        editor.Find(".stocktake-actions .maintenance-button").Click();

        editor.WaitForAssertion(() => Assert.NotEmpty(editor.FindAll(".stocktake-recount")));
    }

    [Fact]
    public void Recounting_refreshes_that_line_and_warns_that_the_count_was_cleared()
    {
        previewJson = Preview(blockers: StaleBaselineBlocker());

        var editor = RenderEditor();

        editor.Find(".stocktake-recount").Click();

        editor.WaitForAssertion(() =>
        {
            var request = requests.Last(candidate => candidate.Method == HttpMethod.Post);
            var body = requestBodies.Last();

            Assert.EndsWith("/refresh", request.RequestUri!.AbsolutePath, StringComparison.Ordinal);
            Assert.Contains(FilterId, body, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Count it again before posting.", editor.Markup, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void A_blocker_names_the_reserving_work_orders_and_keeps_posting_disabled()
    {
        previewJson = Preview(
            canPost: "false",
            blockers: $$"""
                {"inventoryItemId":"{{FilterId}}","code":"BelowReserved",
                "message":"Counted 5.000 is below the outstanding reserved quantity 6.000.",
                "reservingWorkOrderIds":["{{WorkOrderId}}"]}
                """);

        var editor = RenderEditor();

        var blocker = editor.Find(".stocktake-blockers li");

        Assert.Contains("Below reservations", blocker.TextContent, StringComparison.Ordinal);
        Assert.Contains(WorkOrderId, blocker.InnerHtml, StringComparison.OrdinalIgnoreCase);
        Assert.True(editor.Find(".stocktake-post").HasAttribute("disabled"));
    }

    [Fact]
    public void Posting_asks_for_confirmation_before_it_sends_anything()
    {
        previewJson = Preview(canPost: "true");

        var editor = RenderEditor();

        editor.Find(".stocktake-post").Click();

        Assert.Contains("Confirm posting", editor.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain(requests, request => request.RequestUri!.AbsolutePath.EndsWith("/post", StringComparison.Ordinal));
    }

    [Fact]
    public void Confirming_the_post_sends_the_preview_token_and_reports_the_adjustments()
    {
        previewJson = Preview(canPost: "true");
        detailsJson = Details(countedQuantity: "8", variance: "-2", varianceReason: "\"Missing\"");

        var editor = RenderEditor();
        editor.Find(".stocktake-post").Click();

        editor.Find(".maintenance-modal .maintenance-button").Click();

        editor.WaitForAssertion(() =>
        {
            var body = requestBodies.Last(candidate => candidate.Contains("previewToken", StringComparison.Ordinal));

            Assert.Contains("TOKEN", body, StringComparison.Ordinal);
            Assert.Contains(ConcurrencyToken, body, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Stocktake posted with 1 adjustment(s).", editor.Markup, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void A_stale_preview_is_reported_without_changing_the_draft()
    {
        previewJson = Preview(canPost: "true");

        var editor = RenderEditor();
        editor.Find(".stocktake-post").Click();

        respond = request => request.RequestUri!.AbsolutePath.EndsWith("/post", StringComparison.Ordinal)
            ? Json(
                HttpStatusCode.Conflict,
                """{"title":"Stocktake posting preview is out of date.","detail":"Review a new preview."}""")
            : DefaultResponse(request);

        editor.Find(".maintenance-modal .maintenance-button").Click();

        editor.WaitForAssertion(() =>
        {
            Assert.Contains(
                "Stocktake posting preview is out of date.",
                editor.Markup,
                StringComparison.Ordinal);
            Assert.NotNull(editor.Find(".stocktake-lines-table"));
        });
    }

    [Fact]
    public void Cancelling_requires_a_reason_before_it_is_sent()
    {
        var editor = RenderEditor();

        editor.FindAll(".stocktake-actions button").Last().Click();

        var submit = editor.Find(".maintenance-modal button[type=submit]");

        Assert.True(submit.HasAttribute("disabled"));

        editor.Find("#stocktake-cancel-reason").Change("Counted the wrong locker");
        editor.Find(".maintenance-modal button[type=submit]").Click();

        editor.WaitForAssertion(() =>
        {
            var request = requests.Last(candidate => candidate.Method == HttpMethod.Post);

            Assert.EndsWith("/cancel", request.RequestUri!.AbsolutePath, StringComparison.Ordinal);
            Assert.Contains("Counted the wrong locker", requestBodies.Last(), StringComparison.Ordinal);
        });
    }

    [Fact]
    public void A_posted_stocktake_is_read_only_history_with_its_adjustments()
    {
        detailsJson = PostedDetails();

        var editor = RenderEditor();

        Assert.Empty(editor.FindAll(".stocktake-actions"));
        Assert.Empty(editor.FindAll("input"));
        Assert.DoesNotContain(
            Paths(),
            path => path.EndsWith("/preview", StringComparison.Ordinal));

        var adjustment = editor.Find(".stocktake-adjustments-table tbody tr");

        Assert.Contains("Missing", adjustment.TextContent, StringComparison.Ordinal);
        Assert.Contains("-2", adjustment.TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void A_read_only_planner_cannot_change_a_draft()
    {
        var editor = RenderEditor("maintenance.read");

        Assert.Empty(editor.FindAll(".stocktake-actions"));
        Assert.Empty(editor.FindAll(".stocktake-lines-table input"));
    }

    [Fact]
    public void Closing_the_editor_notifies_the_panel()
    {
        var closed = false;
        var editor = RenderEditor(
            parameters => parameters.Add(component => component.OnClosed, () => closed = true));

        editor.Find(".maintenance-card-heading .maintenance-reset").Click();

        Assert.True(closed);
    }

    private IEnumerable<string> Paths()
    {
        return requests.Select(request => request.RequestUri!.AbsolutePath).ToList();
    }

    private IRenderedComponent<StocktakeCountEditor> RenderEditor(params string[] permissions)
    {
        return RenderEditor(null, permissions);
    }

    private IRenderedComponent<StocktakeCountEditor> RenderEditor(
        Action<ComponentParameterCollectionBuilder<StocktakeCountEditor>>? parameterBuilder,
        params string[] permissions)
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

        var editor = Render<StocktakeCountEditor>(parameters =>
        {
            parameters.Add(component => component.StocktakeId, Guid.Parse(StocktakeId));
            parameterBuilder?.Invoke(parameters);
        });

        editor.WaitForAssertion(() => Assert.NotEmpty(editor.FindAll(".stocktake-lines-table")));

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

        if (path.EndsWith("/preview", StringComparison.Ordinal))
        {
            return Json(HttpStatusCode.OK, previewJson);
        }

        if (path.EndsWith("/post", StringComparison.Ordinal))
        {
            return Json(HttpStatusCode.OK, PostResult());
        }

        return Json(HttpStatusCode.OK, detailsJson);
    }

    private static string Details(
        string countedQuantity = "null",
        string variance = "null",
        string varianceReason = "null")
    {
        return $$"""
            {"stocktakeId":"{{StocktakeId}}","status":"Draft","notes":"Engine room locker",
            "concurrencyToken":"{{ConcurrencyToken}}","createdByUserId":"{{FilterId}}",
            "createdUtc":"2026-09-23T08:00:00Z","lastModifiedByUserId":null,"lastModifiedUtc":null,
            "postedByUserId":null,"postedUtc":null,"cancelledByUserId":null,"cancelledUtc":null,
            "cancellationReason":null,
            "lines":[{"inventoryItemId":"{{FilterId}}","name":"Main engine filter",
            "partNumber":"FILTER-001","unitAbbreviation":"pcs","baselineQuantity":10,
            "baselineCapturedUtc":"2026-09-23T08:00:00Z","countedQuantity":{{countedQuantity}},
            "variance":{{variance}},"varianceReason":{{varianceReason}},
            "countedByUserId":null,"countedUtc":null}],
            "adjustments":[]}
            """;
    }

    private static string PostedDetails()
    {
        return $$"""
            {"stocktakeId":"{{StocktakeId}}","status":"Posted","notes":null,
            "concurrencyToken":"{{ConcurrencyToken}}","createdByUserId":"{{FilterId}}",
            "createdUtc":"2026-09-23T08:00:00Z","lastModifiedByUserId":"{{FilterId}}",
            "lastModifiedUtc":"2026-09-23T09:00:00Z","postedByUserId":"{{FilterId}}",
            "postedUtc":"2026-09-23T09:00:00Z","cancelledByUserId":null,"cancelledUtc":null,
            "cancellationReason":null,
            "lines":[{"inventoryItemId":"{{FilterId}}","name":"Main engine filter",
            "partNumber":"FILTER-001","unitAbbreviation":"pcs","baselineQuantity":10,
            "baselineCapturedUtc":"2026-09-23T08:00:00Z","countedQuantity":8,"variance":-2,
            "varianceReason":"Missing","countedByUserId":"{{FilterId}}",
            "countedUtc":"2026-09-23T08:30:00Z"}],
            "adjustments":[{"adjustmentId":"{{WorkOrderId}}","inventoryItemId":"{{FilterId}}",
            "stocktakeId":"{{StocktakeId}}","quantityBefore":10,"countedQuantity":8,
            "quantityDelta":-2,"reason":"Missing","actorUserId":"{{FilterId}}",
            "adjustedUtc":"2026-09-23T09:00:00Z"}]}
            """;
    }

    private static string StaleBaselineBlocker()
    {
        return $$"""
            {"inventoryItemId":"{{FilterId}}","code":"StaleBaseline",
            "message":"Stock for 'FILTER-001' changed after the count baseline was captured.",
            "reservingWorkOrderIds":[]}
            """;
    }

    private static string Preview(string canPost = "false", string blockers = "")
    {
        return $$"""
            {"stocktakeId":"{{StocktakeId}}","concurrencyToken":"{{ConcurrencyToken}}",
            "previewToken":"TOKEN","canPost":{{canPost}},
            "lines":[{"inventoryItemId":"{{FilterId}}","name":"Main engine filter",
            "partNumber":"FILTER-001","baselineQuantity":10,"currentQuantity":10,
            "countedQuantity":null,"variance":null,"outstandingReservedQuantity":0,
            "baselineIsStale":false}],
            "blockers":[{{blockers}}],"generatedUtc":"2026-09-23T08:00:00Z"}
            """;
    }

    private static string PostResult()
    {
        return $$"""
            {"stocktakeId":"{{StocktakeId}}","status":"Posted","concurrencyToken":"{{ConcurrencyToken}}",
            "postedByUserId":"{{FilterId}}","postedUtc":"2026-09-23T09:00:00Z",
            "adjustments":[{"adjustmentId":"{{WorkOrderId}}","inventoryItemId":"{{FilterId}}",
            "stocktakeId":"{{StocktakeId}}","quantityBefore":10,"countedQuantity":8,
            "quantityDelta":-2,"reason":"Missing","actorUserId":"{{FilterId}}",
            "adjustedUtc":"2026-09-23T09:00:00Z"}]}
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
