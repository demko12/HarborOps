using System.Net;
using System.Security.Claims;
using System.Text;
using Bunit;
using HarborOps.Web.Api;
using HarborOps.Web.Components.Pages.Crew;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace HarborOps.Web.Tests.Components;

public sealed class CrewQualificationPoliciesPageTests : BunitContext
{
    private static readonly Guid PositionId = Guid.Parse("50000000-0000-0000-0000-000000000001");
    private static readonly Guid PolicyId = Guid.Parse("60000000-0000-0000-0000-000000000001");

    private readonly List<(HttpMethod Method, Uri Uri)> requests = [];

    private Func<HttpRequestMessage, Task<HttpResponseMessage>> respondToPositions =
        _ => Json(HttpStatusCode.OK, $$"""
            {"items":[{"crewPositionId":"{{PositionId}}","rankName":"Bosun","department":"Deck",
            "seniorityLevel":"Senior","crewMemberUsageCount":0,"assignmentUsageCount":0}],
            "pageNumber":1,"pageSize":100,"totalCount":1}
            """);

    private Func<HttpRequestMessage, Task<HttpResponseMessage>> respondToList =
        _ => Json(HttpStatusCode.OK, Page());

    private Func<HttpRequestMessage, Task<HttpResponseMessage>> respondToDetail =
        _ => Json(HttpStatusCode.OK, Detail());

    private Func<HttpRequestMessage, Task<HttpResponseMessage>> respondToCreate =
        _ => Json(HttpStatusCode.Created, $$"""{"id":"{{PolicyId}}","crewPositionId":"{{PositionId}}","enabled":true,"concurrencyToken":"70000000-0000-0000-0000-000000000001","requiredTypes":["STCW-II/1"]}""");

    private Func<HttpRequestMessage, Task<HttpResponseMessage>> respondToUpdate =
        _ => Json(HttpStatusCode.OK, $$"""{"id":"{{PolicyId}}","enabled":true,"concurrencyToken":"70000000-0000-0000-0000-000000000002","requiredTypes":["STCW-VI/1"]}""");

    [Fact]
    public void Rows_are_rendered_with_rank_enabled_state_and_required_type_count()
    {
        respondToList = _ => Json(HttpStatusCode.OK, Page(Row()));

        var page = RenderPage();

        page.WaitForAssertion(() =>
        {
            var row = page.Find(".crew-positions-row");
            Assert.Contains("Bosun", row.TextContent, StringComparison.Ordinal);
            Assert.Contains("Enabled", row.TextContent, StringComparison.Ordinal);
            Assert.Contains("1", row.TextContent, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Empty_data_shows_the_empty_state()
    {
        var page = RenderPage();

        page.WaitForAssertion(() =>
        {
            Assert.Empty(page.FindAll(".crew-positions-row"));
            Assert.Contains("No qualification policies found", page.Markup, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Without_manage_permission_add_and_edit_controls_are_not_offered()
    {
        respondToList = _ => Json(HttpStatusCode.OK, Page(Row()));

        var page = RenderPage(CurrentUserAuthorizationState.CrewReadPermission);

        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll(".crew-positions-row")));
        Assert.Empty(page.FindAll(".crew-positions-add-button"));
        Assert.Empty(page.FindAll(".crew-positions-edit-button"));
    }

    [Fact]
    public void The_add_button_opens_an_empty_dialog_with_one_requirement_row()
    {
        var page = RenderPage(
            CurrentUserAuthorizationState.CrewReadPermission, CurrentUserAuthorizationState.CrewManagePermission);
        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll(".crew-positions-add-button")));

        page.Find(".crew-positions-add-button").Click();

        Assert.Equal("Add policy", page.Find(".crew-positions-modal-heading h2").TextContent);
        Assert.Single(page.FindAll(".crew-manning-line-row"));
    }

    [Fact]
    public void Creating_a_policy_closes_the_dialog_and_refreshes_the_list()
    {
        var callCount = 0;
        respondToList = _ =>
        {
            callCount++;
            return callCount == 1 ? Json(HttpStatusCode.OK, Page()) : Json(HttpStatusCode.OK, Page(Row()));
        };

        var page = RenderPage(
            CurrentUserAuthorizationState.CrewReadPermission, CurrentUserAuthorizationState.CrewManagePermission);
        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll(".crew-positions-add-button")));
        page.Find(".crew-positions-add-button").Click();

        page.Find("#policy-rank").Change(PositionId.ToString());
        page.Find(".crew-manning-line-row input[type='text']").Change("STCW-II/1");
        page.Find(".crew-positions-form-grid").Submit();

        page.WaitForAssertion(() =>
        {
            Assert.Empty(page.FindAll(".crew-positions-modal"));
            Assert.Contains("Bosun", page.Markup, StringComparison.Ordinal);
        });
        Assert.True(callCount >= 2);
    }

    [Fact]
    public void A_duplicate_policy_shows_a_conflict_banner()
    {
        respondToCreate = _ => Json(
            HttpStatusCode.Conflict,
            """{"title":"A qualification policy already exists for this rank.","status":409,"detail":"A qualification policy already exists for crew position."}""");

        var page = RenderPage(
            CurrentUserAuthorizationState.CrewReadPermission, CurrentUserAuthorizationState.CrewManagePermission);
        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll(".crew-positions-add-button")));
        page.Find(".crew-positions-add-button").Click();
        page.Find("#policy-rank").Change(PositionId.ToString());
        page.Find(".crew-manning-line-row input[type='text']").Change("STCW-II/1");
        page.Find(".crew-positions-form-grid").Submit();

        page.WaitForAssertion(() => Assert.Contains(
            "already exists", page.Find(".crew-positions-form-conflict").TextContent, StringComparison.Ordinal));
    }

