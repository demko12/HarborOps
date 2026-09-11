using System.Net;
using System.Security.Claims;
using System.Text;
using Bunit;
using HarborOps.Web.Api;
using HarborOps.Web.Api.Cargo;
using HarborOps.Web.Components.Pages.Cargo;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Sections;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace HarborOps.Web.Tests.Components;

public sealed class CargoContainerControlPanelTests : BunitContext
{
    private const string ContainerId = "70000000-0000-0000-0000-000000000006";
    private const string ActorUserId = "50000000-0000-0000-0000-000000000005";

    private const string PendingControl = """
        {"containerId":"70000000-0000-0000-0000-000000000006","containerNumber":"MSKU1234567","containerType":"40HC","isReefer":false,"sealNumber":null,"sealStatus":"None","state":"Pending","latestInspectionResult":null,"latestInspectionNotes":null,"latestInspectionTemperatureCelsius":null,"latestInspectedAtUtc":null,"latestInspectedByUserId":null,"stateReason":null,"stateChangedByUserId":null,"stateChangedAtUtc":null,"canRelease":false,"releaseBlockedReason":"it has no intact seal"}
        """;

    private const string SealedControl = """
        {"containerId":"70000000-0000-0000-0000-000000000006","containerNumber":"MSKU1234567","containerType":"40HC","isReefer":false,"sealNumber":"ML458821","sealStatus":"Intact","state":"Pending","latestInspectionResult":"Passed","latestInspectionNotes":"Doors aligned","latestInspectionTemperatureCelsius":null,"latestInspectedAtUtc":"2026-09-07T09:15:00Z","latestInspectedByUserId":"50000000-0000-0000-0000-000000000005","stateReason":null,"stateChangedByUserId":null,"stateChangedAtUtc":null,"canRelease":true,"releaseBlockedReason":null}
        """;

    private const string UninspectedControl = """
        {"containerId":"70000000-0000-0000-0000-000000000006","containerNumber":"MSKU1234567","containerType":"40HC","isReefer":false,"sealNumber":"ML458821","sealStatus":"Intact","state":"Pending","latestInspectionResult":null,"latestInspectionNotes":null,"latestInspectionTemperatureCelsius":null,"latestInspectedAtUtc":null,"latestInspectedByUserId":null,"stateReason":null,"stateChangedByUserId":null,"stateChangedAtUtc":null,"canRelease":false,"releaseBlockedReason":"it has not been inspected yet"}
        """;

    private const string ReleasedControl = """
        {"containerId":"70000000-0000-0000-0000-000000000006","containerNumber":"MSKU1234567","containerType":"40HC","isReefer":false,"sealNumber":"ML458821","sealStatus":"Intact","state":"Released","latestInspectionResult":"Passed","latestInspectionNotes":"Doors aligned","latestInspectionTemperatureCelsius":null,"latestInspectedAtUtc":"2026-09-07T09:15:00Z","latestInspectedByUserId":"50000000-0000-0000-0000-000000000005","stateReason":"Cleared for loading","stateChangedByUserId":"50000000-0000-0000-0000-000000000005","stateChangedAtUtc":"2026-09-07T10:30:00Z","canRelease":false,"releaseBlockedReason":"the container is already released"}
        """;

    private const string HeldControl = """
        {"containerId":"70000000-0000-0000-0000-000000000006","containerNumber":"MSKU1234567","containerType":"40HC","isReefer":false,"sealNumber":"ML458821","sealStatus":"Intact","state":"Held","latestInspectionResult":"Passed","latestInspectionNotes":"Doors aligned","latestInspectionTemperatureCelsius":null,"latestInspectedAtUtc":"2026-09-07T09:15:00Z","latestInspectedByUserId":"50000000-0000-0000-0000-000000000005","stateReason":"Damaged corner post","stateChangedByUserId":"50000000-0000-0000-0000-000000000005","stateChangedAtUtc":"2026-09-07T11:45:00Z","canRelease":false,"releaseBlockedReason":"the container is held"}
        """;

