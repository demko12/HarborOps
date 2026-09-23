using System.Net;
using System.Security.Claims;
using System.Text;
using Bunit;
using HarborOps.Web.Api;
using HarborOps.Web.Api.Voyages;
using HarborOps.Web.Components.Pages.Voyages;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace HarborOps.Web.Tests.Components;

public sealed class PortCallStatementPrintPageTests : BunitContext
{
    private const string VoyageId = "20000000-0000-0000-0000-000000000001";
    private const string StopId = "30000000-0000-0000-0000-000000000001";

    private const string ScriptPayload = "<script>alert('x')</script>";

    private static readonly string FinalizedStatement = Statement("Lines ashore");

    private readonly List<HttpRequestMessage> requests = [];

    private Func<HttpRequestMessage, Task<HttpResponseMessage>> respond =
        _ => Json(HttpStatusCode.OK, FinalizedStatement);

    public PortCallStatementPrintPageTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public void The_statement_is_requested_from_the_print_route()
    {
        RenderPage();

        Assert.Equal(
            $"/api/voyages/{VoyageId}/stops/{StopId}/statement/print",
            requests[0].RequestUri!.AbsolutePath);
    }

    [Fact]
    public void The_sheet_shows_the_snapshot_labels_times_and_finalizer()
    {
        var page = RenderPage();

        Assert.Equal("SOF-VOY-2026-17-02", page.Find("[data-testid=statement-number]").TextContent.Trim());
        Assert.Contains("VOY-2026-17 · Stop 2", page.Find(".print-subtitle").TextContent, StringComparison.Ordinal);
        Assert.Equal("Port of Bar (MEBAR)", page.Find("[data-testid=statement-port]").TextContent.Trim());
        Assert.Equal("Berth 3", page.Find("[data-testid=statement-berth]").TextContent.Trim());
        Assert.Contains("02 Sep 2026 08:00 UTC", page.Find("[data-testid=statement-finalized]").TextContent, StringComparison.Ordinal);
        Assert.Contains("master@harborops.test", page.Find("[data-testid=statement-finalized]").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void A_finalizer_without_a_stored_name_falls_back_to_the_short_identifier()
    {
        respond = _ => Json(HttpStatusCode.OK, Statement("Lines ashore", finalizedByName: null));

        var page = RenderPage();

        Assert.Contains("33333333", page.Find("[data-testid=statement-finalized]").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void The_document_number_repeats_in_the_table_head_of_every_printed_page()
    {
        var page = RenderPage();

        var runningHead = page.Find("thead [data-testid=statement-running-head]");

        Assert.Contains("SOF-VOY-2026-17-02", runningHead.TextContent, StringComparison.Ordinal);
        Assert.Contains("Port of Bar", runningHead.TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void A_port_time_zone_prints_local_times_with_their_offset_next_to_utc()
    {
        respond = _ => Json(HttpStatusCode.OK, Statement("Lines ashore", portTimeZone: "Europe/Podgorica"));

        var page = RenderPage();

        Assert.Equal("Europe/Podgorica", page.Find("[data-testid=statement-time-zone]").TextContent.Trim());
        Assert.Contains("01 Sep 2026 12:00 LT (UTC+02:00)", page.Find("[data-testid=statement-ata]").TextContent, StringComparison.Ordinal);
        Assert.Contains("01 Sep 2026 10:00 UTC", page.Find("[data-testid=statement-ata]").TextContent, StringComparison.Ordinal);
        Assert.Equal(
            ["01 Sep 2026 12:00 LT (UTC+02:00)", "01 Sep 2026 12:20 LT (UTC+02:00)", "01 Sep 2026 20:00 LT (UTC+02:00)"],
            page.FindAll("[data-testid=statement-entry-local]").Select(cell => cell.TextContent.Trim()));
    }

    [Fact]
    public void A_port_without_a_time_zone_prints_utc_only()
    {
        var page = RenderPage();

        Assert.Equal("UTC (port time zone not set)", page.Find("[data-testid=statement-time-zone]").TextContent.Trim());
        Assert.Empty(page.FindAll("[data-testid=statement-entry-local]"));
        Assert.Equal("01 Sep 2026 10:00 UTC", page.Find("[data-testid=statement-ata]").TextContent.Trim());
    }

    [Fact]
    public void Entries_are_printed_in_their_stored_order_with_their_source()
    {
        var page = RenderPage();

        var rows = page.FindAll("[data-testid=statement-entry]");

        Assert.Equal(3, rows.Count);
        Assert.Contains("Stop arrival", rows[0].TextContent, StringComparison.Ordinal);
        Assert.Contains("All fast", rows[1].TextContent, StringComparison.Ordinal);
        Assert.Contains("Lines ashore", rows[1].TextContent, StringComparison.Ordinal);
        Assert.Contains("Stop departure", rows[2].TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void Text_from_the_snapshot_is_rendered_as_text()
    {
        respond = _ => Json(HttpStatusCode.OK, Statement(ScriptPayload));

        var page = RenderPage();

        Assert.Empty(page.FindAll("script"));
        Assert.Contains(ScriptPayload, page.FindAll("[data-testid=statement-entry]")[1].TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void The_toolbar_offers_printing_and_a_way_back_to_the_timeline()
    {
        var page = RenderPage();

        Assert.Equal($"/voyages/{VoyageId}", page.Find(".print-back").GetAttribute("href"));
        Assert.NotEmpty(page.FindAll(".print-button"));
        Assert.Contains("no-print", page.Find(".print-toolbar").ClassList);
    }

    [Fact]
    public void A_draft_statement_is_not_printable()
    {
        respond = _ => Json(
            HttpStatusCode.Conflict,
            """{"status":409,"title":"Only a finalized statement can be printed.","detail":"Port-call statement is still a draft."}""");

        var page = RenderPage();

        Assert.Contains("Only a finalized statement can be printed.", page.Find(".api-error-state").TextContent, StringComparison.Ordinal);
        Assert.Empty(page.FindAll(".print-sheet"));
        Assert.Empty(page.FindAll(".print-button"));
    }

    [Fact]
    public void A_failed_load_can_be_retried()
    {
        respond = _ => Json(
            HttpStatusCode.ServiceUnavailable,
            """{"title":"Unable to load the printable statement","status":503}""");

        var page = RenderPage();

        Assert.NotEmpty(page.FindAll(".api-error-state"));

        respond = _ => Json(HttpStatusCode.OK, FinalizedStatement);
        page.Find(".api-error-state button").Click();

        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll("[data-testid=statement-sheet]")));
    }

    private IRenderedComponent<PortCallStatementPrint> RenderPage()
    {
        var httpClient = new HttpClient(new StubHttpMessageHandler(request =>
        {
            requests.Add(request);

            return respond(request);
        }))
        {
            BaseAddress = new Uri("https://api.harborops.test/"),
        };

        Services.AddSingleton(new VoyagesApiClient(httpClient));
        Services.RemoveAll<IHttpContextAccessor>();
        Services.RemoveAll<CurrentUserAuthorizationState>();
        Services.AddSingleton<IHttpContextAccessor>(new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim(CurrentUserAuthorizationState.PermissionClaimType, CurrentUserAuthorizationState.VoyagesReadPermission)],
                    "TestAuthentication")),
            },
        });
        Services.AddScoped<CurrentUserAuthorizationState>();

        var page = Render<PortCallStatementPrint>(parameters => parameters
            .Add(component => component.VoyageId, Guid.Parse(VoyageId))
            .Add(component => component.StopId, Guid.Parse(StopId)));

        page.WaitForAssertion(() => Assert.NotEmpty(requests));

        return page;
    }

    private static string Statement(
        string notes,
        string? portTimeZone = null,
        string? finalizedByName = "master@harborops.test")
    {
        var escapedNotes = System.Text.Json.JsonSerializer.Serialize(notes);
        var zone = portTimeZone is null ? "null" : $"\"{portTimeZone}\"";
        var finalizer = finalizedByName is null ? "null" : $"\"{finalizedByName}\"";

        return $$"""
            {
              "statementId":"60000000-0000-0000-0000-000000000001",
              "documentNumber":"SOF-VOY-2026-17-02",
              "voyageId":"{{VoyageId}}",
              "stopId":"{{StopId}}",
              "status":"Finalized",
              "version":"70000000-0000-0000-0000-000000000009",
              "sourceVersion":"ABCDEF",
              "voyageNumber":"VOY-2026-17",
              "stopOrder":2,
              "portName":"Port of Bar",
              "portUnLocode":"MEBAR",
              "portTimeZone":{{zone}},
              "berthName":"Berth 3",
              "actualArrivalUtc":"2026-09-01T10:00:00Z",
              "actualDepartureUtc":"2026-09-01T18:00:00Z",
              "createdByUserId":"11111111-1111-1111-1111-111111111111",
              "createdAtUtc":"2026-09-01T19:00:00Z",
              "finalizedByUserId":"33333333-3333-3333-3333-333333333333",
              "finalizedByName":{{finalizer}},
              "finalizedAtUtc":"2026-09-02T08:00:00Z",
              "canFinalize":false,
              "blockers":[],
              "entries":[
                {"position":1,"source":"StopArrival","sourceId":"{{StopId}}","sourceRevision":null,"label":"Actual arrival (ATA)","occurredAtUtc":"2026-09-01T10:00:00Z","endedAtUtc":null,"notes":null,"durationMinutes":null,"isOutsidePortCall":false},
                {"position":2,"source":"ManualActivity","sourceId":"40000000-0000-0000-0000-000000000001","sourceRevision":2,"label":"AllFast","occurredAtUtc":"2026-09-01T10:20:00Z","endedAtUtc":null,"notes":{{escapedNotes}},"durationMinutes":null,"isOutsidePortCall":false},
                {"position":3,"source":"StopDeparture","sourceId":"{{StopId}}","sourceRevision":null,"label":"Actual departure (ATD)","occurredAtUtc":"2026-09-01T18:00:00Z","endedAtUtc":null,"notes":null,"durationMinutes":null,"isOutsidePortCall":false}
              ]
            }
            """;
    }

    private static Task<HttpResponseMessage> Json(HttpStatusCode statusCode, string json)
    {
        return Task.FromResult(new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
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
