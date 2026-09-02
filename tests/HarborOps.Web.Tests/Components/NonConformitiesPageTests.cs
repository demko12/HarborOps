using System.Net;
using System.Security.Claims;
using System.Text;
using Bunit;
using HarborOps.Web.Api;
using HarborOps.Web.Api.Compliance;
using HarborOps.Web.Api.Documents;
using HarborOps.Web.Api.Fleet;
using HarborOps.Web.Components.Pages.Compliance;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;

namespace HarborOps.Web.Tests.Components;

public sealed class NonConformitiesPageTests : BunitContext
{
    private static readonly Guid VesselOne = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid VesselTwo = Guid.Parse("10000000-0000-0000-0000-000000000002");
    private static readonly Guid DocOne = Guid.Parse("60000000-0000-0000-0000-000000000001");
    private static readonly Guid DocTwo = Guid.Parse("60000000-0000-0000-0000-000000000002");
    private static readonly Guid ActorOne = Guid.Parse("70000000-0000-0000-0000-000000000001");
    private static readonly Guid ActorTwo = Guid.Parse("70000000-0000-0000-0000-000000000002");
    private static readonly Guid CloserId = Guid.Parse("70000000-0000-0000-0000-000000000003");

    private readonly List<HttpRequestMessage> requests = [];
    private readonly List<CancellationToken> tokens = [];
    private Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond;

    public NonConformitiesPageTests()
    {
        respond = (request, _) => DefaultResponseAsync(request);
    }

    // ---- 1. Filters ----------------------------------------------------

    [Fact]
    public void Changing_severity_reloads_with_the_severity_query_parameter()
    {
        var page = RenderPage();
        ClearListRequests();

        page.Find("#nc-filter-severity").Change("Critical");

        page.WaitForAssertion(() => Assert.NotEmpty(ListRequests()));
        Assert.Equal("Critical", QueryValue(ListRequests()[^1], "severity"));
    }

    [Fact]
    public void Changing_closure_status_reloads_with_the_closure_status_query_parameter()
    {
        var page = RenderPage();
        ClearListRequests();

        page.Find("#nc-filter-status").Change("InProgress");

        page.WaitForAssertion(() => Assert.NotEmpty(ListRequests()));
        Assert.Equal("InProgress", QueryValue(ListRequests()[^1], "closureStatus"));
    }

    [Fact]
    public void Changing_overdue_reloads_with_the_overdue_query_parameter()
    {
        var page = RenderPage();
        ClearListRequests();

        page.Find("#nc-filter-overdue").Change("true");

        page.WaitForAssertion(() => Assert.NotEmpty(ListRequests()));
        Assert.Equal("true", QueryValue(ListRequests()[^1], "overdue"));
    }

    [Fact]
    public void Changing_search_reloads_with_the_search_query_parameter()
    {
        var page = RenderPage();
        ClearListRequests();

        page.Find("#nc-filter-search").Input(DocOne.ToString()[..8]);

        page.WaitForAssertion(() => Assert.NotEmpty(ListRequests()));
        Assert.Equal(DocOne.ToString()[..8], QueryValue(ListRequests()[^1], "search"));
    }

    [Fact]
    public void Changing_a_filter_does_not_reset_the_current_page()
    {
        var page = RenderPage(totalCount: 60);
        page.FindAll(".nc-pagination button")[1].Click();
        page.WaitForAssertion(() => Assert.Equal("2", QueryValue(ListRequests()[^1], "pageNumber")));

        page.Find("#nc-filter-severity").Change("Major");

        page.WaitForAssertion(() => Assert.Equal("Major", QueryValue(ListRequests()[^1], "severity")));
        Assert.Equal("2", QueryValue(ListRequests()[^1], "pageNumber"));
    }

    // ---- 2. Pagination ---------------------------------------------------

    [Fact]
    public void Previous_is_disabled_on_the_first_page()
    {
        var page = RenderPage();
        var previous = page.FindAll(".nc-pagination button")[0];
        Assert.True(previous.HasAttribute("disabled"));
    }

    [Fact]
    public void Next_is_disabled_on_the_last_page()
    {
        var page = RenderPage(totalCount: 3, pageSize: 25);
        var next = page.FindAll(".nc-pagination button")[1];
        Assert.True(next.HasAttribute("disabled"));
    }