    private const string FailedInspectionControl = """
        {"containerId":"70000000-0000-0000-0000-000000000006","containerNumber":"MSKU1234567","containerType":"40HC","isReefer":false,"sealNumber":"ML458821","sealStatus":"Intact","state":"Pending","latestInspectionResult":"Failed","latestInspectionNotes":"Door seal damaged","latestInspectionTemperatureCelsius":null,"latestInspectedAtUtc":"2026-09-07T09:15:00Z","latestInspectedByUserId":"50000000-0000-0000-0000-000000000005","stateReason":null,"stateChangedByUserId":null,"stateChangedAtUtc":null,"canRelease":false,"releaseBlockedReason":"its latest inspection did not pass"}
        """;

    private const string ReeferControl = """
        {"containerId":"70000000-0000-0000-0000-000000000006","containerNumber":"MSKU1234567","containerType":"40RH","isReefer":true,"sealNumber":"ML458821","sealStatus":"Intact","state":"Pending","latestInspectionResult":"Passed","latestInspectionNotes":"Cooling steady","latestInspectionTemperatureCelsius":-18.5,"latestInspectedAtUtc":"2026-09-07T09:15:00Z","latestInspectedByUserId":"50000000-0000-0000-0000-000000000005","stateReason":null,"stateChangedByUserId":null,"stateChangedAtUtc":null,"canRelease":true,"releaseBlockedReason":null}
        """;

    private const string History = """
        [{"containerControlEventId":"90000000-0000-0000-0000-000000000001","action":"SealAssigned","fromState":"Pending","toState":"Pending","sealNumber":"ML458821","inspectionResult":null,"temperatureCelsius":null,"notes":null,"actorUserId":"50000000-0000-0000-0000-000000000005","occurredUtc":"2026-09-07T08:00:00Z"},{"containerControlEventId":"90000000-0000-0000-0000-000000000002","action":"InspectionRecorded","fromState":"Pending","toState":"Pending","sealNumber":null,"inspectionResult":"Passed","temperatureCelsius":null,"notes":"Doors aligned","actorUserId":"50000000-0000-0000-0000-000000000005","occurredUtc":"2026-09-07T09:15:00Z"}]
        """;

    private readonly List<HttpRequestMessage> requests = [];
    private readonly List<string> requestBodies = [];

    private Func<HttpRequestMessage, Task<HttpResponseMessage>> respond =
        _ => Json(HttpStatusCode.OK, View(PendingControl));

    public CargoContainerControlPanelTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public void The_control_is_requested_for_the_container()
    {
        RenderPanel();

        Assert.Equal(
            $"/api/cargo/containers/{ContainerId}/control",
            requests[0].RequestUri!.AbsolutePath);
    }

    [Fact]
    public void A_container_without_a_seal_is_reported_as_not_released()
    {
        var panel = RenderPanel();

        Assert.Contains(
            "Awaiting release",
            panel.Find(".control-status").TextContent,
            StringComparison.Ordinal);
        Assert.Contains("No seal assigned", panel.Markup, StringComparison.Ordinal);
        Assert.Contains(
            "Assign a seal before inspecting",
            panel.Markup,
            StringComparison.Ordinal);
    }

    [Fact]
    public void The_blocking_reason_is_explained_in_words()
    {
        var panel = RenderPanel();

        Assert.Contains(
            "no intact seal",
            panel.Find(".control-blocked").TextContent,
            StringComparison.Ordinal);
    }

    [Fact]
    public void A_held_container_explains_the_hold_and_shows_its_reason()
    {
        respond = _ => Json(HttpStatusCode.OK, View(HeldControl));

        var panel = RenderPanel();

        Assert.Contains("On hold", panel.Find(".control-status").TextContent, StringComparison.Ordinal);
        Assert.Contains("on hold", panel.Find(".control-blocked").TextContent, StringComparison.Ordinal);
        Assert.Contains(
            "Damaged corner post",
            panel.Find(".control-state-reason").TextContent,
            StringComparison.Ordinal);
    }

