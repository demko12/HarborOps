using System.Net;
using System.Text;
using Bunit;
using HarborOps.Web.Api.Maintenance;
using HarborOps.Web.Components.Pages.Maintenance;
using Microsoft.Extensions.DependencyInjection;

namespace HarborOps.Web.Tests.Components;

public sealed class ScheduleOccurrencesSectionTests : BunitContext
{
    private const string ScheduleId = "d0000000-0000-0000-0000-00000000000d";
    private const string VesselId = "30000000-0000-0000-0000-000000000002";
    private const string OccurrenceId = "e0000000-0000-0000-0000-00000000000e";
    private const string CancelledOccurrenceId = "e0000000-0000-0000-0000-0000000000e2";
    private const string WorkOrderId = "f0000000-0000-0000-0000-00000000000f";
    private const string CancelledWorkOrderId = "f0000000-0000-0000-0000-0000000000f3";
    private const string ReplacementWorkOrderId = "f0000000-0000-0000-0000-0000000000f2";

    private const string OccurrencesPath = $"/api/maintenance/schedules/{ScheduleId}/occurrences";
    private const string WorkOrdersPath = $"/api/maintenance/schedules/{ScheduleId}/work-orders";
    private const string ReplacementPath =
        $"/api/maintenance/preventive-work/occurrences/{CancelledOccurrenceId}/replacement";

    private readonly List<Uri> requestedUris = [];
    private int changedCount;

    private Func<HttpRequestMessage, Task<HttpResponseMessage>> respond = OneGeneratedOccurrence;

    private int OccurrenceRequests => requestedUris.Count(uri => uri.AbsolutePath == OccurrencesPath);

    [Fact]
    public void The_occurrences_and_the_work_orders_of_the_schedule_are_loaded()
    {
        RenderSection();

        Assert.Contains(requestedUris, uri => uri.AbsolutePath == OccurrencesPath);
        Assert.Contains(requestedUris, uri => uri.AbsolutePath == WorkOrdersPath);
    }

    [Fact]
    public void An_occurrence_links_its_work_order_with_the_work_order_status()
    {
        var section = RenderSection();

        var row = section.WaitForElement(".schedules-occurrences-table tbody tr");

        Assert.Contains("2026-09-30", row.TextContent, StringComparison.Ordinal);
        Assert.Equal("Generated", row.QuerySelector(".preventive-state")!.TextContent.Trim());
        Assert.Equal($"/maintenance/work-orders/{WorkOrderId}", row.QuerySelector("a")!.GetAttribute("href"));
        Assert.Equal("InProgress", row.QuerySelector(".work-order-status")!.TextContent.Trim());
    }

