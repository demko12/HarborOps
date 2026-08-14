using System.Net;
using System.Text;
using Bunit;
using HarborOps.Web.Api.Fleet;
using HarborOps.Web.Api.Maintenance;
using HarborOps.Web.Components.Pages.Maintenance;
using Microsoft.Extensions.DependencyInjection;

namespace HarborOps.Web.Tests.Components;

public sealed class MaintenancePageTests : BunitContext
{
    private const string VesselId = "30000000-0000-0000-0000-000000000002";

    private readonly List<Uri> requestedUris = [];

    private Func<HttpRequestMessage, Task<HttpResponseMessage>> respondToMaintenance =
        _ => Json(HttpStatusCode.OK, Page());

    private Func<HttpRequestMessage, Task<HttpResponseMessage>> respondToLookup =
        _ => Json(HttpStatusCode.OK, $$"""{"vessels":[{"id":"{{VesselId}}","name":"Aurora Borealis"}]}""");

    [Fact]
    public void Overdue_open_rows_are_marked_with_text_not_colour_alone()
    {
        respondToMaintenance = _ => Json(HttpStatusCode.OK, Page(
            Row("00000000-0000-0000-0000-000000000001", dueUtc: "2020-01-01T00:00:00Z", status: "Open")));

        var page = RenderMaintenancePage();

        Assert.Single(page.FindAll(".maintenance-overdue-row"));
        Assert.Contains("Overdue", page.Find(".maintenance-overdue-badge").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void Critical_priority_shows_a_warning_icon()
    {
        respondToMaintenance = _ => Json(HttpStatusCode.OK, Page(
            Row("00000000-0000-0000-0000-000000000001", priority: "Critical")));

        var page = RenderMaintenancePage();

        Assert.Single(page.FindAll(".maintenance-priority.priority-critical .bi-exclamation-triangle-fill"));
    }

    [Fact]
    public void Vessel_names_are_resolved_through_the_fleet_lookup()
    {
        respondToMaintenance = _ => Json(HttpStatusCode.OK, Page(
            Row("00000000-0000-0000-0000-000000000001")));

        var page = RenderMaintenancePage();

        Assert.Contains("Aurora Borealis", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void A_vessel_with_no_lookup_match_falls_back_to_a_short_id()
    {
        respondToLookup = _ => Json(HttpStatusCode.OK, """{"vessels":[]}""");
        respondToMaintenance = _ => Json(HttpStatusCode.OK, Page(
            Row("00000000-0000-0000-0000-000000000001")));

        var page = RenderMaintenancePage();

        Assert.Contains("Vessel 30000000", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Only_open_and_in_progress_rows_show_a_complete_button()
    {
        respondToMaintenance = _ => Json(HttpStatusCode.OK, Page(
            Row("00000000-0000-0000-0000-000000000001", status: "Open"),
            Row("00000000-0000-0000-0000-000000000002", status: "Completed")));

        var page = RenderMaintenancePage();

        Assert.Single(page.FindAll(".maintenance-complete-button"));
    }

    [Fact]
    public void Changing_the_status_and_priority_filters_sends_them_to_the_api()
    {
        var page = RenderMaintenancePage();

        page.Find("#maintenance-status").Change("Open");
        page.Find("#maintenance-priority").Change("Critical");

        page.WaitForAssertion(() =>
        {
            var lastQuery = requestedUris.Last(uri => uri.AbsolutePath.Contains("work-orders")).Query;

            Assert.Contains("status=Open", lastQuery, StringComparison.Ordinal);
            Assert.Contains("priority=Critical", lastQuery, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Changing_the_overdue_filter_sends_it_to_the_api()
    {
        var page = RenderMaintenancePage();

        page.Find("#maintenance-overdue").Change("true");

        page.WaitForAssertion(() =>
            Assert.Contains(
                "overdue=true",
                requestedUris.Last(uri =>
                    uri.AbsolutePath.Contains("work-orders")).Query,
                StringComparison.Ordinal));
    }

    [Fact]
    public void Clearing_filters_returns_to_an_unfiltered_first_page()
    {
        var page = RenderMaintenancePage();

        page.Find("#maintenance-status").Change("Open");
        page.WaitForAssertion(() => Assert.True(requestedUris.Count(uri => uri.AbsolutePath.Contains("work-orders")) >= 2));

        page.Find(".maintenance-reset").Click();

        page.WaitForAssertion(() =>
        {
            var lastQuery = requestedUris.Last(uri => uri.AbsolutePath.Contains("work-orders")).Query;

            Assert.DoesNotContain("status=", lastQuery, StringComparison.Ordinal);
            Assert.Contains("pageNumber=1", lastQuery, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Empty_data_shows_the_empty_state_instead_of_a_table()
    {
        respondToMaintenance = _ => Json(HttpStatusCode.OK, Page());

        var page = RenderMaintenancePage();

        Assert.Empty(page.FindAll("table.maintenance-table"));
        Assert.Contains("No work orders match your filters", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void An_unreachable_api_shows_the_error_state_with_a_retry_action()
    {
        respondToMaintenance = _ => throw new HttpRequestException("Connection failed.");

        var page = RenderMaintenancePage();

        Assert.Contains("HarborOps API is unavailable", page.Markup, StringComparison.Ordinal);
        Assert.Empty(page.FindAll("table.maintenance-table"));
        Assert.NotNull(page.Find(".api-error-state button"));
    }

    [Fact]
    public void Retry_reloads_the_board_after_a_failure()
    {
        var attempt = 0;
        respondToMaintenance = _ =>
        {
            attempt++;

            return attempt == 1
                ? throw new HttpRequestException("Connection failed.")
                : Json(HttpStatusCode.OK, Page(Row("00000000-0000-0000-0000-000000000001", title: "Replace pump")));
        };

        var page = RenderMaintenancePage();
        Assert.Contains("HarborOps API is unavailable", page.Markup, StringComparison.Ordinal);

        page.Find(".api-error-state button").Click();

        page.WaitForAssertion(() =>
            Assert.Contains("Replace pump", page.Markup, StringComparison.Ordinal));
    }

    [Fact]
    public void Creating_a_work_order_shows_a_success_message_and_refreshes_the_board()
    {
        var maintenanceCalls = 0;
        respondToMaintenance = request =>
        {
            if (request.Method == HttpMethod.Post)
            {
                return Json(HttpStatusCode.Created, """{"id":"60000000-0000-0000-0000-000000000006"}""");
            }

            maintenanceCalls++;

            return Json(HttpStatusCode.OK, Page());
        };

        var page = RenderMaintenancePage();

        page.Find("#create-vessel").Change(VesselId);
        page.Find("#create-title").Change("Replace filter");
        page.Find("#create-priority").Change("High");
        page.Find("#create-due").Change("2026-12-01T10:00");
        page.Find("form.maintenance-form").Submit();

        page.WaitForAssertion(() =>
        {
            Assert.Contains("Work order created.", page.Markup, StringComparison.Ordinal);
            Assert.True(maintenanceCalls >= 2);
        });
    }

    [Fact]
    public void Create_validation_errors_are_shown_and_the_form_keeps_its_values()
    {
        respondToMaintenance = request => request.Method == HttpMethod.Post
            ? Json(
                HttpStatusCode.BadRequest,
                """{"title":"One or more validation errors occurred.","errors":{"title":["Title is required."]}}""")
            : Json(HttpStatusCode.OK, Page());

        var page = RenderMaintenancePage();

        page.Find("#create-vessel").Change(VesselId);
        page.Find("form.maintenance-form").Submit();

        page.WaitForAssertion(() =>
            Assert.Contains("Title is required.", page.Find(".maintenance-field-errors").TextContent, StringComparison.Ordinal));

        Assert.Equal(VesselId, page.Find("#create-vessel").GetAttribute("value"));
    }

    [Fact]
    public void Completing_a_work_order_shows_a_success_message_and_refreshes_the_board()
    {
        respondToMaintenance = request =>
        {
            if (request.RequestUri!.AbsolutePath.Contains(
                    "/completion",
                    StringComparison.Ordinal))
            {
                return Json(
                    HttpStatusCode.OK,
                    """
                {
                    "workOrderId": "00000000-0000-0000-0000-000000000001",
                    "maintenanceRecordId": "70000000-0000-0000-0000-000000000007",
                    "completedUtc": "2026-08-12T09:00:00Z"
                }
                """);
            }

            return Json(
                HttpStatusCode.OK,
                Page(
                    Row(
                        "00000000-0000-0000-0000-000000000001",
                        status: "Open")));
        };

        var page = RenderMaintenancePage();

        page.Find(".maintenance-complete-button").Click();

        page.Find("#complete-technician")
            .Change(Guid.NewGuid().ToString());

        page.Find("#complete-completed-utc")
            .Change("2026-08-12T09:00");

        page.Find("section[aria-labelledby='maintenance-complete-title'] form.maintenance-form").Submit();

        page.WaitForAssertion(() =>
        {
            var completionRequests = requestedUris
                .Where(uri =>
                    uri.AbsolutePath.Contains(
                        "/completion",
                        StringComparison.Ordinal))
                .ToList();

            Assert.NotEmpty(completionRequests);

            Assert.Contains(
                "Work order completed.",
                page.Markup,
                StringComparison.Ordinal);
        });
    }

    [Fact]
    public void A_conflict_response_shows_a_message_and_closes_the_completion_form()
    {
        respondToMaintenance = request =>
        {
            if (request.RequestUri!.AbsolutePath.Contains("/completion", StringComparison.Ordinal))
            {
                return Json(
                    HttpStatusCode.Conflict,
                    """{"title":"Maintenance work order cannot be completed from its current status.","detail":"This work order can no longer be completed."}""");
            }

            return Json(HttpStatusCode.OK, Page(Row("00000000-0000-0000-0000-000000000001", status: "Open")));
        };

        var page = RenderMaintenancePage();

        page.Find(".maintenance-complete-button").Click();
        page.Find("#complete-technician").Change(Guid.NewGuid().ToString());
        page.Find("#complete-completed-utc").Change("2026-08-12T09:00");
        page.Find(
            "section[aria-labelledby='maintenance-complete-title'] form.maintenance-form")
            .Submit();

        page.WaitForAssertion(() =>
        {
            Assert.Contains("This work order can no longer be completed.", page.Markup, StringComparison.Ordinal);
            Assert.Empty(page.FindAll("#complete-technician"));
        });
    }

    [Fact]
    public void Validation_problems_on_filters_are_listed_above_the_results()
    {
        respondToMaintenance = _ => Json(
            HttpStatusCode.BadRequest,
            """{"title":"One or more validation errors occurred.","errors":{"pageSize":["Page size must not exceed 100."]}}""");

        var page = RenderMaintenancePage();

        Assert.Contains(
            "Page size must not exceed 100.",
            page.Find(".maintenance-validation").TextContent,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Paging_requests_the_next_page_and_disables_previous_on_the_first_page()
    {
        respondToMaintenance = request =>
        {
            var pageNumber = request.RequestUri!.Query.Contains("pageNumber=2", StringComparison.Ordinal) ? 2 : 1;

            return Json(HttpStatusCode.OK, Page(
                pageNumber,
                60,
                Row($"0000000{pageNumber}-0000-0000-0000-000000000001", title: $"Page {pageNumber}")));
        };

        var page = RenderMaintenancePage();

        var previous = page.FindAll(".maintenance-pages button")[0];
        Assert.True(previous.HasAttribute("disabled"));

        page.FindAll(".maintenance-pages button")[1].Click();

        page.WaitForAssertion(() =>
        {
            Assert.Contains("Page 2", page.Markup, StringComparison.Ordinal);
            Assert.False(page.FindAll(".maintenance-pages button")[0].HasAttribute("disabled"));
        });
    }

    private IRenderedComponent<Maintenance> RenderMaintenancePage()
    {
        var maintenanceHttpClient = new HttpClient(new StubHttpMessageHandler(request =>
        {
            requestedUris.Add(request.RequestUri!);

            return respondToMaintenance(request);
        }))
        {
            BaseAddress = new Uri("https://api.harborops.test/"),
        };

        var lookupHttpClient = new HttpClient(new StubHttpMessageHandler(request =>
        {
            requestedUris.Add(request.RequestUri!);

            return respondToLookup(request);
        }))
        {
            BaseAddress = new Uri("https://api.harborops.test/"),
        };

        Services.AddSingleton(new MaintenanceApiClient(maintenanceHttpClient));
        Services.AddSingleton(new VesselLookupApiClient(lookupHttpClient));

        var page = Render<Maintenance>();
        page.WaitForAssertion(() => Assert.NotEmpty(requestedUris));

        return page;
    }

    private static string Row(
        string workOrderId,
        string title = "Replace filter",
        string priority = "Medium",
        string status = "Open",
        string dueUtc = "2026-12-01T10:00:00Z")
    {
        return $$"""
            {"workOrderId":"{{workOrderId}}","vesselId":"{{VesselId}}","title":"{{title}}",
            "description":"Routine task","priority":"{{priority}}","status":"{{status}}",
            "dueUtc":"{{dueUtc}}","assignedEngineerId":null,
            "createdUtc":"2026-08-01T08:00:00Z","completedUtc":null}
            """;
    }

    private static string Page(params string[] rows) => Page(1, rows.Length, rows);

    private static string Page(int pageNumber, int totalCount, params string[] rows)
    {
        return $$"""
            {"items":[{{string.Join(',', rows)}}],"pageNumber":{{pageNumber}},"pageSize":25,"totalCount":{{totalCount}}}
            """;
    }

    private static Task<HttpResponseMessage> Json(HttpStatusCode statusCode, string json)
    {
        return Task.FromResult(new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/problem+json"),
        });
    }

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> responseFactory)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return responseFactory(request);
        }
    }
}
