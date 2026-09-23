using System.Net;
using System.Security.Claims;
using System.Text;
using Bunit;
using HarborOps.Web.Api;
using HarborOps.Web.Api.Cargo;
using HarborOps.Web.Components.Pages.Cargo;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace HarborOps.Web.Tests.Components;

public sealed class CargoReeferPanelTests : BunitContext
{
    private const string ContainerId = "70000000-0000-0000-0000-000000000006";
    private const string ActorUserId = "50000000-0000-0000-0000-000000000005";
    private const string ReadingId = "80000000-0000-0000-0000-000000000001";
    private const string ExcursionId = "90000000-0000-0000-0000-000000000001";

    private const string ConfiguredProfile = """
        {"containerId":"70000000-0000-0000-0000-000000000006","containerNumber":"MSKU1234567","profile":{"containerId":"70000000-0000-0000-0000-000000000006","minimumCelsius":-2,"maximumCelsius":4,"targetCelsius":1,"version":2,"createdByUserId":"50000000-0000-0000-0000-000000000005","createdAtUtc":"2026-09-18T06:00:00Z","updatedByUserId":"50000000-0000-0000-0000-000000000005","updatedAtUtc":"2026-09-19T16:00:00Z"},"changes":[{"id":"a0000000-0000-0000-0000-000000000002","version":2,"fromMinimumCelsius":-5,"fromMaximumCelsius":2,"fromTargetCelsius":-1,"toMinimumCelsius":-2,"toMaximumCelsius":4,"toTargetCelsius":1,"reason":"Shipper tightened the cold chain","actorUserId":"50000000-0000-0000-0000-000000000005","occurredUtc":"2026-09-19T16:00:00Z"}],"openExcursionCount":0}
        """;

    private const string ProfileWithInspectionInRange = """
        {"containerId":"70000000-0000-0000-0000-000000000006","containerNumber":"MSKU1234567","profile":{"containerId":"70000000-0000-0000-0000-000000000006","minimumCelsius":-2,"maximumCelsius":4,"targetCelsius":1,"version":2,"createdByUserId":"50000000-0000-0000-0000-000000000005","createdAtUtc":"2026-09-18T06:00:00Z","updatedByUserId":"50000000-0000-0000-0000-000000000005","updatedAtUtc":"2026-09-19T16:00:00Z"},"changes":[],"openExcursionCount":0,"latestInspectionTemperatureCelsius":1}
        """;

    private const string ProfileWithInspectionOutOfRange = """
        {"containerId":"70000000-0000-0000-0000-000000000006","containerNumber":"MSKU1234567","profile":{"containerId":"70000000-0000-0000-0000-000000000006","minimumCelsius":-2,"maximumCelsius":4,"targetCelsius":1,"version":2,"createdByUserId":"50000000-0000-0000-0000-000000000005","createdAtUtc":"2026-09-18T06:00:00Z","updatedByUserId":"50000000-0000-0000-0000-000000000005","updatedAtUtc":"2026-09-19T16:00:00Z"},"changes":[],"openExcursionCount":0,"latestInspectionTemperatureCelsius":9}
        """;

    private const string MissingProfile = """
        {"containerId":"70000000-0000-0000-0000-000000000006","containerNumber":"MSKU1234567","profile":null,"changes":[],"openExcursionCount":0}
        """;

    private const string NoExcursions = """
        {"items":[],"openCount":0}
        """;

    private const string OpenExcursion = """
        {"items":[{"id":"90000000-0000-0000-0000-000000000001","containerId":"70000000-0000-0000-0000-000000000006","readingId":"80000000-0000-0000-0000-000000000001","direction":"Above","temperatureCelsius":6.5,"thresholdMinimumCelsius":-2,"thresholdMaximumCelsius":4,"measuredAtUtc":"2026-09-19T07:45:00Z","detectedAtUtc":"2026-09-19T08:00:00Z","status":"Open","disposition":null,"explanation":null,"resolvedByUserId":null,"resolvedAtUtc":null,"supersededByExcursionId":null,"supersededAtUtc":null}],"openCount":1}
        """;

