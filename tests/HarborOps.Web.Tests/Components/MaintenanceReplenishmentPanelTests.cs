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

public sealed class MaintenanceReplenishmentPanelTests : BunitContext
{
    private const string FilterId = "b0000000-0000-0000-0000-00000000000b";
    private const string SupplierId = "c0000000-0000-0000-0000-00000000000c";

    private readonly List<Uri> requestedUris = [];

    private Func<HttpRequestMessage, Task<HttpResponseMessage>> respond =
        _ => Json(HttpStatusCode.OK, ReplenishmentPage(Row()));

    private Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>? respondWithToken;

    [Fact]
    public void The_board_is_requested_with_the_default_page()
    {
        RenderPanel();

        var query = requestedUris.Last();

        Assert.Equal("/api/maintenance/inventory/replenishment", query.AbsolutePath);
        Assert.Contains("pageNumber=1", query.Query, StringComparison.Ordinal);
        Assert.Contains("pageSize=25", query.Query, StringComparison.Ordinal);
    }

    [Fact]
    public void A_part_is_listed_with_stock_reservations_availability_and_shortage()
    {
        var panel = RenderPanel();

        var row = panel.Find(".replenishment-table tbody tr");

        Assert.Contains("Main engine filter", row.TextContent, StringComparison.Ordinal);
        Assert.Contains("FILTER-001", row.TextContent, StringComparison.Ordinal);
        Assert.Contains("Baltic Marine Parts", row.TextContent, StringComparison.Ordinal);

        var cells = row.QuerySelectorAll("td");

        Assert.Equal("8", cells[3].TextContent.Trim());
        Assert.Equal("3", cells[4].TextContent.Trim());
        Assert.Equal("5", cells[5].TextContent.Trim());
        Assert.Equal("10", cells[6].TextContent.Trim());
        Assert.Equal("5", cells[7].TextContent.Trim());
        Assert.Equal("0", cells[8].TextContent.Trim());
    }

    [Theory]
    [InlineData("Critical", "critical", "Out of stock")]
    [InlineData("Low", "low", "Below reorder level")]
    [InlineData("Covered", "covered", "On order")]
    public void The_classification_is_shown_as_a_badge(
        string classification,
        string modifier,
        string label)
    {
        respond = _ => Json(HttpStatusCode.OK, ReplenishmentPage(Row(classification: classification)));

        var panel = RenderPanel();

        var badge = panel.Find(".replenishment-state");

        Assert.Contains(modifier, badge.ClassName, StringComparison.Ordinal);
        Assert.Equal(label, badge.TextContent.Trim());
    }

    [Fact]
    public void The_summary_counts_the_critical_and_covered_parts()
    {
        respond = _ => Json(
            HttpStatusCode.OK,
            ReplenishmentPage(
                Row(classification: "Critical", shortageQuantity: "5"),
                Row(name: "Gasket", partNumber: "PN-2", classification: "Covered", shortageQuantity: "2"),
                Row(name: "Bearing", partNumber: "PN-3", classification: "Low", shortageQuantity: "1")));

        var panel = RenderPanel();

        var totals = panel.FindAll(".maintenance-summary-card strong");

        Assert.Equal("3", totals[0].TextContent.Trim());
        Assert.Equal("1", totals[1].TextContent.Trim());
        Assert.Equal("1", totals[2].TextContent.Trim());
        Assert.Equal("8", totals[3].TextContent.Trim());
    }

    [Fact]
    public void An_empty_board_explains_that_nothing_needs_replenishment()
    {
        respond = _ => Json(HttpStatusCode.OK, ReplenishmentPage());

        var panel = RenderPanel();

        Assert.Empty(panel.FindAll(".replenishment-table"));
        Assert.Contains(
            "Every part is above its reorder level",
            panel.Markup,
            StringComparison.Ordinal);
    }

    [Fact]
    public void A_failed_load_offers_a_retry_that_asks_again()
    {
        respond = _ => Json(HttpStatusCode.InternalServerError, """{"title":"Server error","detail":"Broken"}""");

        var panel = RenderPanel();
        var before = requestedUris.Count;

        respond = _ => Json(HttpStatusCode.OK, ReplenishmentPage(Row()));
        panel.Find(".api-error-state button").Click();

        panel.WaitForAssertion(() =>
        {
            Assert.True(requestedUris.Count > before);
            Assert.NotNull(panel.Find(".replenishment-table"));
        });
    }

    [Fact]
    public void A_forbidden_board_explains_the_missing_permission()
    {
        respond = _ => Json(HttpStatusCode.Forbidden, """{"title":"Forbidden","detail":"No access"}""");

        var panel = RenderPanel();

        Assert.Contains(
            "You cannot view the replenishment board",
            panel.Markup,
            StringComparison.Ordinal);
        Assert.Empty(panel.FindAll(".api-error-state"));
    }

