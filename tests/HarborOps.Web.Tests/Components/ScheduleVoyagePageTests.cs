using System.Net;
using System.Security.Claims;
using System.Text;
using Bunit;
using HarborOps.Web.Api;
using HarborOps.Web.Api.Voyages;
using HarborOps.Web.Components.Pages.Voyages;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace HarborOps.Web.Tests.Components;

public sealed class ScheduleVoyagePageTests : BunitContext
{
    private const string VesselPageJson = """
        {"items":[{"vesselId":"40000000-0000-0000-0000-000000000009","name":"Northern Star",
        "imoNumber":"5100001","flagState":"Montenegro","currentStatus":"Active",
        "vesselTypeId":"50000000-0000-0000-0000-000000000001","vesselTypeName":"Bulk carrier",
        "companyId":"60000000-0000-0000-0000-000000000001","dwt":52000,
        "updatedAtUtc":"2026-08-01T10:00:00Z"}],"pageNumber":1,"pageSize":8,"totalCount":1}
        """;

    private readonly List<(string Path, string Query, string Body)> writes = [];
    private readonly List<Uri> requestedUris = [];

    private Func<HttpRequestMessage, Task<HttpResponseMessage>> respond =
        _ => Json(HttpStatusCode.OK, VesselPageJson);

    [Fact]
    public void Without_the_manage_permission_the_form_is_not_offered()
    {
        var page = RenderSchedulePage();

        Assert.Empty(page.FindAll("form.schedule-form"));
        Assert.Contains("Voyage management permission required", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Searching_looks_vessels_up_over_the_fleet_api()
    {
        var page = RenderSchedulePage(ScheduleVoyage.ManageVoyagesPermission);

        page.Find("#schedule-vessel-search").Input("Northern");

        page.WaitForAssertion(() =>
        {
            Assert.Contains(
                requestedUris,
                uri => uri.AbsolutePath == "/api/fleet/vessels"
                    && uri.Query.Contains("search=Northern", StringComparison.Ordinal));
            Assert.Contains("Northern Star", page.Find(".schedule-vessel-results").TextContent, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Scheduling_submits_the_selected_vessel_id_and_utc_times()
    {
        respond = request => request.Method == HttpMethod.Get
            ? Json(HttpStatusCode.OK, VesselPageJson)
            : Json(HttpStatusCode.Created, """{"id":"70000000-0000-0000-0000-000000000004"}""");

        var page = RenderSchedulePage(ScheduleVoyage.ManageVoyagesPermission);

        SelectFirstVessel(page);

        page.Find("#schedule-voyage-number").Change("VOY-2026-0143");
        page.Find("#schedule-departure-port").Input("mebar");
        page.Find("#schedule-arrival-port").Input("itgoa");
        page.Find("#schedule-departure-date").Change("2026-09-01");
        page.Find("#schedule-departure-time").Change("06:00:00");
        page.Find("#schedule-arrival-date").Change("2026-09-04");
        page.Find("#schedule-arrival-time").Change("17:30:00");
        page.Find("form.schedule-form").Submit();

        page.WaitForAssertion(() =>
        {
            var write = Assert.Single(writes);

            Assert.Equal("/api/voyages", write.Path);
            Assert.Contains("\"voyageNumber\":\"VOY-2026-0143\"", write.Body, StringComparison.Ordinal);
            Assert.Contains("40000000-0000-0000-0000-000000000009", write.Body, StringComparison.Ordinal);
            Assert.Contains("2026-09-01T06:00:00Z", write.Body, StringComparison.Ordinal);
            Assert.Contains("2026-09-04T17:30:00Z", write.Body, StringComparison.Ordinal);
            Assert.DoesNotContain("Northern Star", write.Body, StringComparison.Ordinal);
            Assert.Contains("\"departurePortCode\":\"MEBAR\"", write.Body, StringComparison.Ordinal);
            Assert.Contains("\"arrivalPortCode\":\"ITGOA\"", write.Body, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Scheduling_returns_to_the_board_after_success()
    {
        respond = request => request.Method == HttpMethod.Get
            ? Json(HttpStatusCode.OK, VesselPageJson)
            : Json(HttpStatusCode.Created, """{"id":"70000000-0000-0000-0000-000000000004"}""");

        var page = RenderSchedulePage(ScheduleVoyage.ManageVoyagesPermission);
        var navigation = Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>();

        SelectFirstVessel(page);
        page.Find("form.schedule-form").Submit();

        page.WaitForAssertion(() => Assert.EndsWith("/voyages", navigation.Uri, StringComparison.Ordinal));
    }

    [Fact]
    public void Validation_problems_from_the_api_are_listed_on_the_form()
    {
        respond = request => request.Method == HttpMethod.Get
            ? Json(HttpStatusCode.OK, VesselPageJson)
            : Json(
                HttpStatusCode.BadRequest,
                """{"title":"One or more validation errors occurred.","status":400,"errors":{"plannedArrivalUtc":["Planned arrival must be later than planned departure."]}}""");

        var page = RenderSchedulePage(ScheduleVoyage.ManageVoyagesPermission);

        SelectFirstVessel(page);
        page.Find("form.schedule-form").Submit();

        page.WaitForAssertion(() => Assert.Contains(
            "Planned arrival must be later than planned departure.",
            page.Find(".schedule-failure").TextContent,
            StringComparison.Ordinal));
    }

    [Fact]
    public void A_conflicting_voyage_number_is_reported_on_the_form()
    {
        respond = request => request.Method == HttpMethod.Get
            ? Json(HttpStatusCode.OK, VesselPageJson)
            : Json(
                HttpStatusCode.Conflict,
                """{"title":"A voyage with this voyage number already exists.","status":409}""");

        var page = RenderSchedulePage(ScheduleVoyage.ManageVoyagesPermission);

        SelectFirstVessel(page);
        page.Find("form.schedule-form").Submit();

        page.WaitForAssertion(() => Assert.Contains(
            "A voyage with this voyage number already exists.",
            page.Find(".schedule-failure").TextContent,
            StringComparison.Ordinal));
    }

    [Fact]
    public void Scheduling_is_blocked_until_a_vessel_is_selected()
    {
        var page = RenderSchedulePage(ScheduleVoyage.ManageVoyagesPermission);

        Assert.True(page.Find(".schedule-button").HasAttribute("disabled"));

        SelectFirstVessel(page);

        Assert.False(page.Find(".schedule-button").HasAttribute("disabled"));
    }

    [Fact]
    public void Focusing_the_vessel_field_lists_vessels_without_a_search_term()
    {
        var page = RenderSchedulePage(ScheduleVoyage.ManageVoyagesPermission);

        page.Find("#schedule-vessel-search").Focus();

        page.WaitForAssertion(() =>
        {
            Assert.Contains(
                requestedUris,
                uri => uri.AbsolutePath == "/api/fleet/vessels"
                    && uri.Query.Contains("search=&", StringComparison.Ordinal));
            Assert.Contains("Northern Star", page.Find(".schedule-vessel-results").TextContent, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Focusing_again_reuses_the_options_already_listed()
    {
        var page = RenderSchedulePage(ScheduleVoyage.ManageVoyagesPermission);

        page.Find("#schedule-vessel-search").Focus();
        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll(".schedule-vessel-results button")));

        var lookupsAfterFirstFocus = VesselLookups();

        page.Find("#schedule-vessel-search").Focus();

        Assert.Equal(lookupsAfterFirstFocus, VesselLookups());
    }

    [Fact]
    public void Clearing_the_search_lists_every_vessel_again()
    {
        var page = RenderSchedulePage(ScheduleVoyage.ManageVoyagesPermission);

        page.Find("#schedule-vessel-search").Input("Northern");
        page.WaitForAssertion(() => Assert.Contains(
            requestedUris,
            uri => uri.Query.Contains("search=Northern", StringComparison.Ordinal)));

        page.Find("#schedule-vessel-search").Input(string.Empty);

        page.WaitForAssertion(() => Assert.Contains(
            "search=&",
            requestedUris[^1].Query,
            StringComparison.Ordinal));
    }

    [Fact]
    public void Options_carry_the_identifiers_that_keyboard_scrolling_targets()
    {
        var page = RenderSchedulePage(ScheduleVoyage.ManageVoyagesPermission);

        page.Find("#schedule-vessel-search").Focus();

        page.WaitForAssertion(() => Assert.Equal(
            ScheduleVoyage.VesselOptionId(0),
            page.Find(".schedule-vessel-results button").GetAttribute("id")));
    }

    [Fact]
    public void An_empty_register_is_reported_rather_than_left_blank()
    {
        respond = _ => Json(
            HttpStatusCode.OK,
            """{"items":[],"pageNumber":1,"pageSize":25,"totalCount":0}""");

        var page = RenderSchedulePage(ScheduleVoyage.ManageVoyagesPermission);

        page.Find("#schedule-vessel-search").Focus();

        page.WaitForAssertion(() => Assert.Contains(
            "No vessels are registered yet.",
            page.Find(".schedule-hint").TextContent,
            StringComparison.Ordinal));
    }

    private List<Uri> VesselLookups()
    {
        return requestedUris
            .Where(uri => uri.AbsolutePath == "/api/fleet/vessels")
            .ToList();
    }

    private void SelectFirstVessel(IRenderedComponent<ScheduleVoyage> page)
    {
        page.Find("#schedule-vessel-search").Input("Northern");
        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll(".schedule-vessel-results button")));
        page.Find(".schedule-vessel-results button").Click();
        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll(".schedule-vessel-selected")));
    }

    private IRenderedComponent<ScheduleVoyage> RenderSchedulePage(params string[] permissions)
    {
        var httpClient = new HttpClient(new StubHttpMessageHandler(request =>
        {
            requestedUris.Add(request.RequestUri!);

            if (request.Method != HttpMethod.Get)
            {
                writes.Add((
                    request.RequestUri!.AbsolutePath,
                    request.RequestUri.Query,
                    request.Content?.ReadAsStringAsync().GetAwaiter().GetResult() ?? string.Empty));
            }

            return respond(request);
        }))
        {
            BaseAddress = new Uri("https://api.harborops.test/"),
        };

        Services.AddSingleton(new VoyagesApiClient(httpClient));
        Services.AddSingleton(new FleetApiClient(httpClient));
        Services.AddSingleton<IHttpContextAccessor>(new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    permissions
                        .Select(permission => new Claim(
                            CurrentUserAuthorizationState.PermissionClaimType,
                            permission))
                        .ToArray(),
                    "TestAuthentication")),
            },
        });
        Services.AddScoped<CurrentUserAuthorizationState>();

        return Render<ScheduleVoyage>();
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
