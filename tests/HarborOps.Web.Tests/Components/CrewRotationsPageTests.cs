using System.Net;
using System.Security.Claims;
using System.Text;
using Bunit;
using HarborOps.Web.Api;
using HarborOps.Web.Api.Fleet;
using HarborOps.Web.Components.Pages.Crew;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace HarborOps.Web.Tests.Components;

public sealed class CrewRotationsPageTests : BunitContext
{
    private const string VesselId = "40000000-0000-0000-0000-000000000004";
    private const string AssignmentId = "31000000-0000-0000-0000-000000000001";
    private const string CrewMemberId = "10000000-0000-0000-0000-000000000001";

    private readonly List<Uri> requestedUris = [];

    private Func<HttpRequestMessage, Task<HttpResponseMessage>> respondToRotations =
        _ => Json(HttpStatusCode.OK, Page());

    private Func<HttpRequestMessage, Task<HttpResponseMessage>> respondToLookup =
        _ => Json(HttpStatusCode.OK, $$"""{"vessels":[{"id":"{{VesselId}}","name":"Aurora Borealis"}]}""");

    private Func<HttpRequestMessage, Task<HttpResponseMessage>> respondToVesselSearch =
        _ => Json(HttpStatusCode.OK, VesselSearchPage());

    [Fact]
    public void Initial_state_loads_rotations_without_filters()
    {
        var page = RenderRotationsPage();

        page.WaitForAssertion(() =>
        {
            var query = requestedUris.First(IsRotationsRequest).Query;
            Assert.DoesNotContain("vesselId=", query, StringComparison.Ordinal);
            Assert.DoesNotContain("from=", query, StringComparison.Ordinal);
            Assert.DoesNotContain("overdueOnly=", query, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Rows_are_shown_with_vessel_name_resolved_through_the_fleet_lookup()
    {
        respondToRotations = _ => Json(HttpStatusCode.OK, Page(Row()));

        var page = RenderRotationsPage();

        page.WaitForAssertion(() =>
        {
            Assert.Contains("Aurora Borealis", page.Markup, StringComparison.Ordinal);
            Assert.Contains("Ivan Petrović", page.Markup, StringComparison.Ordinal);
            Assert.Contains("2-2", page.Markup, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void A_vessel_with_no_lookup_match_does_not_render_an_id()
    {
        respondToLookup = _ => Json(HttpStatusCode.OK, """{"vessels":[]}""");
        respondToRotations = _ => Json(HttpStatusCode.OK, Page(Row()));

        var page = RenderRotationsPage();

        page.WaitForAssertion(() => Assert.Contains("Unknown vessel", page.Markup, StringComparison.Ordinal));
    }

    [Fact]
    public void An_overdue_rotation_shows_the_overdue_badge()
    {
        respondToRotations = _ => Json(HttpStatusCode.OK, Page(Row(isOverdue: true)));

        var page = RenderRotationsPage();

        page.WaitForAssertion(() =>
        {
            var badge = page.Find(".crew-rotations-table-scroll .crew-rotations-status");
            Assert.Equal("Overdue", badge.TextContent.Trim());
            Assert.Contains("crew-rotations-status-overdue", badge.ClassList);
        });
    }

    [Fact]
    public void A_non_overdue_rotation_shows_the_planned_badge()
    {
        respondToRotations = _ => Json(HttpStatusCode.OK, Page(Row(isOverdue: false)));

        var page = RenderRotationsPage();

        page.WaitForAssertion(() =>
        {
            var badge = page.Find(".crew-rotations-table-scroll .crew-rotations-status");
            Assert.Equal("Planned", badge.TextContent.Trim());
            Assert.Contains("crew-rotations-status-planned", badge.ClassList);
        });
    }

    [Fact]
    public void The_mobile_card_is_collapsed_until_its_heading_is_clicked()
    {
        respondToRotations = _ => Json(HttpStatusCode.OK, Page(Row()));

        var page = RenderRotationsPage();
        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll(".crew-rotation-card-heading")));

        Assert.Empty(page.FindAll(".crew-rotation-card-details"));

        page.Find(".crew-rotation-card-heading").Click();

        Assert.NotEmpty(page.FindAll(".crew-rotation-card-details"));
        Assert.Equal("true", page.Find(".crew-rotation-card-heading").GetAttribute("aria-expanded"));

        page.Find(".crew-rotation-card-heading").Click();

        Assert.Empty(page.FindAll(".crew-rotation-card-details"));
    }

    [Fact]
    public void No_relieving_member_shows_not_selected()
    {
        respondToRotations = _ => Json(HttpStatusCode.OK, Page(Row(relievingCrewMemberId: null)));

        var page = RenderRotationsPage();

        page.WaitForAssertion(() => Assert.Contains("Not selected", page.Markup, StringComparison.Ordinal));
    }

    [Fact]
    public void Empty_data_shows_the_empty_state_instead_of_a_table()
    {
        respondToRotations = _ => Json(HttpStatusCode.OK, Page());

        var page = RenderRotationsPage();

        page.WaitForAssertion(() =>
        {
            Assert.Empty(page.FindAll("table.crew-rotations-table"));
            Assert.Contains("No rotations found", page.Markup, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void An_api_failure_shows_the_error_state_with_retry()
    {
        respondToRotations = _ => Json(HttpStatusCode.InternalServerError, """{"status":500}""");

        var page = RenderRotationsPage();

        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll(".api-error-state, [class*='error']")));
    }

    [Fact]
    public async Task Changing_filters_automatically_sends_them_and_resets_to_page_one()
    {
        var page = RenderRotationsPage();
        page.WaitForAssertion(() => Assert.Contains(requestedUris, IsRotationsRequest));

        page.Find("#crew-rotations-from").Change("2026-07-01");
        page.Find("#crew-rotations-to").Change("2026-07-31");
        page.Find("#crew-rotations-overdue").Change("true");
        page.Find(".vessel-picker-input").Input("Auro");

        page.WaitForAssertion(
            () => Assert.Single(page.FindAll(".vessel-picker-option")),
            TimeSpan.FromSeconds(3));

        await page.InvokeAsync(() => page.Find(".vessel-picker-option").Click());

        page.WaitForAssertion(
            () =>
            {
                var lastQuery = requestedUris.Last(IsRotationsRequest).Query;

                Assert.Contains("from=2026-07-01", lastQuery, StringComparison.Ordinal);
                Assert.Contains("to=2026-07-31", lastQuery, StringComparison.Ordinal);
                Assert.Contains("overdueOnly=true", lastQuery, StringComparison.Ordinal);
                Assert.Contains($"vesselId={VesselId}", lastQuery, StringComparison.Ordinal);
                Assert.Contains("pageNumber=1", lastQuery, StringComparison.Ordinal);
            },
            TimeSpan.FromSeconds(3));
    }

    [Fact]
    public void A_to_date_before_the_from_date_shows_a_validation_message_without_calling_the_api()
    {
        var page = RenderRotationsPage();
        page.WaitForAssertion(() => Assert.Contains(requestedUris, IsRotationsRequest));

        page.Find("#crew-rotations-from").Change("2026-07-10");
        var requestsBeforeInvalidTo = requestedUris.Count(IsRotationsRequest);

        page.Find("#crew-rotations-to").Change("2026-07-01");

        page.WaitForAssertion(() => Assert.Contains(
            "must be on or after",
            page.Find(".crew-rotations-validation").TextContent,
            StringComparison.Ordinal));
        Assert.Equal(requestsBeforeInvalidTo, requestedUris.Count(IsRotationsRequest));
    }

    [Fact]
    public void The_reset_button_clears_all_filters_and_reloads()
    {
        var page = RenderRotationsPage();
        page.WaitForAssertion(() => Assert.Contains(requestedUris, IsRotationsRequest));

        page.Find("#crew-rotations-from").Change("2026-07-01");
        page.Find(".crew-reset-button").Click();

        page.WaitForAssertion(() =>
        {
            var lastQuery = requestedUris.Last(IsRotationsRequest).Query;
            Assert.DoesNotContain("from=", lastQuery, StringComparison.Ordinal);
        });
        Assert.Null(page.Find("#crew-rotations-from").GetAttribute("value"));
    }

    [Fact]
    public void Pagination_requests_the_next_page()
    {
        respondToRotations = _ => Json(HttpStatusCode.OK, Page([Row()], pageNumber: 1, pageSize: 25, totalCount: 30));

        var page = RenderRotationsPage();
        page.WaitForAssertion(() => Assert.Contains("Page 1 of 2", page.Markup, StringComparison.Ordinal));

        respondToRotations = _ => Json(HttpStatusCode.OK, Page([Row()], pageNumber: 2, pageSize: 25, totalCount: 30));
        page.FindAll(".crew-pagination button")[1].Click();

        page.WaitForAssertion(() =>
        {
            var lastQuery = requestedUris.Last(IsRotationsRequest).Query;
            Assert.Contains("pageNumber=2", lastQuery, StringComparison.Ordinal);
        });
    }

    private static bool IsRotationsRequest(Uri uri) => uri.AbsolutePath.EndsWith("/rotations", StringComparison.Ordinal);

    private IRenderedComponent<CrewRotations> RenderRotationsPage(params string[] permissions)
    {
        var crewHttpClient = new HttpClient(new StubHttpMessageHandler(request =>
        {
            requestedUris.Add(request.RequestUri!);

            return respondToRotations(request);
        }))
        {
            BaseAddress = new Uri("https://api.harborops.test/"),
        };

        var fleetHttpClient = new HttpClient(new StubHttpMessageHandler(request =>
        {
            requestedUris.Add(request.RequestUri!);

            return request.RequestUri!.AbsolutePath.EndsWith("/lookup", StringComparison.Ordinal)
                ? respondToLookup(request)
                : respondToVesselSearch(request);
        }))
        {
            BaseAddress = new Uri("https://api.harborops.test/"),
        };

        Services.AddSingleton(new CrewApiClient(crewHttpClient));
        Services.AddSingleton(new FleetApiClient(fleetHttpClient));
        Services.AddSingleton<IHttpContextAccessor>(new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    (permissions.Length == 0 ? [CurrentUserAuthorizationState.CrewReadPermission] : permissions)
                        .Select(permission => new Claim(CurrentUserAuthorizationState.PermissionClaimType, permission))
                        .ToArray(),
                    "TestAuthentication")),
            },
        });
        Services.AddScoped<CurrentUserAuthorizationState>();

        var page = Render<CrewRotations>();
        page.WaitForAssertion(() => Assert.NotEmpty(requestedUris));

        return page;
    }

    private static string Page(params string[] rows) => Page(rows, 1, 25, rows.Length);

    private static string Page(string[] rows, int pageNumber, int pageSize, int totalCount) => $$"""
        {"items":[{{string.Join(',', rows)}}],"pageNumber":{{pageNumber}},"pageSize":{{pageSize}},"totalCount":{{totalCount}}}
        """;

    private static string Row(
        string? relievingCrewMemberId = "20000000-0000-0000-0000-000000000002",
        bool isOverdue = false) => $$"""
        {"rotationId":"81000000-0000-0000-0000-000000000001","assignmentId":"{{AssignmentId}}",
        "vesselId":"{{VesselId}}","crewMemberId":"{{CrewMemberId}}","crewMemberName":"Ivan Petrović",
        "rankId":"50000000-0000-0000-0000-000000000001","rankName":"Bosun",
        "relievingCrewMemberId":{{(relievingCrewMemberId is null ? "null" : $"\"{relievingCrewMemberId}\"")}},
        "relievingCrewMemberName":{{(relievingCrewMemberId is null ? "null" : "\"Relief Member\"")}},
        "rotationPattern":"2-2","plannedReliefDate":"2026-07-01","isOverdue":{{(isOverdue ? "true" : "false")}}}
        """;

    private static string VesselSearchPage() => $$"""
        {"items":[{"vesselId":"{{VesselId}}","name":"Aurora Borealis","imoNumber":"5100001",
        "flagState":"Montenegro","currentStatus":"Active","vesselTypeId":"50000000-0000-0000-0000-000000000002",
        "vesselTypeName":"Bulk carrier","companyId":"60000000-0000-0000-0000-000000000001","dwt":52000,
        "updatedAtUtc":"2026-08-01T10:00:00Z"}],"pageNumber":1,"pageSize":8,"totalCount":1}
        """;

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