    [Fact]
    public void A_sealed_container_is_told_it_still_awaits_an_inspection()
    {
        respond = _ => Json(HttpStatusCode.OK, View(UninspectedControl));

        var panel = RenderPanel();

        var blocked = panel.Find(".control-blocked").TextContent;

        Assert.Contains("has not been inspected yet", blocked, StringComparison.Ordinal);
        Assert.DoesNotContain("did not pass", blocked, StringComparison.Ordinal);
    }

    [Fact]
    public void A_failed_inspection_is_named_as_the_blocking_reason()
    {
        respond = _ => Json(HttpStatusCode.OK, View(FailedInspectionControl));

        var panel = RenderPanel();

        Assert.Contains(
            "did not pass",
            panel.Find(".control-blocked").TextContent,
            StringComparison.Ordinal);
        Assert.Contains(
            "Failed",
            panel.Find(".control-inspection-result").TextContent,
            StringComparison.Ordinal);
    }

    [Fact]
    public void A_released_container_shows_the_actor_and_the_timestamp()
    {
        respond = _ => Json(HttpStatusCode.OK, View(ReleasedControl));

        var panel = RenderPanel();

        Assert.Contains("Released", panel.Find(".control-status").TextContent, StringComparison.Ordinal);
        Assert.Contains(
            "2026-09-07 10:30",
            panel.Find(".control-audit-time").TextContent,
            StringComparison.Ordinal);
        var actor = panel.Find(".control-audit-actor");

        Assert.Contains("50000000", actor.TextContent, StringComparison.Ordinal);
        Assert.Equal(ActorUserId, actor.GetAttribute("title"));
    }

    [Fact]
    public void A_reefer_container_offers_a_temperature_field_and_shows_the_recorded_value()
    {
        respond = _ => Json(HttpStatusCode.OK, View(ReeferControl));

        var panel = RenderPanel();

        Assert.Single(panel.FindAll("#control-inspection-temperature"));
        Assert.Contains(
            "-18.5 °C",
            panel.Find(".control-inspection-temperature").TextContent,
            StringComparison.Ordinal);
    }

    [Fact]
    public void A_dry_container_offers_no_temperature_field()
    {
        var panel = RenderPanel();

        Assert.Empty(panel.FindAll("#control-inspection-temperature"));
    }