    private const string ResolvedExcursion = """
        {"items":[{"id":"90000000-0000-0000-0000-000000000001","containerId":"70000000-0000-0000-0000-000000000006","readingId":"80000000-0000-0000-0000-000000000001","direction":"Above","temperatureCelsius":6.5,"thresholdMinimumCelsius":-2,"thresholdMaximumCelsius":4,"measuredAtUtc":"2026-09-19T07:45:00Z","detectedAtUtc":"2026-09-19T08:00:00Z","status":"Resolved","disposition":"CorrectiveAction","explanation":"Reefer unit restarted and verified","resolvedByUserId":"50000000-0000-0000-0000-000000000005","resolvedAtUtc":"2026-09-19T16:00:00Z","supersededByExcursionId":null,"supersededAtUtc":null}],"openCount":0}
        """;

    private const string NoReadings = """
        {"items":[],"pageNumber":1,"pageSize":10,"totalCount":0}
        """;

    private const string EffectiveReading = """
        {"items":[{"id":"80000000-0000-0000-0000-000000000001","containerId":"70000000-0000-0000-0000-000000000006","temperatureCelsius":6.5,"measuredAtUtc":"2026-09-19T07:45:00Z","recordedAtUtc":"2026-09-19T08:00:00Z","notes":"Manual round","recordedByUserId":"50000000-0000-0000-0000-000000000005","thresholdMinimumCelsius":-2,"thresholdMaximumCelsius":4,"thresholdTargetCelsius":1,"thresholdProfileVersion":1,"status":"Effective","isWithinThresholds":false,"correctsReadingId":null,"supersededByReadingId":null,"correctionReason":null,"correctedByUserId":null,"correctedAtUtc":null,"voidReason":null,"voidedByUserId":null,"voidedAtUtc":null}],"pageNumber":1,"pageSize":10,"totalCount":1}
        """;

    private const string SupersededReading = """
        {"items":[{"id":"80000000-0000-0000-0000-000000000001","containerId":"70000000-0000-0000-0000-000000000006","temperatureCelsius":6.5,"measuredAtUtc":"2026-09-19T07:45:00Z","recordedAtUtc":"2026-09-19T08:00:00Z","notes":null,"recordedByUserId":"50000000-0000-0000-0000-000000000005","thresholdMinimumCelsius":-2,"thresholdMaximumCelsius":4,"thresholdTargetCelsius":1,"thresholdProfileVersion":1,"status":"Superseded","isWithinThresholds":false,"correctsReadingId":null,"supersededByReadingId":"80000000-0000-0000-0000-000000000002","correctionReason":"Probe was misread","correctedByUserId":"50000000-0000-0000-0000-000000000005","correctedAtUtc":"2026-09-19T16:00:00Z","voidReason":null,"voidedByUserId":null,"voidedAtUtc":null}],"pageNumber":1,"pageSize":10,"totalCount":1}
        """;

    private const string VoidedReading = """
        {"items":[{"id":"80000000-0000-0000-0000-000000000001","containerId":"70000000-0000-0000-0000-000000000006","temperatureCelsius":6.5,"measuredAtUtc":"2026-09-19T07:45:00Z","recordedAtUtc":"2026-09-19T08:00:00Z","notes":null,"recordedByUserId":"50000000-0000-0000-0000-000000000005","thresholdMinimumCelsius":-2,"thresholdMaximumCelsius":4,"thresholdTargetCelsius":1,"thresholdProfileVersion":1,"status":"Voided","isWithinThresholds":false,"correctsReadingId":null,"supersededByReadingId":null,"correctionReason":null,"correctedByUserId":null,"correctedAtUtc":null,"voidReason":"Duplicate entry","voidedByUserId":"50000000-0000-0000-0000-000000000005","voidedAtUtc":"2026-09-19T16:00:00Z"}],"pageNumber":1,"pageSize":10,"totalCount":1}
        """;