    [Fact]
    public void Clicking_next_then_previous_loads_the_expected_pages()
    {
        var page = RenderPage(totalCount: 60);
        var buttons = page.FindAll(".nc-pagination button");
        buttons[1].Click();
        page.WaitForAssertion(() => Assert.Equal("2", QueryValue(ListRequests()[^1], "pageNumber")));

        page.FindAll(".nc-pagination button")[0].Click();
        page.WaitForAssertion(() => Assert.Equal("1", QueryValue(ListRequests()[^1], "pageNumber")));
    }

    // ---- 3. List rendering ------------------------------------------------

    [Fact]
    public void Overdue_badge_renders_only_for_overdue_rows()
    {
        var page = RenderPage();
        var rows = page.FindAll(".nc-record");

        Assert.Empty(rows[0].QuerySelectorAll(".nc-overdue-flag"));
        Assert.Single(rows[1].QuerySelectorAll(".nc-overdue-flag"));
        Assert.Empty(rows[2].QuerySelectorAll(".nc-overdue-flag"));
    }

    [Fact]
    public void Vessel_names_resolve_and_fall_back_to_unknown_when_missing_from_the_lookup()
    {
        var page = RenderPage();
        page.WaitForAssertion(() =>
        {
            var rows = page.FindAll(".nc-record");
            Assert.Contains("Aurora", rows[0].TextContent);
            Assert.Contains("Unknown vessel", rows[1].TextContent);
        });
    }

    [Fact]
    public void Corrective_action_count_and_closure_status_render_per_row()
    {
        var page = RenderPage();
        var rows = page.FindAll(".nc-record");

        Assert.Contains("2", rows[1].TextContent);
        Assert.Contains("In progress", rows[1].TextContent);
        Assert.Contains("Closed", rows[2].TextContent);
    }

    // ---- 4. Detail panel & timeline ----------------------------------------

    [Fact]
    public void Clicking_a_row_opens_the_detail_panel_with_the_finding()
    {
        var page = RenderPage();
        page.FindAll(".nc-record")[1].Click();

        page.WaitForAssertion(() => Assert.Contains("Finding #11", page.Markup));
    }

    [Fact]
    public void Evidence_documents_resolve_via_get_document_by_id_and_render_type_and_number()
    {
        var page = RenderPage();
        page.FindAll(".nc-record")[0].Click();

        page.WaitForAssertion(() => Assert.Contains("Bill of Lading — BRZ-2024-0091", page.Markup));
    }

    [Fact]
    public void Evidence_document_falls_back_to_the_raw_guid_when_the_lookup_fails()
    {
        respond = (request, token) => request.RequestUri!.AbsolutePath == $"/api/documents/{DocOne}"
            ? Task.FromResult(Json(HttpStatusCode.NotFound, "{\"title\":\"Not found\"}"))
            : DefaultResponseAsync(request);

        var page = RenderPage();
        page.FindAll(".nc-record")[0].Click();

        page.WaitForAssertion(() => Assert.Contains(DocOne.ToString(), page.Markup));
    }

    [Fact]
    public void Corrective_actions_render_newest_first_matching_the_response_order()
    {
        var page = RenderPage();
        page.FindAll(".nc-record")[1].Click();

        page.WaitForAssertion(() =>
        {
            var entries = page.FindAll(".nc-timeline-entry");
            Assert.Equal(2, entries.Count);
            Assert.Contains("Second action", entries[0].TextContent);
            Assert.Contains("First action", entries[1].TextContent);
        });
    }

    [Fact]
    public void Closure_fields_render_only_when_closed()
    {
        var page = RenderPage();

        page.FindAll(".nc-record")[1].Click();
        page.WaitForAssertion(() => Assert.Contains("Finding #11", page.Markup));
        Assert.DoesNotContain("Closure notes", page.Markup);

        page.FindAll(".nc-record")[2].Click();
        page.WaitForAssertion(() =>
        {
            Assert.Contains("Closure notes", page.Markup);
            Assert.Contains("All resolved.", page.Markup);
            Assert.Contains(CloserId.ToString(), page.Markup);
        });
    }

    // ---- 5. Permission-aware controls ---------------------------------------

    [Fact]
    public void Add_action_button_hidden_without_manage_permission()
    {
        var page = RenderPage(permissions: [CurrentUserAuthorizationState.ComplianceReadPermission]);
        page.FindAll(".nc-record")[0].Click();

        page.WaitForAssertion(() => Assert.Contains("Finding #10", page.Markup));
        Assert.DoesNotContain("Add corrective action", page.Markup);
    }