    [Fact]
    public void A_reader_sees_the_control_without_any_action()
    {
        respond = _ => Json(HttpStatusCode.OK, View(SealedControl));

        var panel = RenderPanel(CurrentUserAuthorizationState.CargoReadPermission);

        Assert.Empty(panel.FindAll(".control-seal-submit"));
        Assert.Empty(panel.FindAll(".control-inspection-submit"));
        Assert.Empty(panel.FindAll(".control-action-release"));
        Assert.Contains("not change it", panel.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void The_release_action_is_disabled_while_the_container_is_blocked()
    {
        var panel = RenderPanel();

        Assert.True(panel.Find(".control-action-release").HasAttribute("disabled"));
    }

    [Fact]
    public void The_hold_action_is_disabled_while_the_container_is_already_held()
    {
        respond = _ => Json(HttpStatusCode.OK, View(HeldControl));

        var panel = RenderPanel();

        Assert.True(panel.Find(".control-action-hold").HasAttribute("disabled"));
    }

    [Fact]
    public void Assigning_a_seal_posts_the_seal_number_and_reloads_the_control()
    {
        var panel = RenderPanel();

        panel.Find("#control-seal-number").Change("  ml458821  ");
        panel.Find(".control-seal-form").Submit();

        panel.WaitForAssertion(() => Assert.Equal(3, requests.Count));

        Assert.Equal(HttpMethod.Post, requests[1].Method);
        Assert.Equal(
            $"/api/cargo/containers/{ContainerId}/seal",
            requests[1].RequestUri!.AbsolutePath);
        Assert.Contains("ml458821", requestBodies[1], StringComparison.Ordinal);
        Assert.Equal(HttpMethod.Get, requests[2].Method);
    }

    [Fact]
    public void An_empty_seal_number_is_rejected_before_any_request()
    {
        var panel = RenderPanel();

        panel.Find(".control-seal-form").Submit();

        Assert.Single(requests);
        Assert.Contains("A seal number is required.", panel.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void A_seal_validation_error_from_the_api_is_shown_on_the_field()
    {
        respond = request => IsPost(request)
            ? Json(
                HttpStatusCode.BadRequest,
                """{"title":"One or more validation errors occurred.","status":400,"errors":{"sealNumber":["Seal number may only contain letters, digits and hyphens."]}}""")
            : Json(HttpStatusCode.OK, View(PendingControl));

        var panel = RenderPanel();

        panel.Find("#control-seal-number").Change("ML/458821");
        panel.Find(".control-seal-form").Submit();

        panel.WaitForAssertion(() => Assert.Contains(
            "letters, digits and hyphens",
            panel.Markup,
            StringComparison.Ordinal));
    }

    [Fact]
    public void A_duplicate_seal_conflict_is_shown_and_the_control_is_reloaded()
    {
        respond = request => IsPost(request)
            ? Json(
                HttpStatusCode.Conflict,
                """{"title":"Seal number is already in use.","status":409,"detail":"Seal number 'ML458821' is already active on another container."}""")
            : Json(HttpStatusCode.OK, View(PendingControl));

        var panel = RenderPanel();

        panel.Find("#control-seal-number").Change("ML458821");
        panel.Find(".control-seal-form").Submit();

        panel.WaitForAssertion(() => Assert.Contains(
            "already in use",
            panel.Markup,
            StringComparison.Ordinal));
        panel.WaitForAssertion(() => Assert.Equal(3, requests.Count));
    }

    [Fact]
    public void Recording_an_inspection_posts_the_result_and_the_notes()
    {
        respond = _ => Json(HttpStatusCode.OK, View(SealedControl));

        var panel = RenderPanel();

        panel.Find("#control-inspection-result").Change("Passed");
        panel.Find("#control-inspection-notes").Change("  Doors aligned  ");
        panel.Find(".control-inspection-form").Submit();

        panel.WaitForAssertion(() => Assert.Equal(3, requests.Count));

        Assert.Equal(
            $"/api/cargo/containers/{ContainerId}/inspections",
            requests[1].RequestUri!.AbsolutePath);
        Assert.Contains("Passed", requestBodies[1], StringComparison.Ordinal);
        Assert.Contains("Doors aligned", requestBodies[1], StringComparison.Ordinal);
    }

    [Fact]
    public void An_inspection_without_a_result_is_rejected_before_any_request()
    {
        respond = _ => Json(HttpStatusCode.OK, View(SealedControl));

        var panel = RenderPanel();

        panel.Find(".control-inspection-form").Submit();

        Assert.Single(requests);
        Assert.Contains("An inspection result is required.", panel.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Releasing_asks_for_confirmation_before_it_posts()
    {
        respond = _ => Json(HttpStatusCode.OK, View(SealedControl));

        var panel = RenderPanel();

        panel.Find(".control-action-release").Click();

        Assert.Contains("Release this container", panel.Markup, StringComparison.Ordinal);
        Assert.Single(requests);
    }

    [Fact]
    public void Confirming_a_release_posts_the_notes_and_reloads()
    {
        respond = _ => Json(HttpStatusCode.OK, View(SealedControl));

        var panel = RenderPanel();

        panel.Find(".control-action-release").Click();
        panel.Find("#control-action-notes").Change("Cleared for loading");
        panel.Find(".control-confirm").Click();

        panel.WaitForAssertion(() => Assert.Equal(3, requests.Count));

        Assert.Equal(
            $"/api/cargo/containers/{ContainerId}/release",
            requests[1].RequestUri!.AbsolutePath);
        Assert.Contains("Cleared for loading", requestBodies[1], StringComparison.Ordinal);
    }

    [Fact]
    public void Cancelling_a_release_dismisses_the_dialog_without_posting()
    {
        respond = _ => Json(HttpStatusCode.OK, View(SealedControl));

        var panel = RenderPanel();

        panel.Find(".control-action-release").Click();
        panel.Find(".operation-confirm-dismiss").Click();

        Assert.Empty(panel.FindAll(".operation-modal"));
        Assert.Single(requests);
    }

    [Fact]
    public void A_hold_without_a_reason_is_rejected_before_any_request()
    {
        respond = _ => Json(HttpStatusCode.OK, View(SealedControl));

        var panel = RenderPanel();

        panel.Find(".control-action-hold").Click();
        panel.Find(".control-confirm").Click();

        Assert.Contains("A hold reason is required.", panel.Markup, StringComparison.Ordinal);
        Assert.Single(requests);
    }

    [Fact]
    public void Confirming_a_hold_posts_the_reason()
    {
        respond = _ => Json(HttpStatusCode.OK, View(SealedControl));

        var panel = RenderPanel();

        panel.Find(".control-action-hold").Click();
        panel.Find("#control-action-notes").Change("Damaged corner post");
        panel.Find(".control-confirm").Click();

        panel.WaitForAssertion(() => Assert.Equal(3, requests.Count));

        Assert.Equal(
            $"/api/cargo/containers/{ContainerId}/hold",
            requests[1].RequestUri!.AbsolutePath);
        Assert.Contains("Damaged corner post", requestBodies[1], StringComparison.Ordinal);
    }

    [Fact]
    public void A_release_conflict_is_shown_in_the_dialog_and_the_control_is_reloaded()
    {
        respond = request => IsPost(request)
            ? Json(
                HttpStatusCode.Conflict,
                """{"title":"Container control transition is not allowed.","status":409,"detail":"The container is held."}""")
            : Json(HttpStatusCode.OK, View(SealedControl));

        var panel = RenderPanel();

        panel.Find(".control-action-release").Click();
        panel.Find(".control-confirm").Click();

        panel.WaitForAssertion(() => Assert.Contains(
            "transition is not allowed",
            panel.Markup,
            StringComparison.Ordinal));
        panel.WaitForAssertion(() => Assert.Equal(3, requests.Count));
    }

    [Fact]
    public void The_history_lists_every_recorded_action()
    {
        respond = _ => Json(HttpStatusCode.OK, View(SealedControl, History));

        var panel = RenderPanel();

        panel.Find(".control-history-open").Click();

        var entries = panel.FindAll(".control-timeline-entry");

        Assert.Equal(2, entries.Count);
        Assert.Contains("Seal assigned", entries[0].TextContent, StringComparison.Ordinal);
        Assert.Contains("Inspection recorded", entries[1].TextContent, StringComparison.Ordinal);
        Assert.Contains("2026-09-07 08:00", entries[0].TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void The_history_is_hidden_when_nothing_has_happened_yet()
    {
        var panel = RenderPanel();

        Assert.Empty(panel.FindAll(".control-history-open"));
    }

    [Fact]
    public void The_history_dialog_can_be_closed()
    {
        respond = _ => Json(HttpStatusCode.OK, View(SealedControl, History));

        var panel = RenderPanel();

        panel.Find(".control-history-open").Click();
        panel.Find(".operation-confirm-dismiss").Click();

        Assert.Empty(panel.FindAll(".control-history-modal"));
    }

    [Fact]
    public void A_failed_load_can_be_retried()
    {
        respond = _ => Json(
            HttpStatusCode.InternalServerError,
            """{"title":"Unable to load the container control","status":500}""");

        var panel = RenderPanel();

        panel.WaitForAssertion(() => Assert.NotEmpty(panel.FindAll(".api-error-state button")));

        respond = _ => Json(HttpStatusCode.OK, View(SealedControl));
        panel.Find(".api-error-state button").Click();

        panel.WaitForAssertion(() => Assert.Contains("ML458821", panel.Markup, StringComparison.Ordinal));
        Assert.Equal(2, requests.Count);
    }

    [Fact]
    public void An_unknown_container_reports_the_failure()
    {
        respond = _ => Json(
            HttpStatusCode.NotFound,
            """{"title":"Container not found.","status":404}""");

        var panel = RenderPanel();

        panel.WaitForAssertion(() => Assert.Contains(
            "Container not found.",
            panel.Markup,
            StringComparison.Ordinal));
    }

    [Fact]
    public void The_steps_report_where_the_container_stands()
    {
        respond = _ => Json(HttpStatusCode.OK, View(SealedControl));

        var panel = RenderPanel();
        var steps = panel.FindAll(".control-step");

        Assert.Equal(3, steps.Count);
        Assert.Contains("control-step-done", steps[0].ClassName, StringComparison.Ordinal);
        Assert.Contains("control-step-done", steps[1].ClassName, StringComparison.Ordinal);
        Assert.Contains("control-step-active", steps[2].ClassName, StringComparison.Ordinal);
    }

    [Fact]
    public void The_inspection_step_is_locked_until_a_seal_is_assigned()
    {
        var panel = RenderPanel();
        var steps = panel.FindAll(".control-step");

        Assert.Contains("control-step-active", steps[0].ClassName, StringComparison.Ordinal);
        Assert.Contains("control-step-locked", steps[1].ClassName, StringComparison.Ordinal);
        Assert.Empty(panel.FindAll(".control-inspection-form"));
    }

    [Fact]
    public void A_failed_inspection_marks_its_step_for_attention()
    {
        respond = _ => Json(HttpStatusCode.OK, View(FailedInspectionControl));

        var panel = RenderPanel();

        Assert.Contains(
            "control-step-attention",
            panel.FindAll(".control-step")[1].ClassName,
            StringComparison.Ordinal);
    }

    [Fact]
    public void A_sealed_container_offers_a_replacement_instead_of_an_open_form()
    {
        respond = _ => Json(HttpStatusCode.OK, View(SealedControl));

        var panel = RenderPanel();

        Assert.Empty(panel.FindAll(".control-seal-form"));
        Assert.Contains(
            "ML458821",
            panel.Find(".control-seal").TextContent,
            StringComparison.Ordinal);

        panel.Find(".control-seal-replace").Click();

        Assert.NotNull(panel.Find(".control-seal-form"));
    }

    [Fact]
    public void Breaking_a_seal_asks_for_confirmation_before_it_posts()
    {
        respond = _ => Json(HttpStatusCode.OK, View(SealedControl));

        var panel = RenderPanel();
        panel.Find(".control-seal-break").Click();

        Assert.Contains("Record this seal as broken", panel.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain(requests, IsPost);
    }

    [Fact]
    public void Confirming_a_seal_break_posts_the_notes()
    {
        respond = request => IsPost(request)
            ? Json(HttpStatusCode.OK, SealedControl)
            : Json(HttpStatusCode.OK, View(SealedControl));

        var panel = RenderPanel();
        panel.Find(".control-seal-break").Click();
        panel.Find("#control-action-notes").Change("Opened for customs");
        panel.Find(".control-confirm").Click();

        panel.WaitForAssertion(() => Assert.Contains(requests, IsPost));

        var post = requests.Find(IsPost)!;

        Assert.Equal(
            $"/api/cargo/containers/{ContainerId}/seal/break",
            post.RequestUri!.AbsolutePath);
        Assert.Contains(
            "Opened for customs",
            requestBodies[requests.IndexOf(post)],
            StringComparison.Ordinal);
    }

    [Fact]
    public void A_reader_is_not_offered_the_seal_break()
    {
        respond = _ => Json(HttpStatusCode.OK, View(SealedControl));

        var panel = RenderPanel(CurrentUserAuthorizationState.CargoReadPermission);

        Assert.Empty(panel.FindAll(".control-seal-break"));
    }

    [Fact]
    public void A_seal_replacement_can_be_cancelled()
    {
        respond = _ => Json(HttpStatusCode.OK, View(SealedControl));

        var panel = RenderPanel();
        panel.Find(".control-seal-replace").Click();
        panel.Find(".control-seal-cancel").Click();

        Assert.Empty(panel.FindAll(".control-seal-form"));
        Assert.DoesNotContain(requests, IsPost);
    }

    [Fact]
    public void A_held_container_explains_how_the_hold_is_lifted()
    {
        respond = _ => Json(HttpStatusCode.OK, View(HeldControl));

        var panel = RenderPanel();

        Assert.Contains(
            "Record a passing inspection",
            panel.Find(".control-hold-hint").TextContent,
            StringComparison.Ordinal);
    }

    [Fact]
    public void The_history_shows_a_readable_actor_with_the_full_id_on_hover()
    {
        respond = _ => Json(HttpStatusCode.OK, View(SealedControl, History));

        var panel = RenderPanel();
        panel.Find(".control-history-open").Click();

        var actor = panel.FindAll(".control-timeline-actor")[0];

        Assert.DoesNotContain(ActorUserId, actor.TextContent, StringComparison.Ordinal);
        Assert.Equal(ActorUserId, actor.GetAttribute("title"));
    }

    private static bool IsPost(HttpRequestMessage request)
    {
        return request.Method == HttpMethod.Post;
    }

    private static string View(string control, string history = "[]")
    {
        return $$"""{"control":{{control}},"history":{{history}}}""";
    }

    private static Task<HttpResponseMessage> Json(HttpStatusCode statusCode, string json)
    {
        return Task.FromResult(new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/problem+json"),
        });
    }

    private IRenderedComponent<ControlOverlayHost> RenderPanel(params string[] permissions)
    {
        var httpClient = new HttpClient(new StubHttpMessageHandler(async (request, _) =>
        {
            requests.Add(request);
            requestBodies.Add(request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(CancellationToken.None));

            return await respond(request);
        }))
        {
            BaseAddress = new Uri("https://api.harborops.test/"),
        };

        Services.AddSingleton(new CargoBookingsApiClient(httpClient));
        ConfigureAuthorization(permissions.Length == 0
            ? [CurrentUserAuthorizationState.CargoManagePermission]
            : permissions);

        var panel = Render<ControlOverlayHost>(parameters => parameters
            .Add(component => component.ContainerId, Guid.Parse(ContainerId)));
        panel.WaitForAssertion(() => Assert.NotEmpty(requests));

        return panel;
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

    internal sealed class ControlOverlayHost : ComponentBase
    {
        [Parameter]
        public Guid ContainerId { get; set; }

        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            ArgumentNullException.ThrowIfNull(builder);

            builder.OpenComponent<CargoContainerControlPanel>(0);
            builder.AddComponentParameter(
                1,
                nameof(CargoContainerControlPanel.ContainerId),
                ContainerId);
            builder.AddComponentParameter(
                2,
                nameof(CargoContainerControlPanel.ContainerNumber),
                "MSKU1234567");
            builder.CloseComponent();

            builder.OpenComponent<SectionOutlet>(3);
            builder.AddComponentParameter(
                4,
                nameof(SectionOutlet.SectionName),
                "harborops-overlay");
            builder.CloseComponent();
        }
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
