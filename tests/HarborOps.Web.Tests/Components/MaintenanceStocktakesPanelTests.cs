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

public sealed class MaintenanceStocktakesPanelTests : BunitContext
{
    private const string StocktakeId = "a0000000-0000-0000-0000-00000000000a";
    private const string FilterId = "b0000000-0000-0000-0000-00000000000b";
    private const string ConcurrencyToken = "d0000000-0000-0000-0000-00000000000d";

    private readonly List<Uri> requestedUris = [];
    private readonly List<string> requestBodies = [];

    private Func<HttpRequestMessage, Task<HttpResponseMessage>> respond = DefaultResponse;

    private Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>? respondWithToken;

    [Fact]
    public void The_list_is_requested_with_the_default_page()
    {
        RenderPanel();

        var query = requestedUris.Last();

        Assert.Equal("/api/maintenance/stocktakes", query.AbsolutePath);
        Assert.Contains("pageNumber=1", query.Query, StringComparison.Ordinal);
        Assert.Contains("pageSize=25", query.Query, StringComparison.Ordinal);
    }

    [Fact]
    public void A_stocktake_is_listed_with_its_line_counts()
    {
        var panel = RenderPanel();

        var cells = panel.Find(".stocktake-table tbody tr").QuerySelectorAll("td");

        Assert.Equal("Draft", cells[1].TextContent.Trim());
        Assert.Equal("4", cells[2].TextContent.Trim());
        Assert.Equal("3", cells[3].TextContent.Trim());
        Assert.Equal("1", cells[4].TextContent.Trim());
    }

    [Theory]
    [InlineData("Draft", "pending")]
    [InlineData("Posted", "done")]
    [InlineData("Cancelled", "cancelled")]
    public void The_status_is_shown_as_a_badge(string status, string modifier)
    {
        respond = _ => Json(HttpStatusCode.OK, StocktakesPage(Summary(status: status)));

        var panel = RenderPanel();

        var badge = panel.Find(".stocktake-status");

        Assert.Contains(modifier, badge.ClassName, StringComparison.Ordinal);
        Assert.Equal(status, badge.TextContent.Trim());
    }

    [Fact]
    public void The_summary_counts_drafts_and_variance_lines()
    {
        respond = _ => Json(
            HttpStatusCode.OK,
            StocktakesPage(
                Summary(varianceLineCount: 1),
                Summary(status: "Posted", varianceLineCount: 2)));

        var panel = RenderPanel();

        var totals = panel.FindAll(".maintenance-summary-card strong");

        Assert.Equal("2", totals[0].TextContent.Trim());
        Assert.Equal("1", totals[1].TextContent.Trim());
        Assert.Equal("3", totals[2].TextContent.Trim());
    }

