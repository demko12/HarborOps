using System.Net;
using System.Security.Claims;
using System.Text;
using Bunit;
using HarborOps.Web.Api;
using HarborOps.Web.Api.Documents;
using HarborOps.Web.Api.Fleet;
using HarborOps.Web.Components.Pages.Documents;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace HarborOps.Web.Tests.Components;

public sealed class DocumentRequirementsPanelTests : BunitContext
{
    private const string VesselId = "40000000-0000-0000-0000-000000000004";
    private const string RequirementId = "11111111-1111-1111-1111-111111111111";
    private const string Version = "33333333-3333-3333-3333-333333333333";

    private readonly List<HttpRequestMessage> requests = [];
    private readonly List<string> requestBodies = [];

    private Func<HttpRequestMessage, Task<HttpResponseMessage>> respondToList =
        _ => Json(HttpStatusCode.OK, Page(Row()));

    private Func<HttpRequestMessage, Task<HttpResponseMessage>> respondToCreate =
        _ => Json(HttpStatusCode.Created, Row());

    private Func<HttpRequestMessage, Task<HttpResponseMessage>> respondToUpdate =
        _ => Json(HttpStatusCode.OK, Row());

    private Func<HttpRequestMessage, Task<HttpResponseMessage>> respondToDisable =
        _ => Json(HttpStatusCode.OK, Row(isEnabled: false));