    [Fact]
    public void A_duplicate_type_validation_failure_lists_field_errors_and_keeps_the_dialog_open()
    {
        respondToCreate = _ => Json(
            HttpStatusCode.BadRequest,
            """{"title":"One or more validation errors occurred.","status":400,"errors":{"requirements":["Must not contain duplicate required types."]}}""");

        var page = RenderPage(
            CurrentUserAuthorizationState.CrewReadPermission, CurrentUserAuthorizationState.CrewManagePermission);
        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll(".crew-positions-add-button")));
        page.Find(".crew-positions-add-button").Click();
        page.Find("#policy-rank").Change(PositionId.ToString());
        page.Find(".crew-manning-line-row input[type='text']").Change("STCW-II/1");
        page.Find(".crew-positions-form-grid").Submit();

        page.WaitForAssertion(() => Assert.Contains(
            "duplicate required types",
            page.Find(".crew-positions-form-field-errors").TextContent,
            StringComparison.Ordinal));
        Assert.NotEmpty(page.FindAll(".crew-positions-modal"));
    }

    [Fact]
    public void The_edit_button_loads_and_prefills_existing_requirements()
    {
        respondToList = _ => Json(HttpStatusCode.OK, Page(Row()));

        var page = RenderPage(
            CurrentUserAuthorizationState.CrewReadPermission, CurrentUserAuthorizationState.CrewManagePermission);
        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll(".crew-positions-edit-button")));

        page.Find(".crew-positions-edit-button").Click();

        page.WaitForAssertion(() =>
        {
            Assert.Equal("Edit policy", page.Find(".crew-positions-modal-heading h2").TextContent);
            Assert.Equal("STCW-II/1", page.Find(".crew-manning-line-row input[type='text']").GetAttribute("value"));
        });
    }

    [Fact]
    public void An_api_failure_shows_the_error_state_and_retry_reloads()
    {
        respondToList = _ => Json(HttpStatusCode.InternalServerError, """{"status":500}""");

        var page = RenderPage();

        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll(".api-error-state")));

        respondToList = _ => Json(HttpStatusCode.OK, Page(Row()));
        page.Find(".api-error-state button").Click();

        page.WaitForAssertion(() => Assert.Contains("Bosun", page.Markup, StringComparison.Ordinal));
    }

    private IRenderedComponent<CrewQualificationPolicies> RenderPage(params string[] permissions)
    {
        var httpClient = new HttpClient(new StubHttpMessageHandler(request =>
        {
            requests.Add((request.Method, request.RequestUri!));

            var path = request.RequestUri!.AbsolutePath;

            if (path == "/api/crew/positions")
            {
                return respondToPositions(request);
            }

            if (path == "/api/crew/qualification-policies")
            {
                return request.Method == HttpMethod.Post ? respondToCreate(request) : respondToList(request);
            }

            if (path.StartsWith("/api/crew/qualification-policies/", StringComparison.Ordinal))
            {
                return request.Method == HttpMethod.Put ? respondToUpdate(request) : respondToDetail(request);
            }

            throw new InvalidOperationException($"Unexpected request: {request.Method} {path}");
        }))
        {
            BaseAddress = new Uri("https://api.harborops.test/"),
        };

        Services.AddSingleton(new CrewApiClient(httpClient));
        Services.AddSingleton<IHttpContextAccessor>(new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    (permissions.Length == 0
                        ? [CurrentUserAuthorizationState.CrewReadPermission, CurrentUserAuthorizationState.CrewManagePermission]
                        : permissions)
                        .Select(permission => new Claim(CurrentUserAuthorizationState.PermissionClaimType, permission))
                        .ToArray(),
                    "TestAuthentication")),
            },
        });
        Services.AddScoped<CurrentUserAuthorizationState>();

        var page = Render<CrewQualificationPolicies>();
        page.WaitForAssertion(() => Assert.NotEmpty(requests));

        return page;
    }

    private static string Page(params string[] rows) => $$"""
        {"items":[{{string.Join(',', rows)}}],"pageNumber":1,"pageSize":25,"totalCount":{{rows.Length}}}
        """;

    private static string Row(bool enabled = true, int requiredTypeCount = 1) => $$"""
        {"id":"{{PolicyId}}","crewPositionId":"{{PositionId}}","rankName":"Bosun",
        "enabled":{{(enabled ? "true" : "false")}},"requiredTypeCount":{{requiredTypeCount}},
        "concurrencyToken":"70000000-0000-0000-0000-000000000001"}
        """;

    private static string Detail() => $$"""
        {"id":"{{PolicyId}}","crewPositionId":"{{PositionId}}","rankName":"Bosun","enabled":true,
        "concurrencyToken":"70000000-0000-0000-0000-000000000001","requiredTypes":["STCW-II/1"],"history":[]}
        """;

    private static Task<HttpResponseMessage> Json(HttpStatusCode statusCode, string json) =>
        Task.FromResult(new HttpResponseMessage(statusCode) { Content = new StringContent(json, Encoding.UTF8, "application/json") });

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> responseFactory)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => responseFactory(request);
    }
}