    private const string PagedReadings = """
        {"items":[{"id":"80000000-0000-0000-0000-000000000001","containerId":"70000000-0000-0000-0000-000000000006","temperatureCelsius":1,"measuredAtUtc":"2026-09-19T07:45:00Z","recordedAtUtc":"2026-09-19T08:00:00Z","notes":null,"recordedByUserId":"50000000-0000-0000-0000-000000000005","thresholdMinimumCelsius":-2,"thresholdMaximumCelsius":4,"thresholdTargetCelsius":1,"thresholdProfileVersion":1,"status":"Effective","isWithinThresholds":true,"correctsReadingId":null,"supersededByReadingId":null,"correctionReason":null,"correctedByUserId":null,"correctedAtUtc":null,"voidReason":null,"voidedByUserId":null,"voidedAtUtc":null}],"pageNumber":1,"pageSize":10,"totalCount":24}
        """;

    private const string SavedProfileRow = """
        {"containerId":"70000000-0000-0000-0000-000000000006","minimumCelsius":-5,"maximumCelsius":2,"targetCelsius":-1,"version":3,"createdByUserId":"50000000-0000-0000-0000-000000000005","createdAtUtc":"2026-09-18T06:00:00Z","updatedByUserId":"50000000-0000-0000-0000-000000000005","updatedAtUtc":"2026-09-19T17:00:00Z"}
        """;

    private const string ResolvedExcursionRow = """
        {"id":"90000000-0000-0000-0000-000000000001","containerId":"70000000-0000-0000-0000-000000000006","readingId":"80000000-0000-0000-0000-000000000001","direction":"Above","temperatureCelsius":6.5,"thresholdMinimumCelsius":-2,"thresholdMaximumCelsius":4,"measuredAtUtc":"2026-09-19T07:45:00Z","detectedAtUtc":"2026-09-19T08:00:00Z","status":"Resolved","disposition":"CorrectiveAction","explanation":"Reefer unit restarted and verified","resolvedByUserId":"50000000-0000-0000-0000-000000000005","resolvedAtUtc":"2026-09-19T16:00:00Z","supersededByExcursionId":null,"supersededAtUtc":null}
        """;

    private readonly List<HttpRequestMessage> requests = [];
    private readonly List<string> requestBodies = [];

    private string profileResponse = ConfiguredProfile;
    private string excursionResponse = NoExcursions;
    private string readingResponse = NoReadings;
    private HttpStatusCode profileStatus = HttpStatusCode.OK;

    public CargoReeferPanelTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public void The_profile_excursions_and_readings_are_requested()
    {
        RenderPanel();

        var paths = requests.ConvertAll(request => request.RequestUri!.AbsolutePath);

        Assert.Contains($"/api/cargo/containers/{ContainerId}/reefer/profile", paths);
        Assert.Contains($"/api/cargo/containers/{ContainerId}/reefer/excursions", paths);
        Assert.Contains($"/api/cargo/containers/{ContainerId}/reefer/readings", paths);
    }

    [Fact]
    public void A_container_that_is_not_a_reefer_is_excluded_without_any_request()
    {
        var panel = RenderPanel(isReefer: false);

        Assert.Contains(
            CargoReeferPanel.NotAReeferMessage,
            panel.Markup,
            StringComparison.Ordinal);
        Assert.Empty(requests);
    }

    [Fact]
    public void A_missing_profile_asks_for_the_thresholds_first()
    {
        profileResponse = MissingProfile;

        var panel = RenderPanel();

        Assert.Contains(
            CargoReeferPanel.ProfileMissingMessage,
            panel.Markup,
            StringComparison.Ordinal);
        Assert.Empty(panel.FindAll("#reefer-temperature"));
    }