    [Fact]
    public void Add_action_button_hidden_when_closed_even_with_manage_permission()
    {
        var page = RenderPage();
        page.FindAll(".nc-record")[2].Click();

        page.WaitForAssertion(() => Assert.Contains("Finding #12", page.Markup));
        Assert.DoesNotContain("Add corrective action", page.Markup);
    }

    [Fact]
    public void Add_action_button_visible_for_open_and_in_progress_findings_with_manage_permission()
    {
        var page = RenderPage();

        page.FindAll(".nc-record")[0].Click();
        page.WaitForAssertion(() => Assert.Contains("Add corrective action", page.Markup));

        page.FindAll(".nc-record")[1].Click();
        page.WaitForAssertion(() => Assert.Contains("Add corrective action", page.Markup));
    }

    [Fact]
    public void Close_finding_button_visible_only_for_in_progress_with_manage_permission()
    {
        var page = RenderPage();

        page.FindAll(".nc-record")[0].Click();
        page.WaitForAssertion(() => Assert.Contains("Finding #10", page.Markup));
        Assert.DoesNotContain("Close finding", page.Markup);

        page.FindAll(".nc-record")[1].Click();
        page.WaitForAssertion(() => Assert.Contains("Close finding", page.Markup));

        page.FindAll(".nc-record")[2].Click();
        page.WaitForAssertion(() => Assert.Contains("Finding #12", page.Markup));
        Assert.DoesNotContain("Close finding", page.Markup);
    }

    // ---- 6. Add corrective action form --------------------------------------

    [Fact]
    public void Empty_action_text_blocks_submit_and_makes_no_api_call()
    {
        var page = RenderPage();
        OpenAddActionForm(page, 10);
        page.Find(".nc-inline-form form").Submit();

        page.WaitForAssertion(() => Assert.Contains("Action text is required.", page.Markup));
        Assert.Empty(CorrectiveActionPosts());
    }

    [Fact]
    public void Action_text_exceeding_the_max_length_blocks_submit()
    {
        var page = RenderPage();
        OpenAddActionForm(page, 10);
        page.Find("#nc-action-text").Change(new string('a', 2001));
        page.Find(".nc-inline-form form").Submit();

        page.WaitForAssertion(() => Assert.Contains("maximum length", page.Markup, StringComparison.OrdinalIgnoreCase));
        Assert.Empty(CorrectiveActionPosts());
    }

    [Fact]
    public async Task Successful_submit_posts_the_expected_body_shows_success_and_reloads()
    {
        var page = RenderPage();
        OpenAddActionForm(page, 10);
        page.Find("#nc-action-text").Change("Inspected the valve.");
        await SelectEvidenceDocumentAsync(page);
        var listRequestsBefore = ListRequests().Count;

        page.Find(".nc-inline-form form").Submit();

        page.WaitForAssertion(() => Assert.Contains("Corrective action recorded successfully", page.Markup));
        var body = Assert.Single(CorrectiveActionPosts());
        Assert.Contains("\"actionText\":\"Inspected the valve.\"", body, StringComparison.Ordinal);
        Assert.Contains($"\"evidenceDocumentId\":\"{DocOne}\"", body, StringComparison.OrdinalIgnoreCase);
        Assert.True(ListRequests().Count > listRequestsBefore);
    }

    [Fact]
    public void Validation_failure_maps_to_the_action_text_field()
    {
        respond = (request, token) => IsCorrectiveActionPost(request, out _)
            ? Task.FromResult(Json(HttpStatusCode.BadRequest, "{\"title\":\"Validation\",\"errors\":{\"actionText\":[\"Action text is required.\"]}}"))
            : DefaultResponseAsync(request);

        var page = RenderPage();
        OpenAddActionForm(page, 10);
        page.Find("#nc-action-text").Change("Something.");
        page.Find(".nc-inline-form form").Submit();

        page.WaitForAssertion(() => Assert.Contains("Action text is required.", page.Markup));
    }

    [Fact]
    public void Not_found_response_shows_the_not_found_banner()
    {
        respond = (request, token) => IsCorrectiveActionPost(request, out _)
            ? Task.FromResult(Json(HttpStatusCode.NotFound, "{\"title\":\"Non-conformity not found.\"}"))
            : DefaultResponseAsync(request);

        var page = RenderPage();
        OpenAddActionForm(page, 10);
        page.Find("#nc-action-text").Change("Something.");
        page.Find(".nc-inline-form form").Submit();

        page.WaitForAssertion(() => Assert.Contains("could not be found", page.Markup));
    }

