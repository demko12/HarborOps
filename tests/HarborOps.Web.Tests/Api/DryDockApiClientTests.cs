using System.Net;
using System.Text;
using HarborOps.Web.Api.Maintenance;

namespace HarborOps.Web.Tests.Api;

public sealed class DryDockApiClientTests
{
    private static readonly Guid DryDockId = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid VesselId = Guid.Parse("20000000-0000-0000-0000-000000000002");
    private static readonly Guid PortId = Guid.Parse("30000000-0000-0000-0000-000000000003");

    private const string PageJson = """
        {"items":[{"dryDockId":"10000000-0000-0000-0000-000000000001",
        "vesselId":"20000000-0000-0000-0000-000000000002",
        "portId":"30000000-0000-0000-0000-000000000003",
        "startDate":"2026-09-01","endDate":null,"plannedTasks":"Hull coating",
        "actualCost":null,"status":"Planned","isOverdue":false,"daysUntilStart":12}],
        "pageNumber":1,"pageSize":25,"totalCount":1}
        """;

    private const string DetailsJson = """
        {"dryDockId":"10000000-0000-0000-0000-000000000001",
        "vesselId":"20000000-0000-0000-0000-000000000002",
        "portId":"30000000-0000-0000-0000-000000000003",
        "startDate":"2026-09-01","endDate":null,"plannedTasks":"Hull coating",
        "actualCost":null,"status":"Planned",
        "allowedNextStatuses":["InProgress","Canceled"],
        "isOverdue":false,"daysUntilStart":12}
        """;

    private const string TransitionJson = """
        {"dryDockId":"10000000-0000-0000-0000-000000000001","status":"InProgress",
        "allowedNextStatuses":["Completed"],"endDate":null,"actualCost":null}
        """;

