using System.Globalization;
using System.Net;
using System.Security.Claims;
using System.Text;
using Bunit;
using HarborOps.Web.Api;
using HarborOps.Web.Api.Fleet;
using HarborOps.Web.Api.Maintenance;
using HarborOps.Web.Components.Pages.Maintenance;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace HarborOps.Web.Tests.Components;

public sealed class MaintenancePreventiveWorkPanelTests : BunitContext
{
    private const string VesselId = "30000000-0000-0000-0000-000000000002";
    private const string ScheduleId = "d0000000-0000-0000-0000-00000000000d";
    private const string EquipmentId = "c0000000-0000-0000-0000-00000000000c";
    private const string SecondEquipmentId = "c0000000-0000-0000-0000-00000000000e";
    private const string OccurrenceId = "e0000000-0000-0000-0000-00000000000e";
    private const string WorkOrderId = "f0000000-0000-0000-0000-00000000000f";
    private const string ReplacementWorkOrderId = "f0000000-0000-0000-0000-0000000000f2";

    private const string ForecastPath = "/api/maintenance/preventive-work/forecast";
    private const string PreviewPath = "/api/maintenance/preventive-work/generate/preview";
    private const string GeneratePath = "/api/maintenance/preventive-work/generate";
    private const string ReplacementPath = $"/api/maintenance/preventive-work/occurrences/{OccurrenceId}/replacement";

    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

    private readonly List<Uri> requestedUris = [];
    private readonly List<string> requestBodies = [];

    private Func<HttpRequestMessage, Task<HttpResponseMessage>> respond =
        _ => Json(HttpStatusCode.OK, ForecastPage(Occurrence()));

    private Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>? respondWithToken;

    private int ForecastRequests => requestedUris.Count(uri => uri.AbsolutePath == ForecastPath);

    [Fact]
    public void The_forecast_is_requested_for_the_next_thirty_days_by_default()
    {
        RenderPanel();

        var query = LastForecastQuery();

        Assert.Contains($"from={Iso(Today)}", query, StringComparison.Ordinal);
        Assert.Contains($"to={Iso(Today.AddDays(30))}", query, StringComparison.Ordinal);
        Assert.Contains("pageNumber=1", query, StringComparison.Ordinal);
        Assert.Contains("pageSize=25", query, StringComparison.Ordinal);
        Assert.DoesNotContain("state=", query, StringComparison.Ordinal);
    }

    [Fact]
    public void An_occurrence_is_listed_with_its_vessel_scope_and_state()
    {
        var panel = RenderPanel();

        var cells = panel.WaitForElement(".preventive-row").QuerySelectorAll("td");

        Assert.Equal("2026-09-20", cells[0].TextContent.Trim());
        Assert.Equal("Due by 2026-09-20 23:59 UTC", cells[0].GetAttribute("title"));
        Assert.Equal("Main engine overhaul", cells[1].TextContent.Trim());
        Assert.Equal("Monthly", cells[2].TextContent.Trim());
        Assert.Equal("Aurora Borealis", cells[3].TextContent.Trim());
        Assert.Equal("2", cells[4].TextContent.Trim());
        Assert.Equal("Not generated", cells[5].QuerySelector(".preventive-state")!.TextContent.Trim());
        Assert.Equal(MaintenanceFormat.MissingValue, cells[6].TextContent.Trim());
    }

    [Fact]
    public void A_generated_overdue_occurrence_links_its_work_order_and_says_overdue_in_text()
    {
        respond = _ => Json(
            HttpStatusCode.OK,
            ForecastPage(Occurrence(state: "Generated", occurrenceId: OccurrenceId, workOrderId: WorkOrderId, isOverdue: true)));

        var panel = RenderPanel();

        var row = panel.WaitForElement(".preventive-row");

        Assert.Contains("maintenance-overdue-row", row.ClassName, StringComparison.Ordinal);
        Assert.Equal("Overdue", row.QuerySelector(".schedules-due-badge")!.TextContent.Trim());
        Assert.Contains("generated", row.QuerySelector(".preventive-state")!.ClassName, StringComparison.Ordinal);
        Assert.Equal($"/maintenance/work-orders/{WorkOrderId}", row.QuerySelector("a")!.GetAttribute("href"));
    }