    [Fact]
    public void Refreshing_asks_the_api_again()
    {
        var panel = RenderPanel();
        var before = requestedUris.Count;

        panel.Find(".maintenance-reset").Click();

        panel.WaitForAssertion(() => Assert.True(requestedUris.Count > before));
    }

    [Fact]
    public void Changing_the_page_size_reloads_from_the_first_page()
    {
        respond = _ => Json(HttpStatusCode.OK, Page(1, 50, 1, Row()));

        var panel = RenderPanel();

        panel.Find("#replenishment-page-size").Change("50");

        panel.WaitForAssertion(() =>
        {
            var query = requestedUris.Last().Query;

            Assert.Contains("pageSize=50", query, StringComparison.Ordinal);
            Assert.Contains("pageNumber=1", query, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Paging_forward_asks_for_the_next_page()
    {
        respond = _ => Json(HttpStatusCode.OK, Page(1, 25, 60, Row()));

        var panel = RenderPanel();

        panel.FindAll(".maintenance-pages button")[1].Click();

        panel.WaitForAssertion(() => Assert.Contains(
            "pageNumber=2",
            requestedUris.Last().Query,
            StringComparison.Ordinal));
    }

    [Fact]
    public void The_first_page_cannot_go_back()
    {
        var panel = RenderPanel();

        Assert.True(panel.FindAll(".maintenance-pages button")[0].HasAttribute("disabled"));
    }

    [Fact]
    public void Ordering_a_short_part_carries_it_to_the_purchase_order_form()
    {
        ReplenishmentOrderIntent? intent = null;
        var panel = RenderPanel(parameters => parameters
            .Add(component => component.OnOrderRequested, value => intent = value));

        panel.Find(".replenishment-order").Click();

        Assert.NotNull(intent);
        Assert.Equal(Guid.Parse(FilterId), intent.InventoryItemId);
        Assert.Equal("FILTER-001", intent.PartNumber);
        Assert.Equal(5m, intent.Quantity);
        Assert.Equal(Guid.Parse(SupplierId), intent.SupplierId);
    }

    [Fact]
    public void Every_row_offers_ordering()
    {
        respond = _ => Json(
            HttpStatusCode.OK,
            ReplenishmentPage(Row(), Row(name: "Gasket", partNumber: "PN-2")));

        var panel = RenderPanel();

        Assert.Equal(2, panel.FindAll(".replenishment-order").Count);
    }

    [Fact]
    public void A_row_names_its_supplier_without_leaking_identifiers()
    {
        var panel = RenderPanel();

        var row = panel.Find(".replenishment-table tbody tr");

        Assert.Equal("Baltic Marine Parts", row.QuerySelectorAll("td")[2].TextContent.Trim());
        Assert.False(row.HasAttribute("title"));
        Assert.All(
            row.QuerySelectorAll("td"),
            cell => Assert.False(cell.HasAttribute("title")));
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

    private IRenderedComponent<ReplenishmentPanel> RenderPanel(params string[] permissions)
    {
        return RenderPanel(null, permissions);
    }

    private IRenderedComponent<ReplenishmentPanel> RenderPanel(
        Action<ComponentParameterCollectionBuilder<ReplenishmentPanel>>? parameterBuilder,
        params string[] permissions)
    {
        var httpClient = new HttpClient(new StubHttpMessageHandler(async (request, token) =>
        {
            requestedUris.Add(request.RequestUri!);

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

        var panel = parameterBuilder is null
            ? Render<ReplenishmentPanel>()
            : Render<ReplenishmentPanel>(parameterBuilder);
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

    private static string Row(
        string name = "Main engine filter",
        string partNumber = "FILTER-001",
        string stockOnHand = "8",
        string reservedQuantity = "3",
        string availableQuantity = "5",
        string reorderLevel = "10",
        string shortageQuantity = "5",
        string openPurchaseOrderQuantity = "0",
        string classification = "Low")
    {
        return $$"""
            {"inventoryItemId":"{{FilterId}}","name":"{{name}}","partNumber":"{{partNumber}}",
            "supplierId":"{{SupplierId}}","supplierName":"Baltic Marine Parts",
            "stockOnHand":{{stockOnHand}},"reservedQuantity":{{reservedQuantity}},
            "availableQuantity":{{availableQuantity}},"reorderLevel":{{reorderLevel}},
            "shortageQuantity":{{shortageQuantity}},
            "openPurchaseOrderQuantity":{{openPurchaseOrderQuantity}},
            "classification":"{{classification}}"}
            """;
    }

    private static string ReplenishmentPage(params string[] items)
    {
        return Page(1, 25, items.Length, items);
    }

    private static string Page(
        int pageNumber,
        int pageSize,
        int totalCount,
        params string[] items)
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
