using System.Net;
using System.Security.Claims;
using System.Text;
using Bunit;
using HarborOps.Web.Api;
using HarborOps.Web.Components.Pages.Crew;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace HarborOps.Web.Tests.Components;

public sealed class CrewPositionsPageTests : BunitContext
{
    private static readonly Guid PositionId = Guid.Parse("50000000-0000-0000-0000-000000000001");

    private readonly List<Uri> requestedUris = [];

    private Func<HttpRequestMessage, Task<HttpResponseMessage>> respondToSearch =
        _ => Json(HttpStatusCode.OK, Page());

    private Func<HttpRequestMessage, Task<HttpResponseMessage>> respondToRegister =
        _ => Json(HttpStatusCode.Created, $$"""{"id":"{{PositionId}}"}""");

    private Func<HttpRequestMessage, Task<HttpResponseMessage>> respondToUpdate =
        _ => Json(HttpStatusCode.OK, $$"""{"id":"{{PositionId}}"}""");

    [Fact]
    public void Rows_are_rendered_with_rank_department_seniority_and_usage_counts()
    {
        respondToSearch = _ => Json(HttpStatusCode.OK, Page(Row()));

        var page = RenderPositionsPage();

        page.WaitForAssertion(() =>
        {
            var row = page.Find(".crew-positions-row");
            Assert.Contains("Bosun", row.TextContent, StringComparison.Ordinal);
            Assert.Contains("Deck", row.TextContent, StringComparison.Ordinal);
            Assert.Contains("Senior Rating", row.TextContent, StringComparison.Ordinal);
            Assert.Contains("2", row.TextContent, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Empty_data_shows_the_empty_state()
    {
        var page = RenderPositionsPage();

        page.WaitForAssertion(() =>
        {
            Assert.Empty(page.FindAll(".crew-positions-row"));
            Assert.Contains("No positions found", page.Markup, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Searching_and_filtering_by_department_sends_both_to_the_api()
    {
        var page = RenderPositionsPage();
        page.WaitForAssertion(() => Assert.NotEmpty(requestedUris));

        page.Find("#crew-positions-search").Input("Bos");
        page.Find("#crew-positions-department").Change("Engine");

        page.WaitForAssertion(() =>
        {
            var lastQuery = requestedUris[^1].Query;
            Assert.Contains("search=Bos", lastQuery, StringComparison.Ordinal);
            Assert.Contains("department=Engine", lastQuery, StringComparison.Ordinal);
            Assert.Contains("pageNumber=1", lastQuery, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void The_reset_button_clears_filters_and_reloads()
    {
        var page = RenderPositionsPage();
        page.WaitForAssertion(() => Assert.NotEmpty(requestedUris));

        page.Find("#crew-positions-search").Input("Bos");
        page.Find(".crew-reset-button").Click();

        page.WaitForAssertion(() =>
        {
            var lastQuery = requestedUris[^1].Query;
            Assert.Contains("search=&", lastQuery, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Pagination_requests_the_next_page()
    {
        respondToSearch = _ => Json(HttpStatusCode.OK, Page([Row()], pageNumber: 1, pageSize: 25, totalCount: 30));

        var page = RenderPositionsPage();
        page.WaitForAssertion(() => Assert.Contains("Page 1 of 2", page.Markup, StringComparison.Ordinal));

        respondToSearch = _ => Json(HttpStatusCode.OK, Page([Row()], pageNumber: 2, pageSize: 25, totalCount: 30));
        page.FindAll(".crew-pagination button")[1].Click();

        page.WaitForAssertion(() =>
        {
            var lastQuery = requestedUris[^1].Query;
            Assert.Contains("pageNumber=2", lastQuery, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void An_api_failure_shows_the_error_state_and_retry_reloads()
    {
        respondToSearch = _ => Json(HttpStatusCode.InternalServerError, """{"status":500}""");

        var page = RenderPositionsPage();

        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll(".api-error-state")));

        respondToSearch = _ => Json(HttpStatusCode.OK, Page(Row()));
        page.Find(".api-error-state button").Click();

        page.WaitForAssertion(() => Assert.Contains("Bosun", page.Markup, StringComparison.Ordinal));
    }

    [Fact]
    public void Without_the_manage_permission_add_and_edit_controls_are_not_offered()
    {
        respondToSearch = _ => Json(HttpStatusCode.OK, Page(Row()));

        var page = RenderPositionsPage(CurrentUserAuthorizationState.CrewReadPermission);

        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll(".crew-positions-row")));
        Assert.Empty(page.FindAll(".crew-positions-add-button"));
        Assert.Empty(page.FindAll(".crew-positions-edit-button"));
    }

    [Fact]
    public void With_the_manage_permission_the_add_button_opens_an_empty_dialog()
    {
        var page = RenderPositionsPage(
            CurrentUserAuthorizationState.CrewReadPermission,
            CurrentUserAuthorizationState.CrewManagePermission);
        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll(".crew-positions-add-button")));

        page.Find(".crew-positions-add-button").Click();

        Assert.Equal("Add position", page.Find(".crew-positions-modal-heading h2").TextContent);
        Assert.True(string.IsNullOrEmpty(page.Find("#position-rank-name").GetAttribute("value")));
    }

    [Fact]
    public void The_edit_button_opens_a_dialog_prefilled_with_the_row_values()
    {
        respondToSearch = _ => Json(HttpStatusCode.OK, Page(Row()));

        var page = RenderPositionsPage(
            CurrentUserAuthorizationState.CrewReadPermission,
            CurrentUserAuthorizationState.CrewManagePermission);
        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll(".crew-positions-edit-button")));

        page.Find(".crew-positions-edit-button").Click();

        Assert.Equal("Edit position", page.Find(".crew-positions-modal-heading h2").TextContent);
        Assert.Equal("Bosun", page.Find("#position-rank-name").GetAttribute("value"));
        Assert.Equal("Deck", page.Find("#position-department").GetAttribute("value"));
        Assert.Equal("SeniorRating", page.Find("#position-seniority-level").GetAttribute("value"));
    }

    [Fact]
    public void The_close_button_dismisses_the_dialog()
    {
        var page = RenderPositionsPage(
            CurrentUserAuthorizationState.CrewReadPermission,
            CurrentUserAuthorizationState.CrewManagePermission);
        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll(".crew-positions-add-button")));
        page.Find(".crew-positions-add-button").Click();

        page.Find(".crew-positions-modal-close").Click();

        Assert.Empty(page.FindAll(".crew-positions-modal"));
    }

    [Fact]
    public void Creating_a_position_closes_the_dialog_and_refreshes_the_list()
    {
        var callCount = 0;
        respondToSearch = _ =>
        {
            callCount++;
            return callCount == 1 ? Json(HttpStatusCode.OK, Page()) : Json(HttpStatusCode.OK, Page(Row()));
        };

        var page = RenderPositionsPage(
            CurrentUserAuthorizationState.CrewReadPermission,
            CurrentUserAuthorizationState.CrewManagePermission);
        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll(".crew-positions-add-button")));
        page.Find(".crew-positions-add-button").Click();

        page.Find("#position-rank-name").Change("Bosun");
        page.Find("#position-department").Change("Deck");
        page.Find("#position-seniority-level").Change("SeniorRating");
        page.Find(".crew-positions-form-grid").Submit();

        page.WaitForAssertion(() =>
        {
            Assert.Empty(page.FindAll(".crew-positions-modal"));
            Assert.Contains("Bosun", page.Markup, StringComparison.Ordinal);
        });
        Assert.True(callCount >= 2);
    }

    [Fact]
    public void A_validation_failure_lists_field_errors_and_keeps_the_dialog_open()
    {
        respondToRegister = _ => Json(
            HttpStatusCode.BadRequest,
            """{"title":"One or more validation errors occurred.","status":400,"errors":{"department":["Department is not supported."]}}""");

        var page = RenderPositionsPage(
            CurrentUserAuthorizationState.CrewReadPermission,
            CurrentUserAuthorizationState.CrewManagePermission);
        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll(".crew-positions-add-button")));
        page.Find(".crew-positions-add-button").Click();
        page.Find("#position-rank-name").Change("Bosun");
        page.Find(".crew-positions-form-grid").Submit();

        page.WaitForAssertion(() => Assert.Contains(
            "Department is not supported.",
            page.Find(".crew-positions-form-field-errors").TextContent,
            StringComparison.Ordinal));
        Assert.NotEmpty(page.FindAll(".crew-positions-modal"));
    }

    [Fact]
    public void A_duplicate_position_shows_a_conflict_banner()
    {
        respondToRegister = _ => Json(
            HttpStatusCode.Conflict,
            """{"title":"A position with this rank and department already exists.","status":409,"detail":"A position with rank 'Bosun' in department 'Deck' already exists."}""");

        var page = RenderPositionsPage(
            CurrentUserAuthorizationState.CrewReadPermission,
            CurrentUserAuthorizationState.CrewManagePermission);
        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll(".crew-positions-add-button")));
        page.Find(".crew-positions-add-button").Click();
        page.Find("#position-rank-name").Change("Bosun");
        page.Find(".crew-positions-form-grid").Submit();

        page.WaitForAssertion(() => Assert.Contains(
            "already exists",
            page.Find(".crew-positions-form-conflict").TextContent,
            StringComparison.Ordinal));
    }

    [Fact]
    public void Updating_a_position_closes_the_dialog_and_refreshes_the_list()
    {
        var callCount = 0;
        respondToSearch = _ =>
        {
            callCount++;
            return callCount == 1
                ? Json(HttpStatusCode.OK, Page(Row()))
                : Json(HttpStatusCode.OK, Page(Row(rankName: "Chief Bosun")));
        };

        var page = RenderPositionsPage(
            CurrentUserAuthorizationState.CrewReadPermission,
            CurrentUserAuthorizationState.CrewManagePermission);
        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll(".crew-positions-edit-button")));
        page.Find(".crew-positions-edit-button").Click();

        page.Find("#position-rank-name").Change("Chief Bosun");
        page.Find(".crew-positions-form-grid").Submit();

        page.WaitForAssertion(() =>
        {
            Assert.Empty(page.FindAll(".crew-positions-modal"));
            Assert.Contains("Chief Bosun", page.Markup, StringComparison.Ordinal);
        });
        Assert.True(callCount >= 2);
    }

    private IRenderedComponent<CrewPositions> RenderPositionsPage(params string[] permissions)
    {
        var httpClient = new HttpClient(new StubHttpMessageHandler(request =>
        {
            requestedUris.Add(request.RequestUri!);

            if (request.Method == HttpMethod.Post)
            {
                return respondToRegister(request);
            }

            if (request.Method == HttpMethod.Put)
            {
                return respondToUpdate(request);
            }

            return respondToSearch(request);
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

        var page = Render<CrewPositions>();
        page.WaitForAssertion(() => Assert.NotEmpty(requestedUris));

        return page;
    }

    private static string Page(params string[] rows) => Page(rows, 1, 25, rows.Length);

    private static string Page(string[] rows, int pageNumber, int pageSize, int totalCount) => $$"""
        {"items":[{{string.Join(',', rows)}}],"pageNumber":{{pageNumber}},"pageSize":{{pageSize}},"totalCount":{{totalCount}}}
        """;

    private static string Row(
        string rankName = "Bosun",
        string department = "Deck",
        string seniorityLevel = "SeniorRating",
        int crewMemberUsageCount = 2,
        int assignmentUsageCount = 1) => $$"""
        {"crewPositionId":"{{PositionId}}","rankName":"{{rankName}}","department":"{{department}}",
        "seniorityLevel":"{{seniorityLevel}}","crewMemberUsageCount":{{crewMemberUsageCount}},
        "assignmentUsageCount":{{assignmentUsageCount}}}
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