    [Fact]
    public void Conflict_response_shows_the_conflict_banner_and_refresh_reloads()
    {
        respond = (request, token) => IsCorrectiveActionPost(request, out _)
            ? Task.FromResult(Json(HttpStatusCode.Conflict, "{\"title\":\"Conflict\"}"))
            : DefaultResponseAsync(request);

        var page = RenderPage();
        OpenAddActionForm(page, 10);
        page.Find("#nc-action-text").Change("Something.");
        page.Find(".nc-inline-form form").Submit();

        page.WaitForAssertion(() => Assert.Contains("closed by someone else", page.Markup));

        page.Find(".nc-conflict-banner button").Click();
        page.WaitForAssertion(() => Assert.DoesNotContain("nc-inline-form", page.Markup));
    }

    [Fact]
    public void Forbidden_response_shows_the_forbidden_banner_with_no_action_button()
    {
        respond = (request, token) => IsCorrectiveActionPost(request, out _)
            ? Task.FromResult(Json(HttpStatusCode.Forbidden, "{\"title\":\"Forbidden\"}"))
            : DefaultResponseAsync(request);

        var page = RenderPage();
        OpenAddActionForm(page, 10);
        page.Find("#nc-action-text").Change("Something.");
        page.Find(".nc-inline-form form").Submit();

        page.WaitForAssertion(() => Assert.Contains("You don't have permission to do this.", page.Markup));
        Assert.Empty(page.FindAll(".nc-forbidden-banner button"));
    }

    [Fact]
    public void Network_failure_preserves_values_and_retry_resubmits_the_same_request()
    {
        var fail = true;
        respond = (request, token) =>
        {
            if (IsCorrectiveActionPost(request, out _))
            {
                if (fail) return Task.FromException<HttpResponseMessage>(new HttpRequestException("boom"));
                return Task.FromResult(Json(HttpStatusCode.Created, "{\"id\":\"80000000-0000-0000-0000-000000000001\",\"closureStatus\":\"InProgress\"}"));
            }

            return DefaultResponseAsync(request);
        };

        var page = RenderPage();
        OpenAddActionForm(page, 10);
        page.Find("#nc-action-text").Change("Inspected the valve.");
        page.Find(".nc-inline-form form").Submit();

        page.WaitForAssertion(() => Assert.Contains("Unable to record the corrective action", page.Markup));

        fail = false;
        page.Find(".nc-error-banner button").Click();

        page.WaitForAssertion(() => Assert.Contains("Corrective action recorded successfully", page.Markup));
        var posts = CorrectiveActionPosts();
        Assert.Equal(2, posts.Count);
        Assert.All(posts, body => Assert.Contains("\"actionText\":\"Inspected the valve.\"", body, StringComparison.Ordinal));
    }

    // ---- 7. Close form, including confirmation ------------------------------

    [Fact]
    public void Submitting_the_close_form_shows_confirmation_without_calling_the_api()
    {
        var page = RenderPage();
        OpenCloseForm(page);
        page.Find("#nc-closure-notes").Change("All corrective actions verified.");
        page.Find(".nc-inline-form form").Submit();

        page.WaitForAssertion(() => Assert.Contains("This cannot be undone.", page.Markup));
        Assert.Empty(ClosePosts());
    }