    [Fact]
    public void An_empty_list_invites_starting_the_first_stocktake()
    {
        respond = _ => Json(HttpStatusCode.OK, StocktakesPage());

        var panel = RenderPanel();

        Assert.Empty(panel.FindAll(".stocktake-table"));
        Assert.Contains("No stocktakes yet", panel.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void A_failed_load_offers_a_retry_that_asks_again()
    {
        respond = _ => Json(HttpStatusCode.InternalServerError, """{"title":"Server error","detail":"Broken"}""");

        var panel = RenderPanel();
        var before = requestedUris.Count;

        respond = _ => Json(HttpStatusCode.OK, StocktakesPage(Summary()));
        panel.Find(".api-error-state button").Click();

        panel.WaitForAssertion(() =>
        {
            Assert.True(requestedUris.Count > before);
            Assert.NotNull(panel.Find(".stocktake-table"));
        });
    }

    [Fact]
    public void A_forbidden_list_explains_the_missing_permission()
    {
        respond = _ => Json(HttpStatusCode.Forbidden, """{"title":"Forbidden","detail":"No access"}""");

        var panel = RenderPanel();

        Assert.Contains("You cannot view stocktakes", panel.Markup, StringComparison.Ordinal);
        Assert.Empty(panel.FindAll(".api-error-state"));
    }

    [Fact]
    public void Filtering_by_status_reloads_from_the_first_page()
    {
        var panel = RenderPanel();

        panel.Find("#stocktake-status").Change("Posted");

        panel.WaitForAssertion(() =>
        {
            var query = requestedUris.Last().Query;

            Assert.Contains("status=Posted", query, StringComparison.Ordinal);
            Assert.Contains("pageNumber=1", query, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Paging_forward_asks_for_the_next_page()
    {
        respond = _ => Json(HttpStatusCode.OK, Page(1, 25, 60, Summary()));

        var panel = RenderPanel();

        panel.FindAll(".maintenance-pages button")[1].Click();

        panel.WaitForAssertion(() => Assert.Contains(
            "pageNumber=2",
            requestedUris.Last().Query,
            StringComparison.Ordinal));
    }

    [Fact]
    public void The_read_only_planner_cannot_start_a_stocktake()
    {
        var panel = RenderPanel("maintenance.read");

        Assert.Empty(panel.FindAll(".stocktake-new"));
    }

    [Fact]
    public void Opening_the_create_form_lists_the_parts_to_count()
    {
        var panel = RenderManagerPanel();

        panel.Find(".stocktake-new").Click();

        panel.WaitForAssertion(() =>
        {
            Assert.Contains(
                requestedUris,
                uri => uri.AbsolutePath == "/api/maintenance/inventory-items");
            Assert.NotEmpty(panel.FindAll(".stocktake-part-options button"));
        });
    }

    [Fact]
    public void A_selected_part_is_listed_as_a_chip_and_cannot_be_picked_twice()
    {
        var panel = RenderManagerPanel();
        panel.Find(".stocktake-new").Click();
        panel.WaitForAssertion(() => Assert.NotEmpty(panel.FindAll(".stocktake-part-options button")));

        panel.Find(".stocktake-part-options button").Click();

        var chip = panel.Find(".stocktake-chip");

        Assert.Contains("FILTER-001", chip.TextContent, StringComparison.Ordinal);
        Assert.True(panel.Find(".stocktake-part-options button").HasAttribute("disabled"));
    }

    [Fact]
    public void Creating_a_stocktake_sends_the_selected_parts_and_opens_the_count_editor()
    {
        var panel = RenderManagerPanel();
        panel.Find(".stocktake-new").Click();
        panel.WaitForAssertion(() => Assert.NotEmpty(panel.FindAll(".stocktake-part-options button")));
        panel.Find(".stocktake-part-options button").Click();
        panel.Find("#stocktake-notes").Change("Engine room locker");

        panel.Find(".stocktake-create button[type=submit]").Click();

        panel.WaitForAssertion(() =>
        {
            var body = requestBodies.Last();

            Assert.Contains(FilterId, body, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Engine room locker", body, StringComparison.Ordinal);
            Assert.NotNull(panel.Find(".stocktake-editor"));
        });
    }

    [Fact]
    public void A_rejected_creation_keeps_the_form_open_with_the_reason()
    {
        respond = request => request.Method == HttpMethod.Post
            ? Json(
                HttpStatusCode.BadRequest,
                """{"title":"One or more validation errors occurred.","detail":"Check the items"}""")
            : DefaultResponse(request);

        var panel = RenderManagerPanel();
        panel.Find(".stocktake-new").Click();
        panel.WaitForAssertion(() => Assert.NotEmpty(panel.FindAll(".stocktake-part-options button")));
        panel.Find(".stocktake-part-options button").Click();

        panel.Find(".stocktake-create button[type=submit]").Click();

        panel.WaitForAssertion(() =>
        {
            Assert.Contains(
                "One or more validation errors occurred.",
                panel.Markup,
                StringComparison.Ordinal);
            Assert.NotNull(panel.Find(".stocktake-create"));
        });
    }

    [Fact]
    public void Opening_a_stocktake_shows_the_count_editor_and_closing_returns_to_the_list()
    {
        var panel = RenderManagerPanel();

        panel.Find(".stocktake-table tbody tr button").Click();

        panel.WaitForAssertion(() => Assert.NotNull(panel.Find(".stocktake-editor")));

        panel.Find(".stocktake-editor .maintenance-reset").Click();

        panel.WaitForAssertion(() => Assert.NotNull(panel.Find(".stocktake-table")));
    }

    [Fact]
    public async Task Disposing_the_panel_cancels_the_in_flight_request()
    {
        var started = new TaskCompletionSource();
        CancellationToken observed = default;

        respondWithToken = (_, token) =>
        {
            observed = token;
            started.TrySetResult();

            return new TaskCompletionSource<HttpResponseMessage>().Task;
        };

        RenderPanel();

        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await DisposeComponentsAsync();

        Assert.True(observed.IsCancellationRequested);
    }

    private IRenderedComponent<StocktakesPanel> RenderManagerPanel()
    {
        return RenderPanel("maintenance.read", "maintenance.manage");
    }

    private IRenderedComponent<StocktakesPanel> RenderPanel(params string[] permissions)
    {
        var httpClient = new HttpClient(new StubHttpMessageHandler(async (request, token) =>
        {
            requestedUris.Add(request.RequestUri!);

            if (request.Content is not null)
            {
                requestBodies.Add(await request.Content.ReadAsStringAsync(token));
            }

            return respondWithToken is not null
                ? await respondWithToken(request, token)
                : await respond(request);
        }))
        {
            BaseAddress = new Uri("https://api.harborops.test/"),
        };

        Services.AddSingleton(new MaintenanceApiClient(httpClient));
        Services.AddSingleton(CreateAuthorizationState(
            permissions.Length == 0 ? ["maintenance.read"] : permissions));

        var panel = Render<StocktakesPanel>();
        panel.WaitForAssertion(() => Assert.NotEmpty(requestedUris));

        return panel;
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

    private static Task<HttpResponseMessage> DefaultResponse(HttpRequestMessage request)
    {
        var path = request.RequestUri!.AbsolutePath;

        if (path == "/api/maintenance/inventory-items")
        {
            return Json(HttpStatusCode.OK, InventoryItemsPage());
        }

        if (path.EndsWith("/preview", StringComparison.Ordinal))
        {
            return Json(HttpStatusCode.OK, Preview());
        }

        if (path == $"/api/maintenance/stocktakes/{StocktakeId}")
        {
            return Json(HttpStatusCode.OK, Details());
        }

        return request.Method == HttpMethod.Post
            ? Json(HttpStatusCode.Created, Details())
            : Json(HttpStatusCode.OK, StocktakesPage(Summary()));
    }

    private static string Summary(
        string status = "Draft",
        int lineCount = 4,
        int countedLineCount = 3,
        int varianceLineCount = 1)
    {
        return $$"""
            {"stocktakeId":"{{StocktakeId}}","status":"{{status}}","lineCount":{{lineCount}},
            "countedLineCount":{{countedLineCount}},"varianceLineCount":{{varianceLineCount}},
            "createdByUserId":"{{FilterId}}","createdUtc":"2026-09-23T08:00:00Z",
            "postedUtc":null,"cancelledUtc":null,"concurrencyToken":"{{ConcurrencyToken}}"}
            """;
    }

    private static string Details()
    {
        return $$"""
            {"stocktakeId":"{{StocktakeId}}","status":"Draft","notes":"Engine room locker",
            "concurrencyToken":"{{ConcurrencyToken}}","createdByUserId":"{{FilterId}}",
            "createdUtc":"2026-09-23T08:00:00Z","lastModifiedByUserId":null,"lastModifiedUtc":null,
            "postedByUserId":null,"postedUtc":null,"cancelledByUserId":null,"cancelledUtc":null,
            "cancellationReason":null,
            "lines":[{"inventoryItemId":"{{FilterId}}","name":"Main engine filter",
            "partNumber":"FILTER-001","unitAbbreviation":"pcs","baselineQuantity":10,
            "baselineCapturedUtc":"2026-09-23T08:00:00Z","countedQuantity":null,"variance":null,
            "varianceReason":null,"countedByUserId":null,"countedUtc":null}],
            "adjustments":[]}
            """;
    }

    private static string Preview()
    {
        return $$"""
            {"stocktakeId":"{{StocktakeId}}","concurrencyToken":"{{ConcurrencyToken}}",
            "previewToken":"TOKEN","canPost":false,
            "lines":[{"inventoryItemId":"{{FilterId}}","name":"Main engine filter",
            "partNumber":"FILTER-001","baselineQuantity":10,"currentQuantity":10,
            "countedQuantity":null,"variance":null,"outstandingReservedQuantity":0,
            "baselineIsStale":false}],
            "blockers":[],"generatedUtc":"2026-09-23T08:00:00Z"}
            """;
    }

    private static string InventoryItemsPage()
    {
        return $$"""
            {"items":[{"inventoryItemId":"{{FilterId}}","name":"Main engine filter",
            "partNumber":"FILTER-001","quantity":10,"minimumQuantity":2,"unitCost":150,
            "categoryId":"{{FilterId}}","unitId":"{{FilterId}}","supplierId":"{{FilterId}}"}],
            "pageNumber":1,"pageSize":25,"totalCount":1}
            """;
    }

    private static string StocktakesPage(params string[] items)
    {
        return Page(1, 25, items.Length, items);
    }

    private static string Page(int pageNumber, int pageSize, int totalCount, params string[] items)
    {
        return $$"""
            {"items":[{{string.Join(',', items)}}],"pageNumber":{{pageNumber}},
            "pageSize":{{pageSize}},"totalCount":{{totalCount}}}
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