    [Fact]
    public void Rows_are_shown_with_vessel_names_resolved_through_the_fleet_lookup()
    {
        var panel = RenderPanel();

        panel.WaitForAssertion(() =>
        {
            Assert.Contains("Aurora Borealis", panel.Markup, StringComparison.Ordinal);
            Assert.Contains("Safety Management Certificate", panel.Markup, StringComparison.Ordinal);
            Assert.Contains("30 days", panel.Markup, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void A_loading_state_is_shown_until_the_list_arrives()
    {
        var gate = new TaskCompletionSource();
        respondToList = async _ =>
        {
            await gate.Task;

            return await Json(HttpStatusCode.OK, Page(Row()));
        };

        var panel = RenderPanel();

        panel.WaitForAssertion(() => Assert.Contains("Loading document requirements...", panel.Markup, StringComparison.Ordinal));

        gate.SetResult();

        panel.WaitForAssertion(() =>
        {
            Assert.DoesNotContain("Loading document requirements...", panel.Markup, StringComparison.Ordinal);
            Assert.Contains("Safety Management Certificate", panel.Markup, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void An_empty_list_shows_the_empty_state()
    {
        respondToList = _ => Json(HttpStatusCode.OK, Page());

        var panel = RenderPanel();

        panel.WaitForAssertion(() => Assert.Contains("No document requirements", panel.Markup, StringComparison.Ordinal));
    }

    [Fact]
    public void A_failed_load_can_be_retried()
    {
        var failing = true;
        respondToList = _ => failing
            ? Json(HttpStatusCode.InternalServerError, """{"title":"Server error","detail":"Try again."}""")
            : Json(HttpStatusCode.OK, Page(Row()));

        var panel = RenderPanel();
        panel.WaitForAssertion(() => Assert.Contains("Server error", panel.Markup, StringComparison.Ordinal));

        failing = false;
        panel.Find(".api-error-state button").Click();

        panel.WaitForAssertion(() => Assert.Contains("Safety Management Certificate", panel.Markup, StringComparison.Ordinal));
    }

    [Fact]
    public void Read_only_users_do_not_get_add_edit_or_disable_actions()
    {
        var panel = RenderPanel("documents.read");

        panel.WaitForAssertion(() => Assert.Contains("History", panel.Markup, StringComparison.Ordinal));
        Assert.Empty(panel.FindAll(".requirements-add-button"));
        Assert.DoesNotContain(panel.FindAll(".requirements-row-action"), button => button.TextContent == "Edit");
        Assert.DoesNotContain(panel.FindAll(".requirements-row-action"), button => button.TextContent == "Disable");
    }

    [Fact]
    public void Server_validation_errors_are_shown_in_the_add_form()
    {
        respondToCreate = _ => Json(
            HttpStatusCode.BadRequest,
            """{"title":"One or more validation errors occurred.","errors":{"minimumRemainingValidityDays":["Minimum remaining validity must be between 0 and 365 days."]}}""");
        var panel = RenderPanel();
        panel.WaitForAssertion(() => Assert.NotEmpty(panel.FindAll(".requirements-add-button")));

        panel.Find(".requirements-add-button").Click();
        panel.Find("#requirement-days").Change("400");
        panel.Find(".requirements-form").Submit();

        panel.WaitForAssertion(() => Assert.Contains(
            "Minimum remaining validity must be between 0 and 365 days.",
            panel.Find(".requirements-validation").TextContent,
            StringComparison.Ordinal));
    }

    [Fact]
    public void A_successful_add_closes_the_form_and_reloads_the_list()
    {
        var panel = RenderPanel();
        panel.WaitForAssertion(() => Assert.NotEmpty(panel.FindAll(".requirements-add-button")));
        var listRequestsBefore = requests.Count(IsListRequest);

        panel.Find(".requirements-add-button").Click();
        panel.Find("#requirement-type").Change("7");
        panel.Find("#requirement-reason").Change("Flag state circular 12/2026.");
        panel.Find(".requirements-form").Submit();

        panel.WaitForAssertion(() =>
        {
            Assert.Contains("The requirement was added.", panel.Markup, StringComparison.Ordinal);
            Assert.Empty(panel.FindAll("#requirement-create-title"));
            Assert.True(requests.Count(IsListRequest) > listRequestsBefore);
        });
        Assert.Contains(requestBodies, body => body.Contains("\"documentTypeId\":7", StringComparison.Ordinal)
            && body.Contains("\"reason\":\"Flag state circular 12/2026.\"", StringComparison.Ordinal));
    }

    [Fact]
    public void Editing_sends_the_reason_and_the_loaded_version()
    {
        var panel = RenderPanel();
        panel.WaitForAssertion(() => Assert.NotEmpty(panel.FindAll(".requirements-row-action")));

        panel.FindAll(".requirements-row-action").First(button => button.TextContent == "Edit").Click();
        panel.Find("#requirement-edit-days").Change("60");
        panel.Find("#requirement-edit-reason").Change("Charter requires 60 days.");
        panel.Find(".requirements-form").Submit();

        panel.WaitForAssertion(() => Assert.Contains("The requirement was updated.", panel.Markup, StringComparison.Ordinal));
        var body = Assert.Single(requestBodies);
        Assert.Contains($"\"version\":\"{Version}\"", body, StringComparison.Ordinal);
        Assert.Contains("\"reason\":\"Charter requires 60 days.\"", body, StringComparison.Ordinal);
        Assert.Contains("\"minimumRemainingValidityDays\":60", body, StringComparison.Ordinal);
    }

    [Fact]
    public void A_concurrent_edit_keeps_the_form_open_and_offers_a_reload()
    {
        respondToUpdate = _ => Json(
            HttpStatusCode.Conflict,
            """{"title":"Document requirement was modified concurrently.","detail":"Reload and try again."}""");
        var panel = RenderPanel();
        panel.WaitForAssertion(() => Assert.NotEmpty(panel.FindAll(".requirements-row-action")));

        panel.FindAll(".requirements-row-action").First(button => button.TextContent == "Edit").Click();
        panel.Find("#requirement-edit-reason").Change("Edit.");
        panel.Find(".requirements-form").Submit();

        panel.WaitForAssertion(() =>
        {
            Assert.Contains("modified concurrently", panel.Find(".requirements-action-failure").TextContent, StringComparison.Ordinal);
            Assert.NotEmpty(panel.FindAll("#requirement-edit-title"));
        });

        panel.FindAll(".requirements-row-action").First(button => button.TextContent == "Reload latest version").Click();

        panel.WaitForAssertion(() => Assert.Empty(panel.FindAll("#requirement-edit-title")));
    }

    [Fact]
    public void Disabling_requires_confirmation_and_sends_the_reason()
    {
        var panel = RenderPanel();
        panel.WaitForAssertion(() => Assert.NotEmpty(panel.FindAll(".requirements-row-action-danger")));

        panel.Find(".requirements-row-action-danger").Click();

        Assert.Contains("stops counting toward readiness", panel.Find(".requirements-confirm-text").TextContent, StringComparison.Ordinal);
        Assert.Empty(requestBodies);

        panel.Find("#requirement-disable-reason").Change("Vessel sold.");
        panel.Find(".requirements-form").Submit();

        panel.WaitForAssertion(() => Assert.Contains("The requirement was disabled.", panel.Markup, StringComparison.Ordinal));
        Assert.Contains("\"reason\":\"Vessel sold.\"", Assert.Single(requestBodies), StringComparison.Ordinal);
    }

    [Fact]
    public void History_lists_each_revision_with_its_reason()
    {
        var panel = RenderPanel("documents.read");
        panel.WaitForAssertion(() => Assert.NotEmpty(panel.FindAll(".requirements-row-action")));

        panel.FindAll(".requirements-row-action").First(button => button.TextContent == "History").Click();

        panel.WaitForAssertion(() =>
        {
            var history = panel.Find(".requirements-history").TextContent;
            Assert.Contains("Rev. 2", history, StringComparison.Ordinal);
            Assert.Contains("Charter requires 30 days.", history, StringComparison.Ordinal);
            Assert.Contains("By fleet.manager@harborops.test", history, StringComparison.Ordinal);
            Assert.Contains("By 22222222-2222-2222-2222-222222222222", history, StringComparison.Ordinal);
        });
    }

    private IRenderedComponent<DocumentRequirementsPanel> RenderPanel(params string[] permissions)
    {
        var documentsHttpClient = new HttpClient(new StubHttpMessageHandler(async request =>
        {
            requests.Add(request);
            if (request.Content is not null)
            {
                requestBodies.Add(await request.Content.ReadAsStringAsync());
            }

            var path = request.RequestUri!.AbsolutePath;

            if (path == "/api/documents/types")
            {
                return await Json(HttpStatusCode.OK, """{"items":[{"documentTypeId":7,"name":"Safety Management Certificate","mandatoryValidityPeriodDays":365,"issuingAuthority":"Flag","requiresSurvey":true}],"pageSize":100,"totalCount":1}""");
            }

            if (request.Method == HttpMethod.Post && path == "/api/documents/requirements")
            {
                return await respondToCreate(request);
            }

            if (request.Method == HttpMethod.Put)
            {
                return await respondToUpdate(request);
            }

            if (path.EndsWith("/disable", StringComparison.Ordinal))
            {
                return await respondToDisable(request);
            }

            if (path == $"/api/documents/requirements/{RequirementId}")
            {
                return await Json(HttpStatusCode.OK, Details());
            }

            return await respondToList(request);
        }))
        {
            BaseAddress = new Uri("https://api.harborops.test/"),
        };

        var fleetHttpClient = new HttpClient(new StubHttpMessageHandler(_ =>
            Json(HttpStatusCode.OK, $$"""{"vessels":[{"id":"{{VesselId}}","name":"Aurora Borealis"}]}""")))
        {
            BaseAddress = new Uri("https://api.harborops.test/"),
        };

        Services.AddSingleton(new DocumentsApiClient(documentsHttpClient));
        Services.AddSingleton(new FleetApiClient(fleetHttpClient));
        Services.AddSingleton(CreateAuthorizationState(
            permissions.Length == 0 ? ["documents.read", "documents.manage"] : permissions));

        var panel = Render<DocumentRequirementsPanel>();
        panel.WaitForAssertion(() => Assert.NotEmpty(requests));

        return panel;
    }

    private static bool IsListRequest(HttpRequestMessage request) =>
        request.Method == HttpMethod.Get && request.RequestUri!.AbsolutePath == "/api/documents/requirements";

    private static string Row(bool isEnabled = true) =>
        $$"""
        {"id":"{{RequirementId}}","vesselId":"{{VesselId}}","documentTypeId":7,
        "documentTypeName":"Safety Management Certificate","isEnabled":{{(isEnabled ? "true" : "false")}},
        "minimumRemainingValidityDays":30,"instruction":null,"revision":1,
        "updatedByUserId":"22222222-2222-2222-2222-222222222222","updatedAtUtc":"2026-09-24T09:00:00Z","version":"{{Version}}"}
        """;

    private static string Page(params string[] rows) =>
        $$"""{"items":[{{string.Join(",", rows)}}],"pageNumber":1,"pageSize":25,"totalCount":{{rows.Length}}}""";

    private static string Details() =>
        $$"""
        {"id":"{{RequirementId}}","vesselId":"{{VesselId}}","documentTypeId":7,"documentTypeName":"Safety Management Certificate",
        "isEnabled":true,"minimumRemainingValidityDays":30,"instruction":null,"revision":2,
        "createdByUserId":"22222222-2222-2222-2222-222222222222","createdAtUtc":"2026-09-20T09:00:00Z",
        "updatedByUserId":"22222222-2222-2222-2222-222222222222","updatedAtUtc":"2026-09-24T09:00:00Z","version":"{{Version}}",
        "history":[
        {"id":"44444444-4444-4444-4444-444444444444","revision":2,"eventType":"Updated","reason":"Charter requires 30 days.","isEnabled":true,"minimumRemainingValidityDays":30,"instruction":null,"actorUserId":"22222222-2222-2222-2222-222222222222","actorEmail":"fleet.manager@harborops.test","occurredAtUtc":"2026-09-24T09:00:00Z"},
        {"id":"55555555-5555-5555-5555-555555555555","revision":1,"eventType":"Created","reason":null,"isEnabled":true,"minimumRemainingValidityDays":0,"instruction":null,"actorUserId":"22222222-2222-2222-2222-222222222222","actorEmail":null,"occurredAtUtc":"2026-09-20T09:00:00Z"}]}
        """;

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