    [Fact]
    public async Task GetDryDocksAsync_sends_every_filter_and_the_page_to_the_endpoint()
    {
        HttpRequestMessage? captured = null;
        using var httpClient = CreateHttpClient((request, _) =>
        {
            captured = request;

            return Json(HttpStatusCode.OK, PageJson);
        });

        var filter = new DryDockFilter
        {
            VesselId = VesselId,
            PortId = PortId,
            Status = "Planned",
            StartFrom = new DateOnly(2026, 9, 1),
            StartTo = new DateOnly(2026, 12, 31),
            Overdue = true,
            Search = "  hull  ",
            PageNumber = 2,
            PageSize = 50,
        };

        var result = await CreateClient(httpClient).GetDryDocksAsync(filter, CancellationToken.None);
        var path = captured!.RequestUri!.PathAndQuery;

        Assert.True(result.IsSuccess);
        Assert.StartsWith("/api/maintenance/dry-docks?", path, StringComparison.Ordinal);
        Assert.Contains($"vesselId={VesselId}", path, StringComparison.Ordinal);
        Assert.Contains($"portId={PortId}", path, StringComparison.Ordinal);
        Assert.Contains("status=Planned", path, StringComparison.Ordinal);
        Assert.Contains("startFrom=2026-09-01", path, StringComparison.Ordinal);
        Assert.Contains("startTo=2026-12-31", path, StringComparison.Ordinal);
        Assert.Contains("overdue=true", path, StringComparison.Ordinal);
        Assert.Contains("search=hull", path, StringComparison.Ordinal);
        Assert.Contains("pageNumber=2", path, StringComparison.Ordinal);
        Assert.Contains("pageSize=50", path, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetDryDocksAsync_omits_the_filters_that_were_not_chosen()
    {
        HttpRequestMessage? captured = null;
        using var httpClient = CreateHttpClient((request, _) =>
        {
            captured = request;

            return Json(HttpStatusCode.OK, PageJson);
        });

        await CreateClient(httpClient).GetDryDocksAsync(new DryDockFilter(), CancellationToken.None);
        var path = captured!.RequestUri!.PathAndQuery;

        Assert.DoesNotContain("vesselId=", path, StringComparison.Ordinal);
        Assert.DoesNotContain("status=", path, StringComparison.Ordinal);
        Assert.DoesNotContain("overdue=", path, StringComparison.Ordinal);
        Assert.DoesNotContain("search=", path, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetDryDocksAsync_reads_the_board_row()
    {
        using var httpClient = CreateHttpClient((_, _) => Json(HttpStatusCode.OK, PageJson));

        var result = await CreateClient(httpClient).GetDryDocksAsync(
            new DryDockFilter(),
            CancellationToken.None);

        var row = Assert.Single(result.Value!.Items);

        Assert.Equal(DryDockId, row.DryDockId);
        Assert.Equal(VesselId, row.VesselId);
        Assert.Equal(PortId, row.PortId);
        Assert.Equal(new DateOnly(2026, 9, 1), row.StartDate);
        Assert.Equal("Hull coating", row.PlannedTasks);
        Assert.Equal("Planned", row.Status);
        Assert.False(row.IsOverdue);
        Assert.Equal(12, row.DaysUntilStart);
    }

    [Fact]
    public async Task GetDryDockDetailsAsync_reads_the_allowed_next_statuses()
    {
        HttpRequestMessage? captured = null;
        using var httpClient = CreateHttpClient((request, _) =>
        {
            captured = request;

            return Json(HttpStatusCode.OK, DetailsJson);
        });

        var result = await CreateClient(httpClient).GetDryDockDetailsAsync(
            DryDockId,
            CancellationToken.None);

        Assert.Equal(
            $"/api/maintenance/dry-docks/{DryDockId}",
            captured!.RequestUri!.AbsolutePath);
        Assert.Equal(["InProgress", "Canceled"], result.Value!.AllowedNextStatuses);
    }

    [Fact]
    public async Task GetDryDockDetailsAsync_reports_a_missing_dry_dock()
    {
        using var httpClient = CreateHttpClient((_, _) =>
            Json(HttpStatusCode.NotFound, """{"title":"Dry dock not found.","detail":"Gone."}"""));

        var result = await CreateClient(httpClient).GetDryDockDetailsAsync(
            DryDockId,
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.True(result.Failure!.IsNotFound);
    }

    [Fact]
    public async Task GetDryDockDetailsAsync_reports_a_forbidden_read()
    {
        using var httpClient = CreateHttpClient((_, _) =>
            Json(HttpStatusCode.Forbidden, "{}"));

        var result = await CreateClient(httpClient).GetDryDockDetailsAsync(
            DryDockId,
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.True(result.Failure!.IsForbidden);
    }

    [Fact]
    public async Task ScheduleDryDockAsync_posts_to_the_board_route()
    {
        HttpRequestMessage? captured = null;
        using var httpClient = CreateHttpClient((request, _) =>
        {
            captured = request;

            return Json(
                HttpStatusCode.Created,
                """{"id":"10000000-0000-0000-0000-000000000001"}""");
        });

        var result = await CreateClient(httpClient).ScheduleDryDockAsync(
            new ScheduleDryDockWebRequest(VesselId, PortId, new DateOnly(2026, 9, 1), "Hull coating"),
            CancellationToken.None);

        Assert.Equal(HttpMethod.Post, captured!.Method);
        Assert.Equal("/api/maintenance/dry-docks", captured.RequestUri!.AbsolutePath);
        Assert.Equal(DryDockId, result.Value!.Id);
    }

    [Fact]
    public async Task ScheduleDryDockAsync_surfaces_the_field_errors()
    {
        using var httpClient = CreateHttpClient((_, _) => Json(
            HttpStatusCode.BadRequest,
            """{"title":"One or more validation errors occurred.","errors":{"portId":["Select a port."]}}"""));

        var result = await CreateClient(httpClient).ScheduleDryDockAsync(
            new ScheduleDryDockWebRequest(VesselId, Guid.Empty, new DateOnly(2026, 9, 1), "Work"),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.True(result.Failure!.IsValidation);
        Assert.Equal(["Select a port."], result.Failure.Errors["portId"]);
    }

    [Theory]
    [InlineData("start")]
    [InlineData("cancel")]
    public async Task A_transition_without_a_body_posts_to_its_own_route(string action)
    {
        HttpRequestMessage? captured = null;
        using var httpClient = CreateHttpClient((request, _) =>
        {
            captured = request;

            return Json(HttpStatusCode.OK, TransitionJson);
        });

        var client = CreateClient(httpClient);
        var result = action == "start"
            ? await client.StartDryDockAsync(DryDockId, CancellationToken.None)
            : await client.CancelDryDockAsync(DryDockId, CancellationToken.None);

        Assert.Equal(HttpMethod.Post, captured!.Method);
        Assert.Equal(
            $"/api/maintenance/dry-docks/{DryDockId}/{action}",
            captured.RequestUri!.AbsolutePath);
        Assert.Equal("InProgress", result.Value!.Status);
        Assert.Equal(["Completed"], result.Value.AllowedNextStatuses);
    }

    [Fact]
    public async Task CompleteDryDockAsync_posts_the_end_date_and_cost()
    {
        HttpRequestMessage? captured = null;
        string? body = null;
        using var httpClient = CreateHttpClient(async (request, token) =>
        {
            captured = request;
            body = await request.Content!.ReadAsStringAsync(token);

            return await Json(HttpStatusCode.OK, TransitionJson);
        });

        await CreateClient(httpClient).CompleteDryDockAsync(
            DryDockId,
            new CompleteDryDockWebRequest(new DateOnly(2026, 9, 20), 91_250.25m),
            CancellationToken.None);

        Assert.Equal(
            $"/api/maintenance/dry-docks/{DryDockId}/complete",
            captured!.RequestUri!.AbsolutePath);
        Assert.Contains("2026-09-20", body!, StringComparison.Ordinal);
        Assert.Contains("91250.25", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_rejected_transition_is_reported_as_a_conflict()
    {
        using var httpClient = CreateHttpClient((_, _) => Json(
            HttpStatusCode.Conflict,
            """{"title":"Dry dock status transition is not allowed.","detail":"Already started."}"""));

        var result = await CreateClient(httpClient).StartDryDockAsync(
            DryDockId,
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.True(result.Failure!.IsConflict);
        Assert.Equal("Dry dock status transition is not allowed.", result.Failure.Title);
    }

    [Fact]
    public async Task An_unreachable_api_is_reported_without_throwing()
    {
        using var httpClient = CreateHttpClient((_, _) =>
            throw new HttpRequestException("boom"));

        var board = await CreateClient(httpClient).GetDryDocksAsync(
            new DryDockFilter(),
            CancellationToken.None);
        var details = await CreateClient(httpClient).GetDryDockDetailsAsync(
            DryDockId,
            CancellationToken.None);
        var start = await CreateClient(httpClient).StartDryDockAsync(
            DryDockId,
            CancellationToken.None);

        Assert.False(board.IsSuccess);
        Assert.False(details.IsSuccess);
        Assert.False(start.IsSuccess);
        Assert.Equal(0, start.Failure!.StatusCode);
    }

    private static MaintenanceApiClient CreateClient(HttpClient httpClient) => new(httpClient);

    private static HttpClient CreateHttpClient(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responseFactory) =>
        new(new StubHttpMessageHandler(responseFactory))
        {
            BaseAddress = new Uri("https://api.harborops.test/"),
        };

    private static Task<HttpResponseMessage> Json(HttpStatusCode statusCode, string json) =>
        Task.FromResult(new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/problem+json"),
        });

    private sealed class StubHttpMessageHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responseFactory)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => responseFactory(request, cancellationToken);
    }
}