    [Fact]
    public void A_configured_profile_shows_its_thresholds_and_version()
    {
        var panel = RenderPanel();

        Assert.Contains("-2 °C", panel.Markup, StringComparison.Ordinal);
        Assert.Contains("4 °C", panel.Markup, StringComparison.Ordinal);
        Assert.Contains("v2", panel.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void The_threshold_history_is_revealed_on_demand()
    {
        var panel = RenderPanel();

        Assert.DoesNotContain(
            "Shipper tightened the cold chain",
            panel.Markup,
            StringComparison.Ordinal);

        panel.Find(".reefer-toggle").Click();

        Assert.Contains(
            "Shipper tightened the cold chain",
            panel.Markup,
            StringComparison.Ordinal);
    }

    [Fact]
    public void An_open_excursion_is_badged_and_explained_as_a_blocker()
    {
        excursionResponse = OpenExcursion;
        readingResponse = EffectiveReading;

        var panel = RenderPanel();

        Assert.Contains("1 open excursion", panel.Markup, StringComparison.Ordinal);
        Assert.Contains(
            CargoReeferPanel.BlockedReleaseMessage,
            panel.Markup,
            StringComparison.Ordinal);
    }

    [Fact]
    public void A_container_without_excursions_reports_a_clear_badge()
    {
        var panel = RenderPanel();

        Assert.Contains("No open excursions", panel.Markup, StringComparison.Ordinal);
        Assert.Empty(panel.FindAll(".reefer-blocker-banner"));
    }

    [Fact]
    public void An_open_excursion_offers_a_resolution_form_to_a_manager()
    {
        excursionResponse = OpenExcursion;
        readingResponse = EffectiveReading;

        var panel = RenderPanel();
        panel.Find(".reefer-excursion-open button").Click();

        Assert.NotNull(panel.Find("#reefer-disposition"));
        Assert.NotNull(panel.Find("#reefer-explanation"));
    }

    [Fact]
    public void An_open_excursion_is_read_only_for_a_reader()
    {
        excursionResponse = OpenExcursion;
        readingResponse = EffectiveReading;

        var panel = RenderPanel(permissions: CurrentUserAuthorizationState.CargoReadPermission);

        Assert.Contains("1 open excursion", panel.Markup, StringComparison.Ordinal);
        Assert.Empty(panel.FindAll(".reefer-excursion-open button"));
        Assert.Empty(panel.FindAll("#reefer-temperature"));
    }

    [Fact]
    public void A_resolved_excursion_shows_its_disposition_and_actor()
    {
        excursionResponse = ResolvedExcursion;
        readingResponse = EffectiveReading;

        var panel = RenderPanel();

        Assert.Contains("Corrective action", panel.Markup, StringComparison.Ordinal);
        Assert.Contains(
            "Reefer unit restarted and verified",
            panel.Markup,
            StringComparison.Ordinal);
        Assert.Contains("Resolved by", panel.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void An_out_of_range_reading_is_flagged_in_the_log()
    {
        readingResponse = EffectiveReading;

        var panel = RenderPanel();

        Assert.Contains("6.5 °C", panel.Markup, StringComparison.Ordinal);
        Assert.Contains("Out of range", panel.Markup, StringComparison.Ordinal);
        Assert.Contains("Thresholds v1", panel.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void A_superseded_reading_keeps_its_correction_history()
    {
        readingResponse = SupersededReading;

        var panel = RenderPanel();

        Assert.Contains("Probe was misread", panel.Markup, StringComparison.Ordinal);
        Assert.Contains("Corrected by", panel.Markup, StringComparison.Ordinal);
        Assert.Empty(panel.FindAll(".reefer-reading-actions"));
    }

    [Fact]
    public void A_voided_reading_keeps_its_void_reason()
    {
        readingResponse = VoidedReading;

        var panel = RenderPanel();

        Assert.Contains("Duplicate entry", panel.Markup, StringComparison.Ordinal);
        Assert.Contains("Voided by", panel.Markup, StringComparison.Ordinal);
        Assert.Empty(panel.FindAll(".reefer-reading-actions"));
    }

    [Fact]
    public void An_effective_reading_offers_a_correction_form()
    {
        readingResponse = EffectiveReading;

        var panel = RenderPanel();
        panel.FindAll(".reefer-reading-actions button")[0].Click();

        Assert.NotNull(panel.Find("#reefer-correct-reason"));
    }

    [Fact]
    public void Voiding_a_reading_asks_for_a_reason_first()
    {
        readingResponse = EffectiveReading;

        var panel = RenderPanel();
        panel.FindAll(".reefer-reading-actions button")[1].Click();

        Assert.NotNull(panel.Find("#reefer-void-reason"));

        panel.Find(".reefer-void-confirm .reefer-void-submit").Click();

        Assert.Contains("A void reason is required.", panel.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void An_empty_log_is_reported()
    {
        var panel = RenderPanel();

        Assert.Contains(
            "No temperature reading has been recorded",
            panel.Markup,
            StringComparison.Ordinal);
    }

    [Fact]
    public void A_long_log_is_paged()
    {
        readingResponse = PagedReadings;

        var panel = RenderPanel();

        Assert.Contains("Page 1 of 3", panel.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Editing_the_profile_sends_the_revised_thresholds_with_a_reason()
    {
        var panel = RenderPanel();

        panel.Find(".reefer-section-heading button").Click();
        panel.Find("#reefer-minimum").Change("-5");
        panel.Find("#reefer-target").Change("-1");
        panel.Find("#reefer-maximum").Change("2");
        panel.Find("#reefer-profile-reason").Change("Shipper tightened the cold chain");
        panel.Find(".reefer-profile-submit").Click();

        var index = requests.FindIndex(request => request.Method == HttpMethod.Put);

        Assert.True(index >= 0);
        Assert.Equal(
            $"/api/cargo/containers/{ContainerId}/reefer/profile",
            requests[index].RequestUri!.AbsolutePath);
        Assert.Contains("\"minimumCelsius\":-5", requestBodies[index], StringComparison.Ordinal);
        Assert.Contains(
            "Shipper tightened the cold chain",
            requestBodies[index],
            StringComparison.Ordinal);
    }

    [Fact]
    public void Editing_the_profile_refuses_to_submit_without_a_reason()
    {
        var panel = RenderPanel();

        panel.Find(".reefer-section-heading button").Click();
        panel.Find("#reefer-minimum").Change("-5");
        panel.Find("#reefer-target").Change("-1");
        panel.Find("#reefer-maximum").Change("2");
        panel.Find(".reefer-profile-submit").Click();

        Assert.Contains(
            "A reason is required when the thresholds change.",
            panel.Markup,
            StringComparison.Ordinal);
        Assert.DoesNotContain(requests, request => request.Method == HttpMethod.Put);
    }

    [Fact]
    public void Resolving_an_excursion_sends_the_disposition_and_explanation()
    {
        excursionResponse = OpenExcursion;
        readingResponse = EffectiveReading;

        var panel = RenderPanel();

        panel.Find(".reefer-excursion-open button").Click();
        panel.Find("#reefer-disposition").Change(CargoReeferPanel.CorrectiveActionDisposition);
        panel.Find("#reefer-explanation").Change("Reefer unit restarted and verified");
        panel.Find(".reefer-resolution-submit").Click();

        var index = requests.FindIndex(request =>
            request.RequestUri!.AbsolutePath.EndsWith("/resolve", StringComparison.Ordinal));

        Assert.True(index >= 0);
        Assert.Equal(
            $"/api/cargo/containers/{ContainerId}/reefer/excursions/{ExcursionId}/resolve",
            requests[index].RequestUri!.AbsolutePath);
        Assert.Contains(
            CargoReeferPanel.CorrectiveActionDisposition,
            requestBodies[index],
            StringComparison.Ordinal);
        Assert.Contains(
            "Reefer unit restarted and verified",
            requestBodies[index],
            StringComparison.Ordinal);
    }

    [Fact]
    public void A_failed_load_offers_a_retry()
    {
        profileStatus = HttpStatusCode.ServiceUnavailable;
        profileResponse = """
            {"title":"Unable to load the reefer monitoring profile","detail":"Try again."}
            """;

        var panel = RenderPanel();

        Assert.Contains(
            "Unable to load the reefer monitoring profile",
            panel.Markup,
            StringComparison.Ordinal);
        Assert.NotNull(panel.Find(".api-error-state button"));
    }

    [Fact]
    public void An_inspection_temperature_inside_the_profile_is_shown_without_a_warning()
    {
        profileResponse = ProfileWithInspectionInRange;

        var panel = RenderPanel();

        Assert.Contains(
            "Latest inspection temperature",
            panel.Markup,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            CargoReeferPanel.InspectionOutOfRangeMessage,
            panel.Markup,
            StringComparison.Ordinal);
    }

    [Fact]
    public void An_inspection_temperature_outside_the_profile_is_flagged()
    {
        profileResponse = ProfileWithInspectionOutOfRange;

        var panel = RenderPanel();

        Assert.Contains(
            CargoReeferPanel.InspectionOutOfRangeMessage,
            panel.Markup,
            StringComparison.Ordinal);
    }

    [Fact]
    public void An_absent_inspection_temperature_is_not_shown()
    {
        var panel = RenderPanel();

        Assert.DoesNotContain(
            "Latest inspection temperature",
            panel.Markup,
            StringComparison.Ordinal);
    }

    [Fact]
    public void The_reading_form_stays_closed_until_it_is_requested()
    {
        var panel = RenderPanel();

        Assert.Empty(panel.FindAll("#reefer-temperature"));

        panel.Find(".reefer-record-open").Click();

        Assert.NotNull(panel.Find("#reefer-temperature"));
    }

    [Fact]
    public void A_reader_is_not_offered_the_reading_form()
    {
        var panel = RenderPanel(permissions: CurrentUserAuthorizationState.CargoReadPermission);

        Assert.Empty(panel.FindAll(".reefer-record-open"));
        Assert.Empty(panel.FindAll("#reefer-temperature"));
    }

    [Fact]
    public void Recording_a_reading_sends_the_measurement()
    {
        var panel = RenderPanel();

        panel.Find(".reefer-record-open").Click();
        panel.Find("#reefer-temperature").Change("-1.5");
        panel.Find("#reefer-measured-at").Change("2026-09-19T07:45");
        panel.Find(".reefer-reading-form button[type=submit]").Click();

        var index = requests.FindIndex(request => request.Method == HttpMethod.Post
            && request.RequestUri!.AbsolutePath.EndsWith("/readings", StringComparison.Ordinal));

        Assert.True(index >= 0);
        Assert.Contains("\"temperatureCelsius\":-1.5", requestBodies[index], StringComparison.Ordinal);
    }

    private IRenderedComponent<CargoReeferPanel> RenderPanel(
        bool isReefer = true,
        params string[] permissions)
    {
        var httpClient = new HttpClient(new StubHttpMessageHandler(async (request, _) =>
        {
            requests.Add(request);
            requestBodies.Add(request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(CancellationToken.None));

            var path = request.RequestUri!.AbsolutePath;

            if (path.EndsWith("/resolve", StringComparison.Ordinal))
            {
                return await Json(HttpStatusCode.OK, ResolvedExcursionRow);
            }

            if (path.EndsWith("/profile", StringComparison.Ordinal))
            {
                return request.Method == HttpMethod.Put
                    ? await Json(HttpStatusCode.OK, SavedProfileRow)
                    : await Json(profileStatus, profileResponse);
            }

            if (path.EndsWith("/excursions", StringComparison.Ordinal))
            {
                return await Json(HttpStatusCode.OK, excursionResponse);
            }

            return await Json(HttpStatusCode.OK, readingResponse);
        }))
        {
            BaseAddress = new Uri("https://api.harborops.test/"),
        };

        Services.AddSingleton(new CargoBookingsApiClient(httpClient));
        ConfigureAuthorization(permissions.Length == 0
            ? [CurrentUserAuthorizationState.CargoManagePermission]
            : permissions);

        var panel = Render<CargoReeferPanel>(parameters => parameters
            .Add(component => component.ContainerId, Guid.Parse(ContainerId))
            .Add(component => component.ContainerNumber, "MSKU1234567")
            .Add(component => component.IsReefer, isReefer));

        if (isReefer)
        {
            var expectedRequests = profileStatus == HttpStatusCode.OK ? 3 : 1;

            panel.WaitForAssertion(() => Assert.True(requests.Count >= expectedRequests));
        }

        return panel;
    }

    private static Task<HttpResponseMessage> Json(HttpStatusCode statusCode, string json)
    {
        return Task.FromResult(new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        });
    }

    private void ConfigureAuthorization(params string[] permissions)
    {
        Services.RemoveAll<IHttpContextAccessor>();
        Services.RemoveAll<CurrentUserAuthorizationState>();
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
    }

    private sealed class StubHttpMessageHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responseFactory)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return responseFactory(request, cancellationToken);
        }
    }
}