    [Fact]
    public void A_replaced_occurrence_also_links_the_cancelled_work_order()
    {
        respond = request => request.RequestUri!.AbsolutePath == OccurrencesPath
            ? Json(HttpStatusCode.OK, Occurrences(Occurrence(
                OccurrenceId,
                "Completed",
                WorkOrderId,
                replacedWorkOrderId: CancelledWorkOrderId,
                resolvedUtc: "2026-10-01T12:00:00Z")))
            : Json(HttpStatusCode.OK, WorkOrders(WorkOrder(WorkOrderId, OccurrenceId, "Completed")));

        var section = RenderSection();

        var links = section.WaitForElement(".schedules-occurrences-table tbody tr").QuerySelectorAll("a");

        Assert.Equal($"/maintenance/work-orders/{WorkOrderId}", links[0].GetAttribute("href"));
        Assert.Equal($"/maintenance/work-orders/{CancelledWorkOrderId}", links[1].GetAttribute("href"));
        Assert.Contains("2026-10-01 12:00", section.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void A_schedule_without_generated_work_explains_where_to_generate_it()
    {
        respond = request => Json(
            HttpStatusCode.OK,
            request.RequestUri!.AbsolutePath == OccurrencesPath ? Occurrences() : WorkOrders());

        var section = RenderSection();

        section.WaitForAssertion(() => Assert.Contains(
            "No work generated for this schedule",
            section.Markup,
            StringComparison.Ordinal));
        Assert.Empty(section.FindAll(".schedules-occurrences-table"));
    }

    [Fact]
    public void An_hour_based_schedule_explains_that_it_is_not_generated_yet()
    {
        respond = request => request.RequestUri!.AbsolutePath == OccurrencesPath
            ? Json(
                HttpStatusCode.Conflict,
                """{"title":"Maintenance schedule is not time based.","detail":"Uses RunningHours."}""")
            : Json(HttpStatusCode.OK, WorkOrders());

        var section = RenderSection();

        section.WaitForAssertion(() => Assert.Contains(
            "Hour-based schedules are not generated yet",
            section.Markup,
            StringComparison.Ordinal));
        Assert.Empty(section.FindAll(".api-error-state"));
    }

    [Fact]
    public void A_failed_load_offers_a_retry_that_asks_again()
    {
        respond = _ => Json(HttpStatusCode.InternalServerError, """{"title":"Server error","detail":"Broken"}""");

        var section = RenderSection();
        section.WaitForElement(".api-error-state");
        var before = OccurrenceRequests;

        respond = OneGeneratedOccurrence;
        section.Find(".api-error-state button").Click();

        section.WaitForAssertion(() =>
        {
            Assert.True(OccurrenceRequests > before);
            Assert.NotNull(section.Find(".schedules-occurrences-table"));
        });
    }

    [Fact]
    public void Replacing_a_cancelled_occurrence_links_the_new_work_order_and_notifies_the_board()
    {
        respond = request => request.RequestUri!.AbsolutePath switch
        {
            ReplacementPath => Json(HttpStatusCode.Created, Replacement()),
            OccurrencesPath => Json(HttpStatusCode.OK, Occurrences(Occurrence(CancelledOccurrenceId, "Cancelled", CancelledWorkOrderId))),
            _ => Json(HttpStatusCode.OK, WorkOrders(WorkOrder(CancelledWorkOrderId, CancelledOccurrenceId, "Canceled"))),
        };

        var section = RenderSection();
        section.WaitForElement(".preventive-replace").Click();
        var before = OccurrenceRequests;

        section.Find(".preventive-confirm-inline .maintenance-button").Click();

        section.WaitForAssertion(() =>
        {
            var toast = section.Find(".preventive-toast");

            Assert.Contains("A replacement work order was created.", toast.TextContent, StringComparison.Ordinal);
            Assert.Equal(
                $"/maintenance/work-orders/{ReplacementWorkOrderId}",
                toast.QuerySelector("a")!.GetAttribute("href"));
            Assert.Empty(section.FindAll(".preventive-confirm-inline"));
            Assert.True(OccurrenceRequests > before);
            Assert.Equal(1, changedCount);
        });
        Assert.Contains(requestedUris, uri => uri.AbsolutePath == ReplacementPath);
    }

    [Fact]
    public void A_conflicting_replacement_shows_the_reason_and_reloads()
    {
        respond = request => request.RequestUri!.AbsolutePath switch
        {
            ReplacementPath => Json(
                HttpStatusCode.Conflict,
                """{"title":"Schedule occurrence cannot be replaced from its current state.","detail":"Already replaced."}"""),
            OccurrencesPath => Json(HttpStatusCode.OK, Occurrences(Occurrence(CancelledOccurrenceId, "Cancelled", CancelledWorkOrderId))),
            _ => Json(HttpStatusCode.OK, WorkOrders()),
        };

        var section = RenderSection();
        section.WaitForElement(".preventive-replace").Click();
        var before = OccurrenceRequests;

        section.Find(".preventive-confirm-inline .maintenance-button").Click();

        section.WaitForAssertion(() =>
        {
            Assert.Contains("Already replaced.", section.Find(".preventive-confirm-inline").TextContent, StringComparison.Ordinal);
            Assert.True(OccurrenceRequests > before);
        });
        Assert.Equal(0, changedCount);
    }

    [Fact]
    public void Keeping_the_occurrence_cancelled_closes_the_confirmation_without_a_request()
    {
        respond = request => Json(
            HttpStatusCode.OK,
            request.RequestUri!.AbsolutePath == OccurrencesPath
                ? Occurrences(Occurrence(CancelledOccurrenceId, "Cancelled", CancelledWorkOrderId))
                : WorkOrders());

        var section = RenderSection();
        section.WaitForElement(".preventive-replace").Click();

        section.Find(".preventive-confirm-inline .maintenance-reset").Click();

        Assert.Empty(section.FindAll(".preventive-confirm-inline"));
        Assert.DoesNotContain(requestedUris, uri => uri.AbsolutePath == ReplacementPath);
    }

    [Fact]
    public void Readers_cannot_replace_a_cancelled_occurrence()
    {
        respond = request => Json(
            HttpStatusCode.OK,
            request.RequestUri!.AbsolutePath == OccurrencesPath
                ? Occurrences(Occurrence(CancelledOccurrenceId, "Cancelled", CancelledWorkOrderId))
                : WorkOrders());

        var section = RenderSection(canManage: false);

        section.WaitForElement(".schedules-occurrences-table");

        Assert.Empty(section.FindAll(".preventive-replace"));
    }

    private IRenderedComponent<ScheduleOccurrencesSection> RenderSection(bool canManage = true)
    {
        var httpClient = new HttpClient(new StubHttpMessageHandler(request =>
        {
            requestedUris.Add(request.RequestUri!);

            return respond(request);
        }))
        {
            BaseAddress = new Uri("https://api.harborops.test/"),
        };

        Services.AddSingleton(new MaintenanceApiClient(httpClient));

        var section = Render<ScheduleOccurrencesSection>(parameters => parameters
            .Add(component => component.ScheduleId, Guid.Parse(ScheduleId))
            .Add(component => component.CanManage, canManage)
            .Add(component => component.OnChanged, () => changedCount++));
        section.WaitForAssertion(() => Assert.Contains(requestedUris, uri => uri.AbsolutePath == OccurrencesPath));

        return section;
    }

    private static Task<HttpResponseMessage> OneGeneratedOccurrence(HttpRequestMessage request)
    {
        return request.RequestUri!.AbsolutePath == OccurrencesPath
            ? Json(HttpStatusCode.OK, Occurrences(Occurrence(OccurrenceId, "Generated", WorkOrderId)))
            : Json(HttpStatusCode.OK, WorkOrders(WorkOrder(WorkOrderId, OccurrenceId, "InProgress")));
    }

    private static string Quoted(string? value)
    {
        return value is null ? "null" : $"\"{value}\"";
    }

    private static string Occurrences(params string[] items)
    {
        return $$"""
            {"scheduleId":"{{ScheduleId}}","maintenanceType":"Quarterly inspection","frequency":"Quarterly",
            "nextDueDate":"2026-12-30","items":[{{string.Join(',', items)}}]}
            """;
    }

    private static string Occurrence(
        string occurrenceId,
        string state,
        string workOrderId,
        string dueDate = "2026-09-30",
        string? replacedWorkOrderId = null,
        string? resolvedUtc = null)
    {
        return $$"""
            {"occurrenceId":"{{occurrenceId}}","scheduleId":"{{ScheduleId}}","dueDate":"{{dueDate}}",
            "dueUtc":"{{dueDate}}T23:59:59Z","state":"{{state}}","workOrderId":"{{workOrderId}}",
            "replacedWorkOrderId":{{Quoted(replacedWorkOrderId)}},"generatedUtc":"2026-09-10T09:00:00Z",
            "resolvedUtc":{{Quoted(resolvedUtc)}}}
            """;
    }

    private static string WorkOrders(params string[] items)
    {
        return $$"""{"scheduleId":"{{ScheduleId}}","items":[{{string.Join(',', items)}}]}""";
    }

    private static string WorkOrder(string workOrderId, string occurrenceId, string status)
    {
        return $$"""
            {"workOrderId":"{{workOrderId}}","occurrenceId":"{{occurrenceId}}","vesselId":"{{VesselId}}",
            "occurrenceDueDate":"2026-09-30","title":"Quarterly inspection - due 2026-09-30","status":"{{status}}",
            "priority":"Medium","occurrenceState":"Generated","dueUtc":"2026-09-30T23:59:59Z",
            "createdUtc":"2026-09-10T09:00:00Z","completedUtc":null}
            """;
    }

    private static string Replacement()
    {
        return $$"""
            {"occurrenceId":"{{CancelledOccurrenceId}}","scheduleId":"{{ScheduleId}}","vesselId":"{{VesselId}}",
            "dueDate":"2026-09-30","dueUtc":"2026-09-30T23:59:59Z","state":"Generated",
            "workOrderId":"{{ReplacementWorkOrderId}}","replacedWorkOrderId":"{{CancelledWorkOrderId}}",
            "equipmentIds":[],"generatedUtc":"2026-09-10T09:00:00Z"}
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
        Func<HttpRequestMessage, Task<HttpResponseMessage>> responseFactory) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => responseFactory(request);
    }
}