    [Fact]
    public void The_summary_shows_the_window_totals_reported_by_the_api()
    {
        respond = _ => Json(HttpStatusCode.OK, Page(1, 25, 7, 4, 2, 1, Occurrence()));

        var panel = RenderPanel();

        panel.WaitForAssertion(() =>
        {
            var totals = panel.FindAll(".maintenance-summary-card strong");

            Assert.Equal("7", totals[0].TextContent.Trim());
            Assert.Equal("4", totals[1].TextContent.Trim());
            Assert.Equal("2", totals[2].TextContent.Trim());
            Assert.Equal("1", totals[3].TextContent.Trim());
        });
        Assert.Contains(Iso(Today), panel.Find(".maintenance-summary-card small").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void Filtering_by_state_sends_the_state_to_the_api()
    {
        var panel = RenderPanel();

        panel.Find("#preventive-state").Change("Generated");

        panel.WaitForAssertion(() => Assert.Contains("state=Generated", LastForecastQuery(), StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("next-90", 0, 90)]
    [InlineData("overdue", -365, -1)]
    public void Choosing_a_due_window_requests_its_dates(string preset, int fromOffset, int toOffset)
    {
        var panel = RenderPanel();

        panel.Find("#preventive-window").Change(preset);

        panel.WaitForAssertion(() =>
        {
            var query = LastForecastQuery();

            Assert.Contains($"from={Iso(Today.AddDays(fromOffset))}", query, StringComparison.Ordinal);
            Assert.Contains($"to={Iso(Today.AddDays(toOffset))}", query, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Editing_a_window_date_sends_the_custom_window()
    {
        var panel = RenderPanel();

        panel.Find("#preventive-from").Change("2026-11-01");

        panel.WaitForAssertion(() => Assert.Contains("from=2026-11-01", LastForecastQuery(), StringComparison.Ordinal));
    }

    [Fact]
    public void Resetting_restores_the_default_window_and_clears_the_state()
    {
        var panel = RenderPanel();

        panel.Find("#preventive-state").Change("Cancelled");
        panel.WaitForAssertion(() => Assert.Contains("state=Cancelled", LastForecastQuery(), StringComparison.Ordinal));

        panel.Find(".maintenance-filters .maintenance-reset").Click();

        panel.WaitForAssertion(() =>
        {
            var query = LastForecastQuery();

            Assert.DoesNotContain("state=", query, StringComparison.Ordinal);
            Assert.Contains($"to={Iso(Today.AddDays(30))}", query, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Paging_forward_asks_for_the_next_page()
    {
        respond = _ => Json(HttpStatusCode.OK, Page(1, 25, 60, 60, 0, 0, Occurrence()));

        var panel = RenderPanel();

        panel.WaitForElement(".maintenance-pages");
        panel.FindAll(".maintenance-pages button")[1].Click();

        panel.WaitForAssertion(() => Assert.Contains("pageNumber=2", LastForecastQuery(), StringComparison.Ordinal));
    }

    [Fact]
    public void An_empty_window_explains_what_appears_here()
    {
        respond = _ => Json(HttpStatusCode.OK, ForecastPage());

        var panel = RenderPanel();

        panel.WaitForAssertion(() => Assert.Contains(
            "No preventive work is due in this window",
            panel.Markup,
            StringComparison.Ordinal));
        Assert.Empty(panel.FindAll(".preventive-table"));
    }

    [Fact]
    public void A_rejected_window_shows_the_validation_errors_next_to_the_filters()
    {
        respond = _ => Json(
            HttpStatusCode.BadRequest,
            """{"title":"One or more validation errors occurred.","errors":{"to":["The forecast window must not exceed 366 days."]}}""");

        var panel = RenderPanel();

        panel.WaitForAssertion(() =>
        {
            var errors = panel.Find(".preventive-filter-errors").TextContent;

            Assert.Contains("Adjust the due window", errors, StringComparison.Ordinal);
            Assert.Contains("To", errors, StringComparison.Ordinal);
            Assert.Contains("must not exceed 366 days", errors, StringComparison.Ordinal);
        });
        Assert.Empty(panel.FindAll(".api-error-state"));
    }

    [Fact]
    public void A_forbidden_forecast_explains_the_missing_permission()
    {
        respond = _ => Json(HttpStatusCode.Forbidden, """{"title":"Forbidden","detail":"No access"}""");

        var panel = RenderPanel();

        panel.WaitForAssertion(() => Assert.Contains(
            "You cannot view preventive work",
            panel.Markup,
            StringComparison.Ordinal));
        Assert.Empty(panel.FindAll(".api-error-state"));
    }

    [Fact]
    public void A_failed_load_offers_a_retry_that_asks_again()
    {
        respond = _ => Json(HttpStatusCode.InternalServerError, """{"title":"Server error","detail":"Broken"}""");

        var panel = RenderPanel();
        panel.WaitForElement(".api-error-state");
        var before = ForecastRequests;

        respond = _ => Json(HttpStatusCode.OK, ForecastPage(Occurrence()));
        panel.Find(".api-error-state button").Click();

        panel.WaitForAssertion(() =>
        {
            Assert.True(ForecastRequests > before);
            Assert.NotNull(panel.Find(".preventive-table"));
        });
    }

    [Fact]
    public void Readers_see_the_forecast_without_generation_or_replacement()
    {
        respond = _ => Json(
            HttpStatusCode.OK,
            ForecastPage(Occurrence(state: "Cancelled", occurrenceId: OccurrenceId, workOrderId: WorkOrderId)));

        var panel = RenderPanel("maintenance.read");

        panel.WaitForElement(".preventive-row");

        Assert.Empty(panel.FindAll(".preventive-generate-toggle"));
        Assert.Empty(panel.FindAll(".preventive-replace"));
    }

    [Fact]
    public void Previewing_posts_the_request_and_shows_what_will_be_created_and_skipped()
    {
        respond = GenerationFlow;

        var panel = RenderPanel();

        OpenPreview(panel);

        var totals = panel.FindAll(".preventive-preview .preventive-total strong");
        var outcomes = panel.FindAll(".preventive-preview-table .preventive-state");

        Assert.Equal("1", totals[0].TextContent.Trim());
        Assert.Equal("1", totals[1].TextContent.Trim());
        Assert.Equal("Will be created", outcomes[0].TextContent.Trim());
        Assert.Equal("Skipped: generated", outcomes[1].TextContent.Trim());
        Assert.Equal("Generate 1 work order", panel.Find(".preventive-confirm").TextContent.Trim());
        Assert.Contains(
            requestBodies,
            body => body.Contains("\"through\":\"2026-10-10\"", StringComparison.Ordinal)
                && body.Contains("\"priority\":\"Medium\"", StringComparison.Ordinal));
        Assert.Contains(requestedUris, uri => uri.AbsolutePath == PreviewPath);
    }

    [Fact]
    public void Confirming_generates_with_the_previewed_request_and_shows_the_totals()
    {
        respond = GenerationFlow;

        var panel = RenderPanel();
        OpenPreview(panel);
        var previewBody = requestBodies.Last();
        var forecastsBefore = ForecastRequests;

        panel.Find(".preventive-confirm").Click();

        panel.WaitForAssertion(() =>
        {
            var totals = panel.FindAll(".preventive-result .preventive-total strong");

            Assert.Equal("1", totals[0].TextContent.Trim());
            Assert.Equal("0", totals[1].TextContent.Trim());
            Assert.Equal(
                $"/maintenance/work-orders/{WorkOrderId}",
                panel.Find(".preventive-result-table a").GetAttribute("href"));
            Assert.True(ForecastRequests > forecastsBefore);
        });
        Assert.Contains(requestedUris, uri => uri.AbsolutePath == GeneratePath);
        Assert.Equal(previewBody, requestBodies.Last());
    }

    [Fact]
    public void Nothing_to_create_disables_the_confirmation()
    {
        respond = request => request.RequestUri!.AbsolutePath == PreviewPath
            ? Json(HttpStatusCode.OK, Preview(0, 1, Occurrence(state: "Generated", occurrenceId: OccurrenceId, workOrderId: WorkOrderId)))
            : Json(HttpStatusCode.OK, ForecastPage(Occurrence()));

        var panel = RenderPanel();

        OpenPreview(panel);

        Assert.True(panel.Find(".preventive-confirm").HasAttribute("disabled"));
    }

    [Fact]
    public void Changing_an_input_after_the_preview_requires_a_new_preview()
    {
        respond = GenerationFlow;

        var panel = RenderPanel();
        OpenPreview(panel);

        panel.Find("#preventive-priority").Change("High");

        panel.WaitForAssertion(() => Assert.Empty(panel.FindAll(".preventive-confirm")));
    }

    [Fact]
    public void A_rejected_preview_lists_the_validation_errors()
    {
        respond = request => request.RequestUri!.AbsolutePath == PreviewPath
            ? Json(
                HttpStatusCode.BadRequest,
                """{"title":"One or more validation errors occurred.","errors":{"through":["Must be today or later."]}}""")
            : Json(HttpStatusCode.OK, ForecastPage(Occurrence()));

        var panel = RenderPanel();

        panel.Find(".preventive-generate-toggle").Click();
        panel.Find(".preventive-generate-form").Submit();

        panel.WaitForAssertion(() =>
        {
            var errors = panel.Find(".preventive-generate-errors").TextContent;

            Assert.Contains("Through date", errors, StringComparison.Ordinal);
            Assert.Contains("Must be today or later.", errors, StringComparison.Ordinal);
        });
        Assert.Empty(panel.FindAll(".preventive-confirm"));
    }

    [Fact]
    public void A_conflicting_generation_asks_for_a_new_preview()
    {
        respond = request => request.RequestUri!.AbsolutePath switch
        {
            PreviewPath => Json(HttpStatusCode.OK, Preview(1, 0, Occurrence())),
            GeneratePath => Json(
                HttpStatusCode.Conflict,
                """{"title":"Preventive work generation conflicted with a concurrent change.","detail":"The due work changed."}"""),
            _ => Json(HttpStatusCode.OK, ForecastPage(Occurrence())),
        };

        var panel = RenderPanel();
        OpenPreview(panel);

        panel.Find(".preventive-confirm").Click();

        panel.WaitForAssertion(() =>
        {
            Assert.Contains(
                "Preview again to see the current state",
                panel.Find(".preventive-generate-error").TextContent,
                StringComparison.Ordinal);
            Assert.NotNull(panel.Find(".preventive-retry"));
            Assert.Empty(panel.FindAll(".preventive-confirm"));
        });
    }

    [Fact]
    public void Replacing_a_cancelled_occurrence_creates_the_work_order_and_refreshes_the_forecast()
    {
        respond = request => request.RequestUri!.AbsolutePath == ReplacementPath
            ? Json(HttpStatusCode.Created, Replacement())
            : Json(
                HttpStatusCode.OK,
                ForecastPage(Occurrence(state: "Cancelled", occurrenceId: OccurrenceId, workOrderId: WorkOrderId)));

        var panel = RenderPanel();
        panel.WaitForElement(".preventive-replace").Click();
        var forecastsBefore = ForecastRequests;

        panel.Find(".preventive-replace-confirm").Click();

        panel.WaitForAssertion(() =>
        {
            var toast = panel.Find(".preventive-toast");

            Assert.Contains("A replacement work order was created.", toast.TextContent, StringComparison.Ordinal);
            Assert.Equal(
                $"/maintenance/work-orders/{ReplacementWorkOrderId}",
                toast.QuerySelector("a")!.GetAttribute("href"));
            Assert.Empty(panel.FindAll(".preventive-replace-card"));
            Assert.True(ForecastRequests > forecastsBefore);
        });
        Assert.Contains(requestedUris, uri => uri.AbsolutePath == ReplacementPath);
    }

    [Fact]
    public void A_conflicting_replacement_keeps_the_dialog_open_and_refreshes()
    {
        respond = request => request.RequestUri!.AbsolutePath == ReplacementPath
            ? Json(
                HttpStatusCode.Conflict,
                """{"title":"Schedule occurrence cannot be replaced from its current state.","detail":"Already replaced."}""")
            : Json(
                HttpStatusCode.OK,
                ForecastPage(Occurrence(state: "Cancelled", occurrenceId: OccurrenceId, workOrderId: WorkOrderId)));

        var panel = RenderPanel();
        panel.WaitForElement(".preventive-replace").Click();
        var forecastsBefore = ForecastRequests;

        panel.Find(".preventive-replace-confirm").Click();

        panel.WaitForAssertion(() =>
        {
            Assert.Contains(
                "Already replaced.",
                panel.Find(".preventive-replace-error").TextContent,
                StringComparison.Ordinal);
            Assert.True(ForecastRequests > forecastsBefore);
        });
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

    private static void OpenPreview(IRenderedComponent<PreventiveWorkPanel> panel)
    {
        panel.Find(".preventive-generate-toggle").Click();
        panel.Find("#preventive-through").Change("2026-10-10");
        panel.Find(".preventive-generate-form").Submit();
        panel.WaitForElement(".preventive-preview", TimeSpan.FromSeconds(5));
    }

    private static Task<HttpResponseMessage> GenerationFlow(HttpRequestMessage request)
    {
        return request.RequestUri!.AbsolutePath switch
        {
            PreviewPath => Json(
                HttpStatusCode.OK,
                Preview(
                    1,
                    1,
                    Occurrence(),
                    Occurrence(state: "Generated", occurrenceId: OccurrenceId, workOrderId: WorkOrderId, dueDate: "2026-09-22"))),
            GeneratePath => Json(HttpStatusCode.Created, Generation()),
            _ => Json(HttpStatusCode.OK, ForecastPage(Occurrence())),
        };
    }

    private string LastForecastQuery()
    {
        return requestedUris.Last(uri => uri.AbsolutePath == ForecastPath).Query;
    }

    private IRenderedComponent<PreventiveWorkPanel> RenderPanel(params string[] permissions)
    {
        var maintenanceHttpClient = new HttpClient(new StubHttpMessageHandler(async (request, token) =>
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

        var fleetHttpClient = new HttpClient(new StubHttpMessageHandler((request, _) =>
            IsVesselLookup(request)
                ? Json(HttpStatusCode.OK, $$"""{"vessels":[{"id":"{{VesselId}}","name":"Aurora Borealis"}]}""")
                : Json(HttpStatusCode.OK, """{"items":[],"pageNumber":1,"pageSize":10,"totalCount":0}""")))
        {
            BaseAddress = new Uri("https://api.harborops.test/"),
        };

        Services.AddSingleton(new MaintenanceApiClient(maintenanceHttpClient));
        Services.AddSingleton(new FleetApiClient(fleetHttpClient));
        Services.AddSingleton(CreateAuthorizationState(
            permissions.Length == 0 ? ["maintenance.manage"] : permissions));

        var panel = Render<PreventiveWorkPanel>();
        panel.WaitForAssertion(() => Assert.Contains(requestedUris, uri => uri.AbsolutePath == ForecastPath));

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

    private static string Iso(DateOnly date)
    {
        return date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    private static string Quoted(string? value)
    {
        return value is null ? "null" : $"\"{value}\"";
    }

    private static string Occurrence(
        string state = "Pending",
        string? occurrenceId = null,
        string? workOrderId = null,
        string dueDate = "2026-09-20",
        bool isOverdue = false)
    {
        return $$"""
            {"scheduleId":"{{ScheduleId}}","occurrenceId":{{Quoted(occurrenceId)}},"vesselId":"{{VesselId}}",
            "maintenanceType":"Main engine overhaul","frequency":"Monthly","dueDate":"{{dueDate}}",
            "dueUtc":"{{dueDate}}T23:59:59Z","state":"{{state}}","isOverdue":{{(isOverdue ? "true" : "false")}},
            "workOrderId":{{Quoted(workOrderId)}},"equipmentIds":["{{EquipmentId}}","{{SecondEquipmentId}}"]}
            """;
    }

    private static string ForecastPage(params string[] items)
    {
        return Page(1, 25, items.Length, items.Length, 0, 0, items);
    }

    private static string Page(
        int pageNumber,
        int pageSize,
        int totalCount,
        int pendingCount,
        int generatedCount,
        int overdueCount,
        params string[] items)
    {
        return $$"""
            {"items":[{{string.Join(',', items)}}],"pageNumber":{{pageNumber}},"pageSize":{{pageSize}},
            "totalCount":{{totalCount}},"pendingCount":{{pendingCount}},"generatedCount":{{generatedCount}},
            "overdueCount":{{overdueCount}}}
            """;
    }

    private static string Preview(int eligibleCount, int skippedCount, params string[] items)
    {
        return $$"""
            {"items":[{{string.Join(',', items)}}],"through":"2026-10-10","eligibleCount":{{eligibleCount}},
            "skippedCount":{{skippedCount}},"previewedUtc":"2026-09-10T09:00:00Z"}
            """;
    }

    private static string Generation()
    {
        return $$"""
            {"results":[{"scheduleId":"{{ScheduleId}}","occurrenceId":"{{OccurrenceId}}","vesselId":"{{VesselId}}",
            "maintenanceType":"Main engine overhaul","dueDate":"2026-09-20","dueUtc":"2026-09-20T23:59:59Z",
            "state":"Generated","outcome":"Created","workOrderId":"{{WorkOrderId}}","equipmentIds":["{{EquipmentId}}"]}],
            "through":"2026-10-10","createdCount":1,"skippedCount":0,"generatedUtc":"2026-09-10T09:00:00Z"}
            """;
    }

    private static string Replacement()
    {
        return $$"""
            {"occurrenceId":"{{OccurrenceId}}","scheduleId":"{{ScheduleId}}","vesselId":"{{VesselId}}",
            "dueDate":"2026-09-20","dueUtc":"2026-09-20T23:59:59Z","state":"Generated",
            "workOrderId":"{{ReplacementWorkOrderId}}","replacedWorkOrderId":"{{WorkOrderId}}",
            "equipmentIds":["{{EquipmentId}}"],"generatedUtc":"2026-09-10T09:00:00Z"}
            """;
    }

    private static bool IsVesselLookup(HttpRequestMessage request)
    {
        return request.RequestUri!.AbsolutePath.EndsWith("/lookup", StringComparison.Ordinal);
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
