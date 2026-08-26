using System.Net;
using System.Security.Claims;
using System.Text;
using Bunit;
using HarborOps.Web.Api;
using HarborOps.Web.Components.Pages.Crew;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace HarborOps.Web.Tests.Components;

public sealed class CrewMedicalExaminationsPageTests : BunitContext
{
    private const string CrewMemberId = "10000000-0000-0000-0000-000000000001";

    private readonly List<Uri> requestedUris = [];

    private Func<HttpRequestMessage, Task<HttpResponseMessage>> respondToExaminations =
        _ => Json(HttpStatusCode.OK, Page());

    [Fact]
    public void Initial_state_loads_examinations_without_filters()
    {
        var page = RenderMedicalPage();

        page.WaitForAssertion(() =>
        {
            var query = requestedUris.First(IsExaminationsRequest).Query;
            Assert.DoesNotContain("search=", query, StringComparison.Ordinal);
            Assert.DoesNotContain("fitnessResult=", query, StringComparison.Ordinal);
            Assert.DoesNotContain("expired=", query, StringComparison.Ordinal);
            Assert.DoesNotContain("expiringWithinDays=", query, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Rows_are_shown_with_crew_member_and_employment_status()
    {
        respondToExaminations = _ => Json(HttpStatusCode.OK, Page(Row()));

        var page = RenderMedicalPage();

        page.WaitForAssertion(() =>
        {
            Assert.Contains("Ivan Petrović", page.Markup, StringComparison.Ordinal);
            Assert.Contains("Active", page.Markup, StringComparison.Ordinal);
            Assert.Contains("Periodic", page.Markup, StringComparison.Ordinal);
        });
    }

    [Theory]
    [InlineData(true, false, "Expired", "crew-medical-badge-expired")]
    [InlineData(false, true, "Expiring soon", "crew-medical-badge-warning")]
    [InlineData(false, false, "Valid", "crew-medical-badge-valid")]
    public void The_status_badge_reflects_expiry_state(bool isExpired, bool isExpiringSoon, string label, string cssClass)
    {
        respondToExaminations = _ => Json(HttpStatusCode.OK, Page(Row(isExpired: isExpired, isExpiringSoon: isExpiringSoon)));

        var page = RenderMedicalPage();

        page.WaitForAssertion(() =>
        {
            var badges = page.FindAll(".crew-medical-table-scroll .crew-medical-badge");
            var statusBadge = badges[1];
            Assert.Equal(label, statusBadge.TextContent.Trim());
            Assert.Contains(cssClass, statusBadge.ClassList);
        });
    }

    [Fact]
    public void The_mobile_card_is_collapsed_until_its_heading_is_clicked()
    {
        respondToExaminations = _ => Json(HttpStatusCode.OK, Page(Row()));

        var page = RenderMedicalPage();
        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll(".crew-medical-card-heading")));

        Assert.Empty(page.FindAll(".crew-medical-card-details"));

        page.Find(".crew-medical-card-heading").Click();

        Assert.NotEmpty(page.FindAll(".crew-medical-card-details"));
        Assert.Equal("true", page.Find(".crew-medical-card-heading").GetAttribute("aria-expanded"));

        page.Find(".crew-medical-card-heading").Click();

        Assert.Empty(page.FindAll(".crew-medical-card-details"));
    }

    [Fact]
    public void Empty_data_shows_the_empty_state_instead_of_a_table()
    {
        var page = RenderMedicalPage();

        page.WaitForAssertion(() =>
        {
            Assert.Empty(page.FindAll("table.crew-medical-table"));
            Assert.Contains("No medical examinations found", page.Markup, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void An_api_failure_shows_the_error_state_with_retry()
    {
        respondToExaminations = _ => Json(HttpStatusCode.InternalServerError, """{"status":500}""");

        var page = RenderMedicalPage();

        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll(".api-error-state, [class*='error']")));
    }

    [Fact]
    public void Changing_filters_automatically_sends_them_and_resets_to_page_one()
    {
        var page = RenderMedicalPage();
        page.WaitForAssertion(() => Assert.Contains(requestedUris, IsExaminationsRequest));

        page.Find("#crew-medical-search").Change("Ana");
        page.Find("#crew-medical-fitness").Change("Fit");
        page.Find("#crew-medical-expired").Change("true");
        page.Find("#crew-medical-expiring-within").Change("15");

        page.WaitForAssertion(() =>
        {
            var lastQuery = requestedUris.Last(IsExaminationsRequest).Query;

            Assert.Contains("search=Ana", lastQuery, StringComparison.Ordinal);
            Assert.Contains("fitnessResult=Fit", lastQuery, StringComparison.Ordinal);
            Assert.Contains("expired=true", lastQuery, StringComparison.Ordinal);
            Assert.Contains("expiringWithinDays=15", lastQuery, StringComparison.Ordinal);
            Assert.Contains("pageNumber=1", lastQuery, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void The_reset_button_clears_all_filters_and_reloads()
    {
        var page = RenderMedicalPage();
        page.WaitForAssertion(() => Assert.Contains(requestedUris, IsExaminationsRequest));

        page.Find("#crew-medical-search").Change("Ana");
        page.Find(".crew-reset-button").Click();

        page.WaitForAssertion(() =>
        {
            var lastQuery = requestedUris.Last(IsExaminationsRequest).Query;
            Assert.DoesNotContain("search=", lastQuery, StringComparison.Ordinal);
        });
        Assert.Null(page.Find("#crew-medical-search").GetAttribute("value"));
    }

    [Fact]
    public void Pagination_requests_the_next_page()
    {
        respondToExaminations = _ => Json(HttpStatusCode.OK, Page([Row()], pageNumber: 1, pageSize: 25, totalCount: 30));

        var page = RenderMedicalPage();
        page.WaitForAssertion(() => Assert.Contains("Page 1 of 2", page.Markup, StringComparison.Ordinal));

        respondToExaminations = _ => Json(HttpStatusCode.OK, Page([Row()], pageNumber: 2, pageSize: 25, totalCount: 30));
        page.FindAll(".crew-pagination button")[1].Click();

        page.WaitForAssertion(() =>
        {
            var lastQuery = requestedUris.Last(IsExaminationsRequest).Query;
            Assert.Contains("pageNumber=2", lastQuery, StringComparison.Ordinal);
        });
    }

    private static bool IsExaminationsRequest(Uri uri) => uri.AbsolutePath.EndsWith("/medical-examinations", StringComparison.Ordinal);

    private IRenderedComponent<CrewMedicalExaminations> RenderMedicalPage(params string[] permissions)
    {
        var crewHttpClient = new HttpClient(new StubHttpMessageHandler(request =>
        {
            requestedUris.Add(request.RequestUri!);

            return respondToExaminations(request);
        }))
        {
            BaseAddress = new Uri("https://api.harborops.test/"),
        };

        Services.AddSingleton(new CrewApiClient(crewHttpClient));
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

        var page = Render<CrewMedicalExaminations>();
        page.WaitForAssertion(() => Assert.NotEmpty(requestedUris));

        return page;
    }

    private static string Page(params string[] rows) => Page(rows, 1, 25, rows.Length);

    private static string Page(string[] rows, int pageNumber, int pageSize, int totalCount) => $$"""
        {"items":[{{string.Join(',', rows)}}],"pageNumber":{{pageNumber}},"pageSize":{{pageSize}},"totalCount":{{totalCount}}}
        """;

    private static string Row(
        bool isExpired = false,
        bool isExpiringSoon = false) => $$"""
        {"medicalExaminationId":"90000000-0000-0000-0000-000000000001","crewMemberId":"{{CrewMemberId}}",
        "crewMemberName":"Ivan Petrović","employmentStatus":"Active","examType":"Periodic","clinic":"Clinic A",
        "examDate":"2026-01-01","expiryDate":"2027-01-01","fitnessResult":"Fit",
        "isExpired":{{(isExpired ? "true" : "false")}},"isExpiringSoon":{{(isExpiringSoon ? "true" : "false")}}}
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