    [Fact]
    public void Cancelling_the_confirmation_returns_to_the_editable_form_with_values_preserved()
    {
        var page = RenderPage();
        OpenCloseForm(page);
        page.Find("#nc-closure-notes").Change("All corrective actions verified.");
        page.Find(".nc-inline-form form").Submit();
        page.WaitForAssertion(() => Assert.Contains("This cannot be undone.", page.Markup));

        page.Find(".nc-confirm-panel .nc-button-secondary").Click();

        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll("#nc-closure-notes")));
        Assert.DoesNotContain("This cannot be undone.", page.Markup);
        Assert.Empty(ClosePosts());

        // Re-submit without touching the textarea again: if Cancel had cleared the
        // model, the resulting confirmation step (or its eventual POST body) would
        // no longer carry the originally entered notes.
        page.Find(".nc-inline-form form").Submit();
        page.WaitForAssertion(() => Assert.Contains("This cannot be undone.", page.Markup));
        page.Find(".nc-confirm-panel .nc-button-danger").Click();

        page.WaitForAssertion(() => Assert.Contains("Finding closed successfully", page.Markup));
        var body = Assert.Single(ClosePosts());
        Assert.Contains("\"closureNotes\":\"All corrective actions verified.\"", body, StringComparison.Ordinal);
    }

    [Fact]
    public void Confirming_calls_close_exactly_once_with_the_expected_body()
    {
        var page = RenderPage();
        OpenCloseForm(page);
        page.Find("#nc-closure-notes").Change("All corrective actions verified.");
        page.Find(".nc-inline-form form").Submit();
        page.WaitForAssertion(() => Assert.Contains("This cannot be undone.", page.Markup));

        page.Find(".nc-confirm-panel .nc-button-danger").Click();

        page.WaitForAssertion(() => Assert.Contains("Finding closed successfully", page.Markup));
        var body = Assert.Single(ClosePosts());
        Assert.Contains("\"closureNotes\":\"All corrective actions verified.\"", body, StringComparison.Ordinal);
    }

    [Fact]
    public void Close_validation_failure_maps_to_the_closure_notes_field()
    {
        respond = (request, token) => IsClosePost(request, out _)
            ? Task.FromResult(Json(HttpStatusCode.BadRequest, "{\"title\":\"Validation\",\"errors\":{\"closureNotes\":[\"Closure notes is required.\"]}}"))
            : DefaultResponseAsync(request);

        var page = RenderPage();
        OpenCloseForm(page);
        page.Find("#nc-closure-notes").Change("Notes.");
        page.Find(".nc-inline-form form").Submit();
        page.Find(".nc-confirm-panel .nc-button-danger").Click();

        page.WaitForAssertion(() => Assert.Contains("Closure notes is required.", page.Markup));
    }

    [Fact]
    public void Close_not_found_response_shows_the_not_found_banner()
    {
        respond = (request, token) => IsClosePost(request, out _)
            ? Task.FromResult(Json(HttpStatusCode.NotFound, "{\"title\":\"Non-conformity not found.\"}"))
            : DefaultResponseAsync(request);

        var page = RenderPage();
        OpenCloseForm(page);
        page.Find("#nc-closure-notes").Change("Notes.");
        page.Find(".nc-inline-form form").Submit();
        page.Find(".nc-confirm-panel .nc-button-danger").Click();

        page.WaitForAssertion(() => Assert.Contains("could not be found", page.Markup));
    }

    [Fact]
    public void Close_conflict_response_shows_the_not_closeable_message()
    {
        respond = (request, token) => IsClosePost(request, out _)
            ? Task.FromResult(Json(HttpStatusCode.Conflict, "{\"title\":\"Conflict\"}"))
            : DefaultResponseAsync(request);

        var page = RenderPage();
        OpenCloseForm(page);
        page.Find("#nc-closure-notes").Change("Notes.");
        page.Find(".nc-inline-form form").Submit();
        page.Find(".nc-confirm-panel .nc-button-danger").Click();

        page.WaitForAssertion(() => Assert.Contains("no longer in a closeable state", page.Markup));
    }

    [Fact]
    public void Close_forbidden_response_shows_the_forbidden_banner()
    {
        respond = (request, token) => IsClosePost(request, out _)
            ? Task.FromResult(Json(HttpStatusCode.Forbidden, "{\"title\":\"Forbidden\"}"))
            : DefaultResponseAsync(request);

        var page = RenderPage();
        OpenCloseForm(page);
        page.Find("#nc-closure-notes").Change("Notes.");
        page.Find(".nc-inline-form form").Submit();
        page.Find(".nc-confirm-panel .nc-button-danger").Click();

        page.WaitForAssertion(() => Assert.Contains("You don't have permission to do this.", page.Markup));
    }

    [Fact]
    public void Close_network_failure_allows_retry_without_re_entering_values()
    {
        var fail = true;
        respond = (request, token) =>
        {
            if (IsClosePost(request, out _))
            {
                if (fail) return Task.FromException<HttpResponseMessage>(new HttpRequestException("boom"));
                return Task.FromResult(Json(HttpStatusCode.OK, "{\"id\":11,\"closureStatus\":\"Closed\",\"closedAtUtc\":\"2026-08-20T09:00:00+00:00\"}"));
            }

            return DefaultResponseAsync(request);
        };

        var page = RenderPage();
        OpenCloseForm(page);
        page.Find("#nc-closure-notes").Change("Notes.");
        page.Find(".nc-inline-form form").Submit();
        page.Find(".nc-confirm-panel .nc-button-danger").Click();

        page.WaitForAssertion(() => Assert.Contains("Unable to close the finding", page.Markup));

        fail = false;
        page.Find(".nc-error-banner button").Click();

        page.WaitForAssertion(() => Assert.Contains("Finding closed successfully", page.Markup));
        Assert.Equal(2, ClosePosts().Count);
    }

    // ---- 8. Disposal / cancellation ------------------------------------------

    [Fact]
    public async Task Disposing_the_page_cancels_an_in_flight_list_load()
    {
        var release = new TaskCompletionSource<HttpResponseMessage>();
        var page = RenderPage();

        respond = (request, token) =>
        {
            if (IsListGet(request))
            {
                tokens.Add(token);
                return release.Task;
            }

            return DefaultResponseAsync(request);
        };

        page.Find("#nc-filter-severity").Change("Major");
        page.WaitForAssertion(() => Assert.NotEmpty(tokens));

        var inFlightToken = tokens[^1];
        Assert.False(inFlightToken.IsCancellationRequested);

        await DisposeComponentsAsync();

        Assert.True(inFlightToken.IsCancellationRequested);
        release.SetResult(Json(HttpStatusCode.OK, ListJson()));
    }

    [Fact]
    public async Task Disposing_the_page_cancels_an_in_flight_detail_load()
    {
        var release = new TaskCompletionSource<HttpResponseMessage>();
        var page = RenderPage();

        respond = (request, token) =>
        {
            if (request.RequestUri!.AbsolutePath.StartsWith("/api/compliance/non-conformities/10", StringComparison.Ordinal))
            {
                tokens.Add(token);
                return release.Task;
            }

            return DefaultResponseAsync(request);
        };

        page.FindAll(".nc-record")[0].Click();
        page.WaitForAssertion(() => Assert.NotEmpty(tokens));

        var inFlightToken = tokens[^1];
        Assert.False(inFlightToken.IsCancellationRequested);

        await DisposeComponentsAsync();

        Assert.True(inFlightToken.IsCancellationRequested);
        release.SetResult(Json(HttpStatusCode.OK, DetailOpenJson));
    }

    // ---- helpers -------------------------------------------------------------

    private IRenderedComponent<NonConformities> RenderPage(
        int totalCount = 3,
        int pageSize = 25,
        params string[] permissions)
    {
        requests.Clear();

        var handler = new StubHttpMessageHandler((request, token) =>
        {
            requests.Add(request);
            return respond(request, token);
        });

        var client = new HttpClient(handler) { BaseAddress = new("https://localhost/") };
        Services.AddSingleton(new ComplianceApiClient(client));
        Services.AddSingleton(new FleetApiClient(client));
        Services.AddSingleton(new DocumentsApiClient(client));
        Services.AddSingleton(CreateAuthorizationState(
            permissions.Length == 0 ? [CurrentUserAuthorizationState.ComplianceManagePermission] : permissions));

        listTotalCount = totalCount;
        listPageSize = pageSize;

        var page = Render<NonConformities>();
        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll(".nc-record")));

        return page;
    }

    private int listTotalCount = 3;
    private int listPageSize = 25;

    private void OpenAddActionForm(IRenderedComponent<NonConformities> page, int findingId)
    {
        var index = findingId switch { 10 => 0, 11 => 1, 12 => 2, _ => 0 };
        page.FindAll(".nc-record")[index].Click();
        page.WaitForAssertion(() => Assert.Contains("Add corrective action", page.Markup));
        page.FindAll(".nc-details-actions button")[0].Click();
        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll("#nc-action-text")));
    }

    private void OpenCloseForm(IRenderedComponent<NonConformities> page)
    {
        page.FindAll(".nc-record")[1].Click();
        page.WaitForAssertion(() => Assert.Contains("Close finding", page.Markup));
        page.FindAll(".nc-details-actions button")[1].Click();
        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll("#nc-closure-notes")));
    }

    private static async Task SelectEvidenceDocumentAsync(IRenderedComponent<NonConformities> page)
    {
        page.Find(".document-picker-input").Input("BRZ");
        page.WaitForAssertion(() => Assert.Single(page.FindAll(".document-picker-option")), TimeSpan.FromSeconds(3));
        await page.InvokeAsync(() => page.Find(".document-picker-option").Click());
    }

    private void ClearListRequests() => requests.Clear();

    private List<HttpRequestMessage> ListRequests() =>
        requests.FindAll(IsListGet);

    private static bool IsListGet(HttpRequestMessage request) =>
        request.Method == HttpMethod.Get && request.RequestUri!.AbsolutePath == "/api/compliance/non-conformities";

    private List<string> CorrectiveActionPosts()
    {
        var bodies = new List<string>();
        foreach (var request in requests)
        {
            if (IsCorrectiveActionPost(request, out var body)) bodies.Add(body);
        }

        return bodies;
    }

    private List<string> ClosePosts()
    {
        var bodies = new List<string>();
        foreach (var request in requests)
        {
            if (IsClosePost(request, out var body)) bodies.Add(body);
        }

        return bodies;
    }

    private static bool IsCorrectiveActionPost(HttpRequestMessage request, out string body)
    {
        body = string.Empty;
        if (request.Method != HttpMethod.Post || !request.RequestUri!.AbsolutePath.EndsWith("/corrective-actions", StringComparison.Ordinal))
        {
            return false;
        }

        body = request.Content?.ReadAsStringAsync().GetAwaiter().GetResult() ?? string.Empty;
        return true;
    }

    private static bool IsClosePost(HttpRequestMessage request, out string body)
    {
        body = string.Empty;
        if (request.Method != HttpMethod.Post || !request.RequestUri!.AbsolutePath.EndsWith("/close", StringComparison.Ordinal))
        {
            return false;
        }

        body = request.Content?.ReadAsStringAsync().GetAwaiter().GetResult() ?? string.Empty;
        return true;
    }

    private static string? QueryValue(HttpRequestMessage request, string key) =>
        QueryHelpers.ParseQuery(request.RequestUri!.Query).TryGetValue(key, out var value) ? value.ToString() : null;

    private async Task<HttpResponseMessage> DefaultResponseAsync(HttpRequestMessage request)
    {
        var path = request.RequestUri!.AbsolutePath;

        if (request.Method == HttpMethod.Get && path == "/api/compliance/non-conformities")
        {
            return Json(HttpStatusCode.OK, ListJson());
        }

        if (request.Method == HttpMethod.Get && path.StartsWith("/api/compliance/non-conformities/", StringComparison.Ordinal))
        {
            return path switch
            {
                "/api/compliance/non-conformities/10" => Json(HttpStatusCode.OK, DetailOpenJson),
                "/api/compliance/non-conformities/11" => Json(HttpStatusCode.OK, DetailInProgressJson),
                "/api/compliance/non-conformities/12" => Json(HttpStatusCode.OK, DetailClosedJson),
                _ => Json(HttpStatusCode.NotFound, "{\"title\":\"Not found\"}"),
            };
        }

        if (IsCorrectiveActionPost(request, out _))
        {
            return Json(HttpStatusCode.Created, "{\"id\":\"80000000-0000-0000-0000-000000000009\",\"closureStatus\":\"InProgress\"}");
        }

        if (IsClosePost(request, out _))
        {
            return Json(HttpStatusCode.OK, "{\"id\":11,\"closureStatus\":\"Closed\",\"closedAtUtc\":\"2026-08-20T09:00:00+00:00\"}");
        }

        if (path == "/api/fleet/vessels/lookup")
        {
            return Json(HttpStatusCode.OK, $$"""{"vessels":[{"id":"{{VesselOne}}","name":"Aurora"}]}""");
        }

        if (path == $"/api/documents/{DocOne}")
        {
            return Json(HttpStatusCode.OK, DocumentJson(DocOne, "Bill of Lading", "BRZ-2024-0091"));
        }

        if (path == $"/api/documents/{DocTwo}")
        {
            return Json(HttpStatusCode.OK, DocumentJson(DocTwo, "Cargo Manifest", "CM-2024-0007"));
        }

        if (path == "/api/documents")
        {
            return Json(HttpStatusCode.OK, DocumentsSearchJson);
        }

        return Json(HttpStatusCode.NotFound, "{\"title\":\"Not found\"}");
    }

    private string ListJson() => $$"""
        {
            "items": [
                {"id":10,"vesselId":"{{VesselOne}}","surveyId":1,"pscId":null,"severity":"Major","closureStatus":"Open","correctiveActionDeadline":"2026-12-01","isOverdue":false,"evidenceDocumentId":"{{DocOne}}","correctiveActionCount":0},
                {"id":11,"vesselId":"{{VesselTwo}}","surveyId":null,"pscId":2,"severity":"Critical","closureStatus":"InProgress","correctiveActionDeadline":"2026-01-01","isOverdue":true,"evidenceDocumentId":"{{DocOne}}","correctiveActionCount":2},
                {"id":12,"vesselId":"{{VesselOne}}","surveyId":1,"pscId":null,"severity":"Minor","closureStatus":"Closed","correctiveActionDeadline":"2026-06-01","isOverdue":false,"evidenceDocumentId":"{{DocOne}}","correctiveActionCount":1}
            ],
            "totalCount": {{listTotalCount}},
            "pageNumber": 1,
            "pageSize": {{listPageSize}}
        }
        """;

    private const string DetailOpenJson = """
        {"id":10,"vesselId":"10000000-0000-0000-0000-000000000001","surveyId":1,"pscId":null,"severity":"Major","closureStatus":"Open","correctiveActionDeadline":"2026-12-01","isOverdue":false,"evidenceDocumentId":"60000000-0000-0000-0000-000000000001","closureNotes":null,"closedAtUtc":null,"closedByUserId":null,"finalEvidenceDocumentId":null,"correctiveActions":[]}
        """;

    private const string DetailInProgressJson = """
        {"id":11,"vesselId":"10000000-0000-0000-0000-000000000002","surveyId":null,"pscId":2,"severity":"Critical","closureStatus":"InProgress","correctiveActionDeadline":"2026-01-01","isOverdue":true,"evidenceDocumentId":"60000000-0000-0000-0000-000000000001","closureNotes":null,"closedAtUtc":null,"closedByUserId":null,"finalEvidenceDocumentId":null,"correctiveActions":[
            {"id":"90000000-0000-0000-0000-000000000002","actionText":"Second action - replaced valve","actorUserId":"70000000-0000-0000-0000-000000000002","recordedAtUtc":"2026-08-05T10:00:00+00:00","evidenceDocumentId":null},
            {"id":"90000000-0000-0000-0000-000000000001","actionText":"First action - inspected system","actorUserId":"70000000-0000-0000-0000-000000000001","recordedAtUtc":"2026-08-01T09:00:00+00:00","evidenceDocumentId":"60000000-0000-0000-0000-000000000001"}
        ]}
        """;

    private const string DetailClosedJson = """
        {"id":12,"vesselId":"10000000-0000-0000-0000-000000000001","surveyId":1,"pscId":null,"severity":"Minor","closureStatus":"Closed","correctiveActionDeadline":"2026-06-01","isOverdue":false,"evidenceDocumentId":"60000000-0000-0000-0000-000000000001","closureNotes":"All resolved.","closedAtUtc":"2026-08-10T12:00:00+00:00","closedByUserId":"70000000-0000-0000-0000-000000000003","finalEvidenceDocumentId":"60000000-0000-0000-0000-000000000002","correctiveActions":[
            {"id":"90000000-0000-0000-0000-000000000003","actionText":"Final fix","actorUserId":"70000000-0000-0000-0000-000000000001","recordedAtUtc":"2026-08-09T09:00:00+00:00","evidenceDocumentId":null}
        ]}
        """;

    private static readonly string DocumentsSearchJson = $$"""
        {"items":[{"documentId":"{{DocOne}}","vesselId":"{{VesselOne}}","number":"BRZ-2024-0091","documentTypeName":"Bill of Lading","status":"Active","expiryDate":"2027-01-01"}],"pageNumber":1,"pageSize":10,"totalCount":1}
        """;

    private static string DocumentJson(Guid id, string documentTypeName, string number) => $$"""
        {"id":"{{id}}","vesselId":"{{VesselOne}}","documentTypeId":1,"documentTypeName":"{{documentTypeName}}","number":"{{number}}","issueDate":"2026-01-01","expiryDate":"2027-01-01","issuingAuthority":"Authority","status":"Active","hasFile":false,"fileName":null,"contentType":null,"sizeBytes":null,"uploadedBy":null,"uploadedAtUtc":null}
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

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private sealed class StubHttpMessageHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responseFactory)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => responseFactory(request, cancellationToken);
    }
}
