using System.Net;
using System.Text;
using Bunit;
using HarborOps.Web.Api;
using HarborOps.Web.Components.Pages.Crew;
using Microsoft.Extensions.DependencyInjection;

namespace HarborOps.Web.Tests.Components;

public sealed class CrewPageTests : BunitContext
{
    private readonly List<Uri> requestedUris = [];

    private Func<HttpRequestMessage, Task<HttpResponseMessage>> respond =
        _ => Json(HttpStatusCode.OK, Page(Row()));

    [Fact]
    public void Crew_members_render_with_their_identity_and_rank()
    {
        respond = _ => Json(HttpStatusCode.OK, Page(Row(
            name: "Marko Petrovic",
            imoSeafarerId: "IMO00001",
            baseRankName: "Chief Engineer")));

        var page = RenderCrewPage();

        var row = Assert.Single(page.FindAll(".crew-member-row"));

        Assert.Contains("Marko Petrovic", row.TextContent, StringComparison.Ordinal);
        Assert.Contains("IMO IMO00001", row.TextContent, StringComparison.Ordinal);
        Assert.Contains("Chief Engineer", row.TextContent, StringComparison.Ordinal);
        Assert.Contains("Montenegro", row.TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void A_roster_row_links_to_the_crew_member_profile()
    {
        var page = RenderCrewPage();

        page.WaitForAssertion(() => Assert.Equal(
            "/crew/80000000-0000-0000-0000-000000000001",
            page.Find(".crew-member-row").GetAttribute("href")));
    }

    [Fact]
    public void An_expired_passport_is_marked_with_wording_not_colour_alone()
    {
        respond = _ => Json(HttpStatusCode.OK, Page(Row(passportExpiry: "2020-01-01")));

        var page = RenderCrewPage();

        var passport = page.Find(".crew-passport");

        Assert.Contains("passport-expired", passport.ClassName, StringComparison.Ordinal);
        Assert.Contains("Expired", passport.TextContent, StringComparison.Ordinal);
        Assert.NotNull(passport.QuerySelector(".bi-exclamation-triangle"));
    }

    [Fact]
    public void A_valid_passport_carries_no_expiry_warning()
    {
        respond = _ => Json(HttpStatusCode.OK, Page(Row(passportExpiry: "2030-01-01")));

        var page = RenderCrewPage();

        var passport = page.Find(".crew-passport");

        Assert.DoesNotContain("passport-expired", passport.ClassName, StringComparison.Ordinal);
        Assert.DoesNotContain("Expired", passport.TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void The_employment_badge_carries_the_status_wording_and_its_modifier()
    {
        respond = _ => Json(HttpStatusCode.OK, Page(Row(employmentStatus: "Inactive")));

        var page = RenderCrewPage();

        var badge = page.Find(".employment-status");

        Assert.Equal("Inactive", badge.TextContent.Trim());
        Assert.Contains("employment-inactive", badge.ClassName, StringComparison.Ordinal);
    }

    [Fact]
    public void Summary_cards_count_expired_and_soon_expiring_passports()
    {
        respond = _ => Json(HttpStatusCode.OK, Page(
            Row(name: "Ana", imoSeafarerId: "IMO00001", passportExpiry: "2020-01-01"),
            Row(name: "Boris", imoSeafarerId: "IMO00002", passportExpiry: "2030-01-01"),
            Row(name: "Cvijeta", imoSeafarerId: "IMO00003", passportExpiry: "2030-01-01", employmentStatus: "Inactive")));

        var page = RenderCrewPage();

        var values = page.FindAll(".crew-summary-card strong");

        Assert.Equal("3", values[0].TextContent.Trim());
        Assert.Equal("2", values[1].TextContent.Trim());
        Assert.Equal("1", values[2].TextContent.Trim());
    }

    [Fact]
    public void The_rank_distribution_groups_the_loaded_page()
    {
        respond = _ => Json(HttpStatusCode.OK, Page(
            Row(name: "Ana", imoSeafarerId: "IMO00001", baseRankName: "Chief Engineer"),
            Row(name: "Boris", imoSeafarerId: "IMO00002", baseRankName: "Chief Engineer"),
            Row(name: "Cvijeta", imoSeafarerId: "IMO00003", baseRankName: "Able Seafarer")));

        var page = RenderCrewPage();

        var rows = page.FindAll(".rank-distribution-row");

        Assert.Equal(2, rows.Count);
        Assert.Contains("Chief Engineer", rows[0].TextContent, StringComparison.Ordinal);
        Assert.Contains("2", rows[0].TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void Passport_attention_lists_the_members_that_need_action()
    {
        respond = _ => Json(HttpStatusCode.OK, Page(
            Row(name: "Ana", imoSeafarerId: "IMO00001", passportExpiry: "2020-01-01"),
            Row(name: "Boris", imoSeafarerId: "IMO00002", passportExpiry: "2035-01-01")));

        var page = RenderCrewPage();

        var attention = page.Find(".passport-attention-list");

        Assert.Contains("Ana", attention.TextContent, StringComparison.Ordinal);
        Assert.DoesNotContain("Boris", attention.TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void An_empty_result_offers_the_empty_state_instead_of_a_roster()
    {
        respond = _ => Json(HttpStatusCode.OK, Page());

        var page = RenderCrewPage();

        Assert.Empty(page.FindAll(".crew-member-row"));
        Assert.Contains("No crew members found", page.Find(".empty-state").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void An_api_failure_reports_the_problem_from_the_response()
    {
        respond = _ => Json(
            HttpStatusCode.InternalServerError,
            """{"title":"Unable to load crew roster","detail":"The register is offline.","status":500}""");

        var page = RenderCrewPage();

        var error = page.Find(".api-error-state");

        Assert.Contains("Unable to load crew roster", error.TextContent, StringComparison.Ordinal);
        Assert.Contains("The register is offline.", error.TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void A_forbidden_response_explains_the_missing_permission()
    {
        respond = _ => Json(HttpStatusCode.Forbidden, "{}");

        var page = RenderCrewPage();

        Assert.Contains(
            "Your account does not have permission to view crew members.",
            page.Find(".api-error-state").TextContent,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Retrying_after_a_failure_loads_the_roster_again()
    {
        respond = _ => Json(HttpStatusCode.InternalServerError, "{}");

        var page = RenderCrewPage();
        var requestsBeforeRetry = requestedUris.Count;

        respond = _ => Json(HttpStatusCode.OK, Page(Row(name: "Marko Petrovic")));

        page.Find(".api-error-state button").Click();

        page.WaitForAssertion(() =>
        {
            Assert.True(requestedUris.Count > requestsBeforeRetry);
            Assert.Contains("Marko Petrovic", page.Markup, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Searching_sends_the_term_to_the_api()
    {
        var page = RenderCrewPage();

        page.Find(".crew-input-shell input").Input("Petrovic");

        page.WaitForAssertion(() => Assert.Contains(
            requestedUris,
            uri => uri.Query.Contains("search=Petrovic", StringComparison.Ordinal)));
    }

    [Fact]
    public void Filtering_by_employment_status_sends_the_status_to_the_api()
    {
        var page = RenderCrewPage();

        page.Find(".crew-filters select").Change("Inactive");

        page.WaitForAssertion(() => Assert.Contains(
            requestedUris,
            uri => uri.Query.Contains("employmentStatus=Inactive", StringComparison.Ordinal)));
    }

    [Fact]
    public void Changing_a_filter_returns_to_the_first_page()
    {
        respond = request => Json(HttpStatusCode.OK, Page(
            request.RequestUri!.Query.Contains("pageNumber=2", StringComparison.Ordinal) ? 2 : 1,
            60,
            Row()));

        var page = RenderCrewPage();

        page.FindAll(".crew-pagination button")[1].Click();
        page.WaitForAssertion(() => Assert.Contains(
            requestedUris,
            uri => uri.Query.Contains("pageNumber=2", StringComparison.Ordinal)));

        page.Find(".crew-input-shell input").Input("Petrovic");

        page.WaitForAssertion(() =>
        {
            var lastQuery = requestedUris[^1].Query;

            Assert.Contains("search=Petrovic", lastQuery, StringComparison.Ordinal);
            Assert.Contains("pageNumber=1", lastQuery, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Resetting_clears_the_search_and_the_employment_status()
    {
        var page = RenderCrewPage();

        page.Find(".crew-input-shell input").Input("Petrovic");
        page.Find(".crew-filters select").Change("Inactive");
        page.WaitForAssertion(() => Assert.Contains(
            requestedUris,
            uri => uri.Query.Contains("employmentStatus=Inactive", StringComparison.Ordinal)));

        page.Find(".crew-reset-button").Click();

        page.WaitForAssertion(() =>
        {
            var lastQuery = requestedUris[^1].Query;

            Assert.Contains("search=&", lastQuery, StringComparison.Ordinal);
            Assert.Contains("employmentStatus=&", lastQuery, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Paging_forward_requests_the_next_page()
    {
        respond = request => Json(HttpStatusCode.OK, Page(
            request.RequestUri!.Query.Contains("pageNumber=2", StringComparison.Ordinal) ? 2 : 1,
            60,
            Row()));

        var page = RenderCrewPage();

        page.FindAll(".crew-pagination button")[1].Click();

        page.WaitForAssertion(() =>
        {
            Assert.Contains(
                requestedUris,
                uri => uri.Query.Contains("pageNumber=2", StringComparison.Ordinal));
            Assert.Contains("Page 2 of 3", page.Find(".crew-page-indicator").TextContent, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Paging_back_from_the_first_page_is_not_offered()
    {
        respond = _ => Json(HttpStatusCode.OK, Page(1, 60, Row()));

        var page = RenderCrewPage();

        Assert.True(page.FindAll(".crew-pagination button")[0].HasAttribute("disabled"));
    }

    [Fact]
    public void The_print_action_is_offered_in_the_page_header()
    {
        var page = RenderCrewPage();

        var print = page.Find(".crew-header .print-button");

        Assert.Equal("Print the crew roster", print.GetAttribute("aria-label"));
        Assert.NotNull(print.QuerySelector(".bi-printer"));
    }

    [Fact]
    public void The_print_action_is_itself_left_out_of_the_printed_output()
    {
        var page = RenderCrewPage();

        Assert.Contains("no-print", page.Find(".print-button").ClassName, StringComparison.Ordinal);
    }

    [Fact]
    public void The_print_action_asks_the_browser_to_print()
    {
        var invocation = JSInterop.SetupVoid("print");

        var page = RenderCrewPage();

        page.Find(".print-button").Click();

        invocation.SetVoidResult();

        Assert.Single(JSInterop.Invocations["print"]);
    }

    [Fact]
    public void The_printed_summary_repeats_the_applied_filters()
    {
        var page = RenderCrewPage();

        page.Find(".crew-input-shell input").Input("Petrovic");
        page.Find(".crew-filters select").Change("Inactive");

        page.WaitForAssertion(() =>
        {
            var summary = page.Find(".crew-print-summary").TextContent;

            Assert.Contains("Petrovic", summary, StringComparison.Ordinal);
            Assert.Contains("Inactive", summary, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void The_printed_summary_says_so_when_no_filters_are_applied()
    {
        var page = RenderCrewPage();

        var summary = page.Find(".crew-print-summary").TextContent;

        Assert.Contains("All crew members", summary, StringComparison.Ordinal);
        Assert.Contains("All statuses", summary, StringComparison.Ordinal);
    }

    [Fact]
    public void The_printed_summary_reports_the_page_and_the_row_range()
    {
        respond = _ => Json(HttpStatusCode.OK, Page(1, 60, Row()));

        var page = RenderCrewPage();

        var summary = page.Find(".crew-print-summary").TextContent;

        Assert.Contains("1 of 3", summary, StringComparison.Ordinal);
        Assert.Contains("of 60", summary, StringComparison.Ordinal);
    }

    [Fact]
    public void The_printed_summary_is_hidden_from_the_screen_layout()
    {
        var page = RenderCrewPage();

        Assert.Contains("print-only", page.Find(".crew-print-summary").ClassName, StringComparison.Ordinal);
    }

    private IRenderedComponent<Crew> RenderCrewPage()
    {
        var httpClient = new HttpClient(new StubHttpMessageHandler(request =>
        {
            requestedUris.Add(request.RequestUri!);

            return respond(request);
        }))
        {
            BaseAddress = new Uri("https://api.harborops.test/"),
        };

        Services.AddSingleton(new CrewApiClient(httpClient));

        var page = Render<Crew>();
        page.WaitForAssertion(() => Assert.NotEmpty(requestedUris));

        return page;
    }

    private static string Row(
        string name = "Marko Petrovic",
        string imoSeafarerId = "IMO00001",
        string baseRankName = "Chief Engineer",
        string employmentStatus = "Active",
        string passportExpiry = "2030-01-01")
    {
        return $$"""
            {"crewMemberId":"80000000-0000-0000-0000-000000000001","name":"{{name}}",
            "nationality":"Montenegro","imoSeafarerId":"{{imoSeafarerId}}",
            "baseRankId":"90000000-0000-0000-0000-000000000001","baseRankName":"{{baseRankName}}",
            "employmentStatus":"{{employmentStatus}}","passportExpiry":"{{passportExpiry}}",
            "hireDate":"2020-01-01"}
            """;
    }

    private static string Page(params string[] rows) => Page(1, rows.Length, rows);

    private static string Page(int pageNumber, int totalCount, params string[] rows)
    {
        return $$"""
            {"items":[{{string.Join(',', rows)}}],"pageNumber":{{pageNumber}},"pageSize":25,"totalCount":{{totalCount}}}
            """;
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
