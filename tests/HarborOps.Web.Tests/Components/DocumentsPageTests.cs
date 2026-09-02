using System.Net;
using System.Security.Claims;
using System.Text;
using Bunit;
using HarborOps.Web.Api;
using HarborOps.Web.Api.Documents;
using HarborOps.Web.Api.Fleet;
using HarborOps.Web.Components.Pages.Documents;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace HarborOps.Web.Tests.Components;

public sealed class DocumentsPageTests : BunitContext
{
    private const string VesselId = "40000000-0000-0000-0000-000000000004";
    private const string DocumentId = "10000000-0000-0000-0000-000000000001";
    private const string UploaderId = "80000000-0000-0000-0000-000000000008";

    private readonly List<Uri> requestedUris = [];

    private Func<HttpRequestMessage, Task<HttpResponseMessage>> respondToExpiring =
        _ => Json(HttpStatusCode.OK, Page());

    private Func<HttpRequestMessage, Task<HttpResponseMessage>> respondToDocumentTypes =
        _ => Json(HttpStatusCode.OK, TypesPage(TypeRow()));

    private Func<HttpRequestMessage, Task<HttpResponseMessage>> respondToRegisterDocument =
        _ => Json(HttpStatusCode.Created, """{"id":"50000000-0000-0000-0000-000000000005"}""");

    private Func<HttpRequestMessage, Task<HttpResponseMessage>> respondToRegisterDocumentType =
        _ => Json(HttpStatusCode.Created, """{"id":99}""");

    private Func<HttpRequestMessage, Task<HttpResponseMessage>> respondToLookup =
        _ => Json(HttpStatusCode.OK, $$"""{"vessels":[{"id":"{{VesselId}}","name":"Aurora Borealis"}]}""");

    private Func<HttpRequestMessage, Task<HttpResponseMessage>> respondToVesselSearch =
        _ => Json(HttpStatusCode.OK, VesselSearchPage());

    private Func<HttpRequestMessage, Task<HttpResponseMessage>> respondToDocumentDetails =
        _ => Json(HttpStatusCode.OK, DocumentDetails());

    private Func<HttpRequestMessage, Task<HttpResponseMessage>> respondToUploadFile =
        _ => Json(HttpStatusCode.Created, FileResponse());

    private Func<HttpRequestMessage, Task<HttpResponseMessage>> respondToReplaceFile =
        _ => Json(HttpStatusCode.OK, FileResponse());

    // ---- Grid ----

    [Fact]
    public void Initial_state_loads_all_documents_without_date_or_vessel_filters()
    {
        var page = RenderDocumentsPage();

        page.WaitForAssertion(() =>
        {
            var initialQuery = requestedUris.First(IsExpiringRequest).Query;
            Assert.DoesNotContain("from=", initialQuery, StringComparison.Ordinal);
            Assert.DoesNotContain("to=", initialQuery, StringComparison.Ordinal);
            Assert.DoesNotContain("vesselId=", initialQuery, StringComparison.Ordinal);
            Assert.Null(page.Find("#documents-from").GetAttribute("value"));
            Assert.Null(page.Find("#documents-to").GetAttribute("value"));
        });
    }

    [Fact]
    public void Rows_are_shown_with_vessel_names_resolved_through_the_fleet_lookup()
    {
        respondToExpiring = _ => Json(HttpStatusCode.OK, Page(Row("10000000-0000-0000-0000-000000000001")));

        var page = RenderDocumentsPage();

        page.WaitForAssertion(() =>
        {
            Assert.Contains("Aurora Borealis", page.Markup, StringComparison.Ordinal);
            Assert.Contains("CERT-001", page.Markup, StringComparison.Ordinal);
        });
    }

    [Theory]
    [InlineData("Active", "documents-status-active")]
    [InlineData("Valid", "documents-status-valid")]
    [InlineData("Expired", "documents-status-expired")]
    [InlineData("Suspended", "documents-status-neutral")]
    public void Status_column_uses_the_matching_colored_badge(string status, string expectedClass)
    {
        respondToExpiring = _ => Json(HttpStatusCode.OK, Page(Row("10000000-0000-0000-0000-000000000001", status: status)));

        var page = RenderDocumentsPage();

        page.WaitForAssertion(() =>
            Assert.Contains(expectedClass, page.Find(".documents-status").ClassName, StringComparison.Ordinal));
    }

    [Fact]
    public void A_vessel_with_no_lookup_match_does_not_render_an_id()
    {
        respondToLookup = _ => Json(HttpStatusCode.OK, """{"vessels":[]}""");
        respondToExpiring = _ => Json(HttpStatusCode.OK, Page(Row("10000000-0000-0000-0000-000000000001")));

        var page = RenderDocumentsPage();

        page.WaitForAssertion(() =>
            Assert.Contains("Unknown vessel", page.Markup, StringComparison.Ordinal));
    }

    [Fact]
    public void Empty_data_shows_the_empty_state_instead_of_a_table()
    {
        respondToExpiring = _ => Json(HttpStatusCode.OK, Page());

        var page = RenderDocumentsPage();

        page.WaitForAssertion(() =>
        {
            Assert.Empty(page.FindAll("table.documents-table"));
            Assert.Contains("No documents match your filters", page.Markup, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void An_unreachable_api_shows_the_error_state_with_a_retry_action()
    {
        respondToExpiring = _ => throw new HttpRequestException("Connection failed.");

        var page = RenderDocumentsPage();

        page.WaitForAssertion(() =>
        {
            Assert.Contains("HarborOps API is unavailable", page.Markup, StringComparison.Ordinal);
            Assert.Empty(page.FindAll("table.documents-table"));
            Assert.NotNull(page.Find(".api-error-state button"));
        });
    }

    [Fact]
    public void Retry_reloads_the_grid_after_a_failure()
    {
        var attempt = 0;
        respondToExpiring = _ =>
        {
            attempt++;

            return attempt == 1
                ? throw new HttpRequestException("Connection failed.")
                : Json(HttpStatusCode.OK, Page(Row("10000000-0000-0000-0000-000000000001", number: "CERT-777")));
        };

        var page = RenderDocumentsPage();
        page.WaitForAssertion(() => Assert.Contains("HarborOps API is unavailable", page.Markup, StringComparison.Ordinal));

        page.Find(".api-error-state button").Click();

        page.WaitForAssertion(() => Assert.Contains("CERT-777", page.Markup, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Changing_filters_automatically_sends_them_and_resets_to_page_one()
    {
        var page = RenderDocumentsPage();
        page.WaitForAssertion(() => Assert.Contains(requestedUris, IsExpiringRequest));

        page.Find("#documents-from").Change("2026-02-01");
        page.Find("#documents-to").Change("2026-02-28");
        page.Find(".documents-filters .vessel-picker-input").Input("Auro");

        page.WaitForAssertion(
            () => Assert.Single(page.FindAll(".documents-filters .vessel-picker-option")),
            TimeSpan.FromSeconds(3));

        // Find and Click are combined into one dispatcher-scheduled action: the debounce
        // timer that just rendered this option can trigger another render at any moment,
        // which would invalidate a separately-found IElement's event handler (bUnit
        // UnknownEventHandlerIdException) before a plain .Find().Click() reaches it.
        await page.InvokeAsync(() => page.Find(".documents-filters .vessel-picker-option").Click());

        page.WaitForAssertion(
            () =>
            {
                var lastQuery = requestedUris.Last(IsExpiringRequest).Query;

                Assert.Contains("from=2026-02-01", lastQuery, StringComparison.Ordinal);
                Assert.Contains("to=2026-02-28", lastQuery, StringComparison.Ordinal);
                Assert.Contains($"vesselId={VesselId}", lastQuery, StringComparison.Ordinal);
                Assert.Contains("pageNumber=1", lastQuery, StringComparison.Ordinal);
            },
            TimeSpan.FromSeconds(3));
    }

    [Fact]
    public void Changing_only_from_automatically_sends_the_lower_bound()
    {
        var page = RenderDocumentsPage();
        page.WaitForAssertion(() => Assert.Contains(requestedUris, IsExpiringRequest));

        page.Find("#documents-from").Change("2026-02-01");

        page.WaitForAssertion(() =>
        {
            var lastQuery = requestedUris.Last(IsExpiringRequest).Query;
            Assert.Contains("from=2026-02-01", lastQuery, StringComparison.Ordinal);
            Assert.DoesNotContain("to=", lastQuery, StringComparison.Ordinal);
        }, TimeSpan.FromSeconds(3));
    }

    [Fact]
    public void Reset_clears_date_filters_resets_paging_and_reloads_unfiltered_results()
    {
        respondToExpiring = request =>
        {
            var requestedPage = request.RequestUri!.Query.Contains("pageNumber=2", StringComparison.Ordinal) ? 2 : 1;
            return Json(HttpStatusCode.OK, Page(
                requestedPage,
                60,
                Row($"0000000{requestedPage}-0000-0000-0000-000000000001", number: $"Page {requestedPage}")));
        };

        var page = RenderDocumentsPage();
        page.WaitForAssertion(() => Assert.Contains("Page 1", page.Markup, StringComparison.Ordinal));

        page.Find("#documents-from").Change("2026-02-01");
        page.Find("#documents-to").Change("2026-02-28");
        page.WaitForAssertion(
            () => Assert.Contains("from=2026-02-01", requestedUris.Last(IsExpiringRequest).Query, StringComparison.Ordinal),
            TimeSpan.FromSeconds(3));

        page.FindAll(".documents-pages button")[1].Click();
        page.WaitForAssertion(() => Assert.Contains("pageNumber=2", requestedUris.Last(IsExpiringRequest).Query, StringComparison.Ordinal));

        page.Find(".documents-filter-reset").Click();

        page.WaitForAssertion(() =>
        {
            var lastQuery = requestedUris.Last(IsExpiringRequest).Query;
            Assert.DoesNotContain("from=", lastQuery, StringComparison.Ordinal);
            Assert.DoesNotContain("to=", lastQuery, StringComparison.Ordinal);
            Assert.DoesNotContain("vesselId=", lastQuery, StringComparison.Ordinal);
            Assert.Contains("pageNumber=1", lastQuery, StringComparison.Ordinal);
            Assert.Null(page.Find("#documents-from").GetAttribute("value"));
            Assert.Null(page.Find("#documents-to").GetAttribute("value"));
        });
    }

    [Fact]
    public void A_to_date_before_the_from_date_shows_a_validation_message_without_calling_the_api()
    {
        var page = RenderDocumentsPage();
        page.WaitForAssertion(() => Assert.Contains(requestedUris, IsExpiringRequest));

        var requestsBeforeSearch = requestedUris.Count(IsExpiringRequest);

        page.Find("#documents-from").Change("2026-06-01");
        page.Find("#documents-to").Change("2026-01-01");

        page.WaitForAssertion(() =>
        {
            Assert.Contains(
                "\"To\" date must be on or after the \"From\" date.",
                page.Find(".documents-validation").TextContent,
                StringComparison.Ordinal);
            Assert.Equal(requestsBeforeSearch, requestedUris.Count(IsExpiringRequest));
        }, TimeSpan.FromSeconds(3));
    }

    [Fact]
    public void Paging_requests_the_next_page_and_disables_previous_on_the_first_page()
    {
        respondToExpiring = request =>
        {
            var pageNumber = request.RequestUri!.Query.Contains("pageNumber=2", StringComparison.Ordinal) ? 2 : 1;

            return Json(HttpStatusCode.OK, Page(
                pageNumber,
                60,
                Row($"0000000{pageNumber}-0000-0000-0000-000000000001", number: $"Page {pageNumber}")));
        };

        var page = RenderDocumentsPage();
        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll("table.documents-table")));

        var previous = page.FindAll(".documents-pages button")[0];
        Assert.True(previous.HasAttribute("disabled"));

        page.FindAll(".documents-pages button")[1].Click();

        page.WaitForAssertion(() =>
        {
            Assert.Contains("Page 2", page.Markup, StringComparison.Ordinal);
            Assert.False(page.FindAll(".documents-pages button")[0].HasAttribute("disabled"));
        });
    }

    // ---- Register document type form ----

    [Fact]
    public void Registration_actions_reveal_modal_forms()
    {
        var page = RenderDocumentsPage();

        Assert.Empty(page.FindAll("form.documents-type-form"));
        Assert.Empty(page.FindAll("form.documents-form"));

        OpenTypeForm(page);
        Assert.Single(page.FindAll("form.documents-type-form"));
        Assert.Single(page.FindAll("[role=dialog]"));
        Assert.NotNull(page.Find(".documents-modal-backdrop"));

        OpenDocumentForm(page);
        Assert.Single(page.FindAll("form.documents-form"));
        Assert.Single(page.FindAll("[role=dialog]"));

        OpenDocumentForm(page);
        Assert.Empty(page.FindAll("form.documents-form"));
    }

    [Fact]
    public void Submitting_an_empty_type_form_shows_client_side_validation_without_calling_the_api()
    {
        var page = RenderDocumentsPage();
        OpenTypeForm(page);
        page.WaitForAssertion(() => Assert.NotEmpty(requestedUris));

        var requestsBeforeSubmit = requestedUris.Count(IsRegisterDocumentTypeRequest);

        page.Find("form.documents-type-form").Submit();

        page.WaitForAssertion(() =>
            Assert.Contains("Name is required.", page.Markup, StringComparison.Ordinal));
        Assert.Equal(requestsBeforeSubmit, requestedUris.Count(IsRegisterDocumentTypeRequest));
    }

    [Fact]
    public void Registering_a_valid_document_type_shows_a_success_message_and_resets_the_form()
    {
        var page = RenderDocumentsPage();
        OpenTypeForm(page);
        page.WaitForAssertion(() => Assert.NotEmpty(requestedUris));

        page.Find("#type-name").Change("Safety Certificate");
        page.Find("#type-issuing-authority").Change("Flag State");
        page.Find("#type-validity-days").Change("365");
        page.FindAll(".documents-type-radio input")[0].Change("True");

        page.Find("form.documents-type-form").Submit();

        page.WaitForAssertion(() =>
            Assert.Contains("Document type registered.", page.Markup, StringComparison.Ordinal));

        Assert.True(string.IsNullOrEmpty(page.Find("#type-name").GetAttribute("value")));
        Assert.True(string.IsNullOrEmpty(page.Find("#type-issuing-authority").GetAttribute("value")));
    }

    [Fact]
    public void Server_validation_error_on_type_name_is_shown_next_to_that_field_and_values_are_preserved()
    {
        respondToRegisterDocumentType = _ => Json(
            HttpStatusCode.BadRequest,
            """{"title":"One or more validation errors occurred.","errors":{"name":["Name must not exceed 200 characters."]}}""");

        var page = RenderDocumentsPage();
        OpenTypeForm(page);
        page.WaitForAssertion(() => Assert.NotEmpty(requestedUris));

        page.Find("#type-name").Change("Safety Certificate");
        page.Find("#type-issuing-authority").Change("Flag State");
        page.Find("#type-validity-days").Change("365");
        page.FindAll(".documents-type-radio input")[0].Change("True");

        page.Find("form.documents-type-form").Submit();

        page.WaitForAssertion(() =>
        {
            var nameField = page.Find("#type-name").ParentElement!;

            Assert.Contains(
                "Name must not exceed 200 characters.",
                nameField.QuerySelector(".validation-message")!.TextContent,
                StringComparison.Ordinal);
        });

        Assert.Equal("Safety Certificate", page.Find("#type-name").GetAttribute("value"));
    }

    [Fact]
    public void A_duplicate_type_name_shows_a_conflict_banner_and_preserves_values()
    {
        respondToRegisterDocumentType = _ => Json(
            HttpStatusCode.Conflict,
            """{"title":"A document type with this name already exists.","errors":{"name":["A document type with name 'Safety Certificate' already exists."]}}""");

        var page = RenderDocumentsPage();
        OpenTypeForm(page);
        page.WaitForAssertion(() => Assert.NotEmpty(requestedUris));

        page.Find("#type-name").Change("Safety Certificate");
        page.Find("#type-issuing-authority").Change("Flag State");
        page.Find("#type-validity-days").Change("365");
        page.FindAll(".documents-type-radio input")[0].Change("True");

        page.Find("form.documents-type-form").Submit();

        page.WaitForAssertion(() =>
            Assert.Contains(
                "A document type with name 'Safety Certificate' already exists.",
                page.Find(".documents-type-conflict").TextContent,
                StringComparison.Ordinal));

        Assert.Equal("Safety Certificate", page.Find("#type-name").GetAttribute("value"));
    }

    [Fact]
    public void A_general_registration_api_error_remains_visible_in_the_inline_form()
    {
        respondToRegisterDocumentType = _ => Json(
            HttpStatusCode.InternalServerError,
            """{"title":"Unable to register document type","detail":"Please try again later.","errors":{}}""");

        var page = RenderDocumentsPage();
        OpenTypeForm(page);
        page.Find("#type-name").Change("Safety Certificate");
        page.Find("#type-issuing-authority").Change("Flag State");
        page.Find("#type-validity-days").Change("365");
        page.FindAll(".documents-type-radio input")[0].Change("True");

        page.Find("form.documents-type-form").Submit();

        page.WaitForAssertion(() =>
            Assert.Contains("Please try again later.", page.Find(".documents-type-inline-error").TextContent));
        Assert.Single(page.FindAll("form.documents-type-form"));
        Assert.Equal("Safety Certificate", page.Find("#type-name").GetAttribute("value"));
    }

    [Fact]
    public void Submitting_the_type_form_twice_quickly_only_sends_one_request_while_the_first_is_pending()
    {
        var pending = new TaskCompletionSource<HttpResponseMessage>();
        var typeRequestCount = 0;

        respondToRegisterDocumentType = _ =>
        {
            typeRequestCount++;

            return pending.Task;
        };

        var page = RenderDocumentsPage();
        OpenTypeForm(page);
        page.WaitForAssertion(() => Assert.NotEmpty(requestedUris));

        page.Find("#type-name").Change("Safety Certificate");
        page.Find("#type-issuing-authority").Change("Flag State");
        page.Find("#type-validity-days").Change("365");
        page.FindAll(".documents-type-radio input")[0].Change("True");

        var form = page.Find("form.documents-type-form");
        form.Submit();

        page.WaitForAssertion(() => Assert.Equal(1, typeRequestCount));
        Assert.True(page.Find("form.documents-type-form button[type=submit]").HasAttribute("disabled"));

        form.Submit();
        Assert.Equal(1, typeRequestCount);

        pending.SetResult(new HttpResponseMessage(HttpStatusCode.Created)
        {
            Content = new StringContent("""{"id":99}""", Encoding.UTF8, "application/json"),
        });

        page.WaitForAssertion(() =>
            Assert.Contains("Document type registered.", page.Markup, StringComparison.Ordinal));
    }

    // ---- Register document form ----

    [Fact]
    public void Submitting_an_empty_document_form_shows_client_side_validation_without_calling_the_api()
    {
        var page = RenderDocumentsPage();
        OpenDocumentForm(page);
        page.WaitForAssertion(() => Assert.NotEmpty(requestedUris));

        var requestsBeforeSubmit = requestedUris.Count(IsRegisterDocumentRequest);

        page.Find("form.documents-form").Submit();

        page.WaitForAssertion(() =>
            Assert.Contains("Document number is required.", page.Markup, StringComparison.Ordinal));
        Assert.Contains("Vessel is required.", page.Markup, StringComparison.Ordinal);
        Assert.Contains("Document type is required.", page.Markup, StringComparison.Ordinal);
        Assert.Contains("Issuing authority is required.", page.Markup, StringComparison.Ordinal);
        Assert.Equal(requestsBeforeSubmit, requestedUris.Count(IsRegisterDocumentRequest));
    }

    [Fact]
    public async Task An_expiry_date_before_the_issue_date_shows_a_cross_field_validation_message_without_calling_the_api()
    {
        var page = RenderDocumentsPage();
        OpenDocumentForm(page);
        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll("#doc-type option")));

        var requestsBeforeSubmit = requestedUris.Count(IsRegisterDocumentRequest);

        // Every other required field is filled in so the cross-field check is the only
        // thing standing between this submit and the API call.
        page.Find("#doc-number").Change("CERT-001");
        page.Find(".documents-form-grid .vessel-picker-input").Input("Auro");
        page.WaitForAssertion(
            () => Assert.Single(page.FindAll(".documents-form-grid .vessel-picker-option")),
            TimeSpan.FromSeconds(3));
        // See the comment on the equivalent grid-filter click above: Find and Click must be
        // dispatched as one action to avoid a stale event handler if the debounced search
        // triggers another render between finding and clicking the option.
        await page.InvokeAsync(() => page.Find(".documents-form-grid .vessel-picker-option").Click());
        page.Find("#doc-type").Change("1");
        page.Find("#doc-issue-date").Change("2026-06-01");
        page.Find("#doc-expiry-date").Change("2026-01-01");
        page.Find("#doc-issuing-authority").Change("Flag State");

        page.Find("form.documents-form").Submit();

        page.WaitForAssertion(
            () => Assert.Contains(
                "Expiry date must not be earlier than the issue date.",
                page.Markup,
                StringComparison.Ordinal),
            TimeSpan.FromSeconds(3));
        Assert.Equal(requestsBeforeSubmit, requestedUris.Count(IsRegisterDocumentRequest));
    }

    [Fact]
    public void Document_type_options_are_populated_from_the_endpoint_with_readable_labels()
    {
        respondToDocumentTypes = _ => Json(
            HttpStatusCode.OK,
            TypesPage(TypeRow(documentTypeId: 7, name: "Load Line Certificate", mandatoryValidityPeriodDays: 60)));

        var page = RenderDocumentsPage();
        OpenDocumentForm(page);

        page.WaitForAssertion(() =>
            Assert.Contains("Load Line Certificate (60 days)", page.Find("#doc-type").TextContent, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Selecting_a_vessel_through_the_picker_and_submitting_sends_its_id_in_the_request()
    {
        string? capturedBody = null;
        respondToRegisterDocument = async request =>
        {
            capturedBody = await request.Content!.ReadAsStringAsync();

            return new HttpResponseMessage(HttpStatusCode.Created)
            {
                Content = new StringContent(
                    """{"id":"50000000-0000-0000-0000-000000000005"}""", Encoding.UTF8, "application/json"),
            };
        };

        var page = RenderDocumentsPage();
        OpenDocumentForm(page);
        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll("#doc-type option")));

        page.Find("#doc-number").Change("CERT-001");
        page.Find(".documents-form-grid .vessel-picker-input").Input("Auro");

        page.WaitForAssertion(
            () => Assert.Single(page.FindAll(".documents-form-grid .vessel-picker-option")),
            TimeSpan.FromSeconds(3));

        // See the comment on the equivalent grid-filter click above: Find and Click must be
        // dispatched as one action to avoid a stale event handler if the debounced search
        // triggers another render between finding and clicking the option.
        await page.InvokeAsync(() => page.Find(".documents-form-grid .vessel-picker-option").Click());

        page.Find("#doc-type").Change("1");
        page.Find("#doc-issue-date").Change("2026-01-01");
        page.Find("#doc-expiry-date").Change("2026-12-31");
        page.Find("#doc-issuing-authority").Change("Flag State");

        page.Find("form.documents-form").Submit();

        page.WaitForAssertion(() => Assert.NotNull(capturedBody), TimeSpan.FromSeconds(3));
        Assert.Contains($"\"vesselId\":\"{VesselId}\"", capturedBody, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Registering_a_valid_document_shows_a_success_message_resets_the_form_and_refreshes_the_grid()
    {
        var page = RenderDocumentsPage();

        page.Find("#documents-from").Change("2026-01-01");
        page.Find("#documents-to").Change("2026-12-31");
        page.Find(".documents-filters .vessel-picker-input").Input("Auro");
        page.WaitForAssertion(
            () => Assert.Single(page.FindAll(".documents-filters .vessel-picker-option")),
            TimeSpan.FromSeconds(3));
        await page.InvokeAsync(() => page.Find(".documents-filters .vessel-picker-option").Click());
        page.WaitForAssertion(() =>
            Assert.Contains("from=2026-01-01", requestedUris.Last(IsExpiringRequest).Query, StringComparison.Ordinal));

        OpenDocumentForm(page);
        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll("#doc-type option")));

        var expiringRequestsBefore = requestedUris.Count(IsExpiringRequest);

        page.Find("#doc-number").Change("CERT-001");
        page.Find(".documents-form-grid .vessel-picker-input").Input("Auro");
        page.WaitForAssertion(
            () => Assert.Single(page.FindAll(".documents-form-grid .vessel-picker-option")),
            TimeSpan.FromSeconds(3));
        // See the comment on the equivalent grid-filter click above: Find and Click must be
        // dispatched as one action to avoid a stale event handler if the debounced search
        // triggers another render between finding and clicking the option.
        await page.InvokeAsync(() => page.Find(".documents-form-grid .vessel-picker-option").Click());

        page.Find("#doc-type").Change("1");
        page.Find("#doc-issue-date").Change("2026-01-01");
        page.Find("#doc-expiry-date").Change("2026-12-31");
        page.Find("#doc-issuing-authority").Change("Flag State");

        page.Find("form.documents-form").Submit();

        page.WaitForAssertion(
            () => Assert.Contains("Document registered.", page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(3));

        Assert.True(string.IsNullOrEmpty(page.Find("#doc-number").GetAttribute("value")));
        Assert.True(string.IsNullOrEmpty(page.Find("#doc-issuing-authority").GetAttribute("value")));
        Assert.True(requestedUris.Count(IsExpiringRequest) > expiringRequestsBefore);
        Assert.Single(page.FindAll("form.documents-form"));

        var refreshedQuery = requestedUris.Last(IsExpiringRequest).Query;
        Assert.Contains("from=2026-01-01", refreshedQuery, StringComparison.Ordinal);
        Assert.Contains("to=2026-12-31", refreshedQuery, StringComparison.Ordinal);
        Assert.Contains($"vesselId={VesselId}", refreshedQuery, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Server_validation_error_on_document_type_id_is_shown_next_to_that_field()
    {
        respondToRegisterDocument = _ => Json(
            HttpStatusCode.BadRequest,
            """{"title":"One or more validation errors occurred.","errors":{"documentTypeId":["Document type is required."]}}""");

        var page = RenderDocumentsPage();
        OpenDocumentForm(page);
        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll("#doc-type option")));

        page.Find("#doc-number").Change("CERT-001");
        page.Find(".documents-form-grid .vessel-picker-input").Input("Auro");
        page.WaitForAssertion(
            () => Assert.Single(page.FindAll(".documents-form-grid .vessel-picker-option")),
            TimeSpan.FromSeconds(3));
        // See the comment on the equivalent grid-filter click above: Find and Click must be
        // dispatched as one action to avoid a stale event handler if the debounced search
        // triggers another render between finding and clicking the option.
        await page.InvokeAsync(() => page.Find(".documents-form-grid .vessel-picker-option").Click());

        page.Find("#doc-type").Change("1");
        page.Find("#doc-issue-date").Change("2026-01-01");
        page.Find("#doc-expiry-date").Change("2026-12-31");
        page.Find("#doc-issuing-authority").Change("Flag State");

        page.Find("form.documents-form").Submit();

        page.WaitForAssertion(
            () =>
            {
                var typeField = page.Find("#doc-type").ParentElement!;

                Assert.Contains(
                    "Document type is required.",
                    typeField.QuerySelector(".validation-message")!.TextContent,
                    StringComparison.Ordinal);
            },
            TimeSpan.FromSeconds(3));
    }

    // ---- File panel ----

    [Fact]
    public void Clicking_the_file_toggle_opens_a_dialog_with_the_file_details_and_closing_dismisses_it()
    {
        respondToExpiring = _ => Json(HttpStatusCode.OK, Page(Row(DocumentId)));
        respondToDocumentDetails = _ => Json(HttpStatusCode.OK, DocumentDetails(hasFile: false));

        var page = RenderDocumentsPage();
        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll(".documents-file-toggle")));

        page.Find(".documents-file-toggle").Click();

        page.WaitForAssertion(() =>
        {
            Assert.Contains(requestedUris, IsDocumentDetailsRequest);
            Assert.Contains("No file attached.", page.Markup, StringComparison.Ordinal);

            var dialog = page.Find(".documents-modal-backdrop .documents-file-form-card");

            Assert.Equal("dialog", dialog.GetAttribute("role"));
            Assert.Equal("true", dialog.GetAttribute("aria-modal"));
        });

        page.Find(".documents-file-close-button").Click();

        page.WaitForAssertion(() => Assert.Empty(page.FindAll(".documents-file-form-card")));
    }

    [Fact]
    public void Clicking_the_dialog_backdrop_closes_the_file_dialog()
    {
        respondToExpiring = _ => Json(HttpStatusCode.OK, Page(Row(DocumentId)));
        respondToDocumentDetails = _ => Json(HttpStatusCode.OK, DocumentDetails(hasFile: false));

        var page = RenderDocumentsPage();
        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll(".documents-file-toggle")));

        page.Find(".documents-file-toggle").Click();
        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll(".documents-file-form-card")));

        page.Find(".documents-modal-backdrop").Click();

        Assert.Empty(page.FindAll(".documents-file-form-card"));
    }

    [Fact]
    public void Upload_form_is_shown_for_a_document_without_a_file_and_uploading_shows_success_with_refreshed_metadata()
    {
        respondToExpiring = _ => Json(HttpStatusCode.OK, Page(Row(DocumentId)));

        var detailsCallCount = 0;
        respondToDocumentDetails = _ =>
        {
            detailsCallCount++;

            return Json(HttpStatusCode.OK, DocumentDetails(
                hasFile: detailsCallCount > 1,
                fileName: detailsCallCount > 1 ? "cert.pdf" : null,
                contentType: detailsCallCount > 1 ? "application/pdf" : null,
                sizeBytes: detailsCallCount > 1 ? 2048 : null,
                uploadedAtUtc: detailsCallCount > 1 ? "2026-08-19T10:00:00Z" : null));
        };

        string? capturedBody = null;
        HttpMethod? capturedMethod = null;
        respondToUploadFile = async request =>
        {
            capturedMethod = request.Method;
            capturedBody = await request.Content!.ReadAsStringAsync();

            return new HttpResponseMessage(HttpStatusCode.Created)
            {
                Content = new StringContent(FileResponse(fileName: "cert.pdf"), Encoding.UTF8, "application/json"),
            };
        };

        var page = RenderDocumentsPage("documents.manage", "documents.read");
        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll(".documents-file-toggle")));
        page.Find(".documents-file-toggle").Click();

        page.WaitForAssertion(() =>
        {
            Assert.Contains("No file attached.", page.Markup, StringComparison.Ordinal);
            Assert.NotEmpty(page.FindAll(".documents-file-upload-form"));
        });

        var inputFile = page.FindComponent<InputFile>();
        inputFile.UploadFiles(InputFileContent.CreateFromText("dummy pdf bytes", "cert.pdf", contentType: "application/pdf"));

        page.Find(".documents-file-upload-button").Click();

        page.WaitForAssertion(() =>
        {
            Assert.Contains("File uploaded.", page.Markup, StringComparison.Ordinal);
            Assert.Contains("cert.pdf", page.Find(".documents-file-meta").TextContent, StringComparison.Ordinal);
            Assert.DoesNotContain("No file attached.", page.Markup, StringComparison.Ordinal);
        });

        Assert.Equal(HttpMethod.Post, capturedMethod);
        Assert.Contains("cert.pdf", capturedBody, StringComparison.Ordinal);
        Assert.True(detailsCallCount >= 2);
    }

    [Fact]
    public void Uploading_shows_the_busy_upload_indicator_while_the_request_is_pending()
    {
        respondToExpiring = _ => Json(HttpStatusCode.OK, Page(Row(DocumentId)));
        respondToDocumentDetails = _ => Json(HttpStatusCode.OK, DocumentDetails(hasFile: false));

        var pending = new TaskCompletionSource<HttpResponseMessage>();
        respondToUploadFile = _ => pending.Task;

        var page = RenderDocumentsPage("documents.manage", "documents.read");
        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll(".documents-file-toggle")));
        page.Find(".documents-file-toggle").Click();
        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll(".documents-file-upload-form")));

        var inputFile = page.FindComponent<InputFile>();
        inputFile.UploadFiles(InputFileContent.CreateFromText("dummy pdf bytes", "cert.pdf", contentType: "application/pdf"));

        page.Find(".documents-file-upload-button").Click();

        page.WaitForAssertion(() =>
        {
            Assert.NotEmpty(page.FindAll(".documents-file-progress"));
            var uploadButton = page.Find(".documents-file-upload-button");
            Assert.Equal("Uploading...", uploadButton.TextContent.Trim());
            Assert.True(uploadButton.HasAttribute("disabled"));
        });

        pending.SetResult(new HttpResponseMessage(HttpStatusCode.Created)
        {
            Content = new StringContent(FileResponse(), Encoding.UTF8, "application/json"),
        });

        page.WaitForAssertion(() => Assert.Contains("File uploaded.", page.Markup, StringComparison.Ordinal));
    }

    [Fact]
    public void Replace_file_shows_a_confirmation_before_the_upload_form_and_cancel_returns_without_a_request()
    {
        respondToExpiring = _ => Json(HttpStatusCode.OK, Page(Row(DocumentId)));
        respondToDocumentDetails = _ => Json(HttpStatusCode.OK, DocumentDetails(
            hasFile: true,
            fileName: "cert.pdf",
            contentType: "application/pdf",
            sizeBytes: 2048,
            uploadedAtUtc: "2026-08-01T10:00:00Z"));

        var page = RenderDocumentsPage("documents.manage", "documents.read");
        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll(".documents-file-toggle")));
        page.Find(".documents-file-toggle").Click();
        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll(".documents-file-replace-button")));

        page.Find(".documents-file-replace-button").Click();

        page.WaitForAssertion(() =>
        {
            Assert.Contains("Replace the current file?", page.Markup, StringComparison.Ordinal);
            Assert.Empty(page.FindAll(".documents-file-upload-form"));
        });

        var requestsBeforeCancel = requestedUris.Count;

        page.Find(".documents-file-replace-cancel-button").Click();

        page.WaitForAssertion(() =>
        {
            Assert.Empty(page.FindAll(".documents-file-replace-confirm"));
            Assert.NotEmpty(page.FindAll(".documents-file-replace-button"));
        });
        Assert.Equal(requestsBeforeCancel, requestedUris.Count);

        page.Find(".documents-file-replace-button").Click();
        page.Find(".documents-file-replace-confirm-button").Click();

        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll(".documents-file-upload-form")));
    }

    [Fact]
    public void Replacing_a_file_sends_a_put_request_and_shows_success_with_refreshed_metadata()
    {
        respondToExpiring = _ => Json(HttpStatusCode.OK, Page(Row(DocumentId)));

        var detailsCallCount = 0;
        respondToDocumentDetails = _ =>
        {
            detailsCallCount++;

            return Json(HttpStatusCode.OK, DocumentDetails(
                hasFile: true,
                fileName: detailsCallCount > 1 ? "replacement.pdf" : "original.pdf",
                contentType: "application/pdf",
                sizeBytes: detailsCallCount > 1 ? 4096 : 2048,
                uploadedAtUtc: "2026-08-19T12:00:00Z"));
        };

        HttpMethod? capturedMethod = null;
        respondToReplaceFile = request =>
        {
            capturedMethod = request.Method;

            return Json(HttpStatusCode.OK, FileResponse(fileName: "replacement.pdf", sizeBytes: 4096));
        };

        var page = RenderDocumentsPage("documents.manage", "documents.read");
        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll(".documents-file-toggle")));
        page.Find(".documents-file-toggle").Click();
        page.WaitForAssertion(() => Assert.Contains("original.pdf", page.Markup, StringComparison.Ordinal));

        page.Find(".documents-file-replace-button").Click();
        page.Find(".documents-file-replace-confirm-button").Click();
        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll(".documents-file-upload-form")));

        var inputFile = page.FindComponent<InputFile>();
        inputFile.UploadFiles(InputFileContent.CreateFromText("new pdf bytes", "replacement.pdf", contentType: "application/pdf"));

        page.Find(".documents-file-upload-button").Click();

        page.WaitForAssertion(() =>
        {
            Assert.Contains("File replaced.", page.Markup, StringComparison.Ordinal);
            Assert.Contains("replacement.pdf", page.Find(".documents-file-meta").TextContent, StringComparison.Ordinal);
        });

        Assert.Equal(HttpMethod.Put, capturedMethod);
        Assert.True(detailsCallCount >= 2);
    }

    [Fact]
    public void A_forbidden_response_on_upload_shows_the_server_message()
    {
        respondToExpiring = _ => Json(HttpStatusCode.OK, Page(Row(DocumentId)));
        respondToDocumentDetails = _ => Json(HttpStatusCode.OK, DocumentDetails(hasFile: false));
        respondToUploadFile = _ => Json(
            HttpStatusCode.Forbidden,
            """{"title":"Forbidden","detail":"You are not allowed to upload files for this document.","errors":{}}""");

        var page = RenderDocumentsPage("documents.manage", "documents.read");
        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll(".documents-file-toggle")));
        page.Find(".documents-file-toggle").Click();
        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll(".documents-file-upload-form")));

        var inputFile = page.FindComponent<InputFile>();
        inputFile.UploadFiles(InputFileContent.CreateFromText("dummy pdf bytes", "cert.pdf", contentType: "application/pdf"));

        page.Find(".documents-file-upload-button").Click();

        page.WaitForAssertion(() =>
            Assert.Contains(
                "You are not allowed to upload files for this document.",
                page.Find(".documents-type-inline-error").TextContent,
                StringComparison.Ordinal));
    }

    [Fact]
    public void A_validation_error_on_upload_shows_the_field_error_list()
    {
        respondToExpiring = _ => Json(HttpStatusCode.OK, Page(Row(DocumentId)));
        respondToDocumentDetails = _ => Json(HttpStatusCode.OK, DocumentDetails(hasFile: false));
        respondToUploadFile = _ => Json(
            HttpStatusCode.BadRequest,
            """{"title":"One or more validation errors occurred.","errors":{"file":["The file extension is not allowed."]}}""");

        var page = RenderDocumentsPage("documents.manage", "documents.read");
        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll(".documents-file-toggle")));
        page.Find(".documents-file-toggle").Click();
        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll(".documents-file-upload-form")));

        var inputFile = page.FindComponent<InputFile>();
        inputFile.UploadFiles(InputFileContent.CreateFromText("dummy bytes", "cert.exe", contentType: "application/octet-stream"));

        page.Find(".documents-file-upload-button").Click();

        page.WaitForAssertion(() =>
            Assert.Contains(
                "The file extension is not allowed.",
                page.Find(".documents-type-field-errors").TextContent,
                StringComparison.Ordinal));
    }

    [Fact]
    public void A_payload_too_large_response_from_the_server_shows_the_max_size_message()
    {
        respondToExpiring = _ => Json(HttpStatusCode.OK, Page(Row(DocumentId)));
        respondToDocumentDetails = _ => Json(HttpStatusCode.OK, DocumentDetails(hasFile: false));
        respondToUploadFile = _ => Json(
            HttpStatusCode.RequestEntityTooLarge,
            """{"title":"Payload too large","errors":{}}""");

        var page = RenderDocumentsPage("documents.manage", "documents.read");
        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll(".documents-file-toggle")));
        page.Find(".documents-file-toggle").Click();
        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll(".documents-file-upload-form")));

        var inputFile = page.FindComponent<InputFile>();
        inputFile.UploadFiles(InputFileContent.CreateFromText("dummy pdf bytes", "cert.pdf", contentType: "application/pdf"));

        page.Find(".documents-file-upload-button").Click();

        page.WaitForAssertion(() =>
            Assert.Contains(
                "File is too large. Maximum allowed size is 25 MB.",
                page.Find(".documents-type-inline-error").TextContent,
                StringComparison.Ordinal));
    }

    [Fact]
    public void A_server_error_on_upload_shows_a_generic_inline_error_without_the_page_breaking()
    {
        respondToExpiring = _ => Json(HttpStatusCode.OK, Page(Row(DocumentId)));
        respondToDocumentDetails = _ => Json(HttpStatusCode.OK, DocumentDetails(hasFile: false));
        respondToUploadFile = _ => Json(
            HttpStatusCode.InternalServerError,
            """{"title":"Unable to upload the document file","detail":"The file storage is currently unavailable.","errors":{}}""");

        var page = RenderDocumentsPage("documents.manage", "documents.read");
        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll(".documents-file-toggle")));
        page.Find(".documents-file-toggle").Click();
        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll(".documents-file-upload-form")));

        var inputFile = page.FindComponent<InputFile>();
        inputFile.UploadFiles(InputFileContent.CreateFromText("dummy pdf bytes", "cert.pdf", contentType: "application/pdf"));

        page.Find(".documents-file-upload-button").Click();

        page.WaitForAssertion(() =>
            Assert.Contains(
                "The file storage is currently unavailable.",
                page.Find(".documents-type-inline-error").TextContent,
                StringComparison.Ordinal));
        Assert.Single(page.FindAll(".documents-file-upload-form"));
    }

    [Fact]
    public void Manage_only_permission_shows_the_replace_button_but_hides_the_download_link()
    {
        respondToExpiring = _ => Json(HttpStatusCode.OK, Page(Row(DocumentId)));
        respondToDocumentDetails = _ => Json(HttpStatusCode.OK, DocumentDetails(
            hasFile: true,
            fileName: "cert.pdf",
            contentType: "application/pdf",
            sizeBytes: 2048,
            uploadedAtUtc: "2026-08-01T10:00:00Z"));

        var page = RenderDocumentsPage("documents.manage");
        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll(".documents-file-toggle")));
        page.Find(".documents-file-toggle").Click();

        page.WaitForAssertion(() =>
        {
            Assert.Empty(page.FindAll(".documents-file-download-link"));
            Assert.NotEmpty(page.FindAll(".documents-file-replace-button"));
        });
    }

    [Fact]
    public void Read_only_permission_shows_the_download_link_but_hides_the_replace_button()
    {
        respondToExpiring = _ => Json(HttpStatusCode.OK, Page(Row(DocumentId)));
        respondToDocumentDetails = _ => Json(HttpStatusCode.OK, DocumentDetails(
            hasFile: true,
            fileName: "cert.pdf",
            contentType: "application/pdf",
            sizeBytes: 2048,
            uploadedAtUtc: "2026-08-01T10:00:00Z"));

        var page = RenderDocumentsPage("documents.read");
        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll(".documents-file-toggle")));
        page.Find(".documents-file-toggle").Click();

        page.WaitForAssertion(() =>
        {
            Assert.NotEmpty(page.FindAll(".documents-file-download-link"));
            Assert.Empty(page.FindAll(".documents-file-replace-button"));
        });
    }

    // ---- Cross-form refresh ----

    [Fact]
    public void Registering_a_document_type_refreshes_the_document_type_dropdown_options()
    {
        var page = RenderDocumentsPage();
        OpenTypeForm(page);

        var typesRequestsBefore = requestedUris.Count(IsDocumentTypesGetRequest);

        respondToDocumentTypes = _ => Json(
            HttpStatusCode.OK,
            TypesPage(TypeRow(documentTypeId: 8, name: "New Survey Report", mandatoryValidityPeriodDays: 30)));

        page.Find("#type-name").Change("New Survey Report");
        page.Find("#type-issuing-authority").Change("Flag State");
        page.Find("#type-validity-days").Change("30");
        page.FindAll(".documents-type-radio input")[0].Change("True");

        page.Find("form.documents-type-form").Submit();

        page.WaitForAssertion(() =>
            Assert.Contains("Document type registered.", page.Markup, StringComparison.Ordinal));

        Assert.True(requestedUris.Count(IsDocumentTypesGetRequest) > typesRequestsBefore);

        page.Find(".documents-modal-cancel").Click();
        OpenDocumentForm(page);
        page.WaitForAssertion(() =>
            Assert.Contains("New Survey Report (30 days)", page.Find("#doc-type").TextContent, StringComparison.Ordinal));
    }

    private static bool IsExpiringRequest(Uri uri) =>
        uri.AbsolutePath.EndsWith("/expiring", StringComparison.Ordinal);

    private static bool IsDocumentTypesGetRequest(Uri uri) =>
        uri.AbsolutePath.EndsWith("/types", StringComparison.Ordinal);

    private static bool IsRegisterDocumentRequest(Uri uri) =>
        uri.AbsolutePath.EndsWith("/documents", StringComparison.Ordinal);

    private static bool IsRegisterDocumentTypeRequest(Uri uri) =>
        uri.AbsolutePath.EndsWith("/types", StringComparison.Ordinal);

    private static bool IsDocumentDetailsRequest(Uri uri) =>
        uri.AbsolutePath.EndsWith($"/{DocumentId}", StringComparison.OrdinalIgnoreCase);

    private static void OpenTypeForm(IRenderedComponent<Documents> page) =>
        page.FindAll(".workspace-tab")[0].Click();

    private static void OpenDocumentForm(IRenderedComponent<Documents> page) =>
        page.FindAll(".workspace-tab")[1].Click();

    private IRenderedComponent<Documents> RenderDocumentsPage(params string[] permissions)
    {
        var documentsHttpClient = new HttpClient(new StubHttpMessageHandler(request =>
        {
            requestedUris.Add(request.RequestUri!);

            return DispatchDocuments(request);
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

        Services.AddSingleton(new DocumentsApiClient(documentsHttpClient));
        Services.AddSingleton(new FleetApiClient(fleetHttpClient));
        Services.AddSingleton(CreateAuthorizationState(
            permissions.Length == 0 ? ["documents.read", "documents.manage"] : permissions));

        var page = Render<Documents>();
        page.WaitForAssertion(() => Assert.NotEmpty(requestedUris));

        return page;
    }

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

    private Task<HttpResponseMessage> DispatchDocuments(HttpRequestMessage request)
    {
        var path = request.RequestUri!.AbsolutePath;

        if (path.EndsWith("/expiring", StringComparison.Ordinal))
        {
            return respondToExpiring(request);
        }

        if (path.EndsWith("/types", StringComparison.Ordinal))
        {
            return request.Method == HttpMethod.Get
                ? respondToDocumentTypes(request)
                : respondToRegisterDocumentType(request);
        }

        if (path.EndsWith("/file", StringComparison.Ordinal))
        {
            return request.Method == HttpMethod.Post
                ? respondToUploadFile(request)
                : respondToReplaceFile(request);
        }

        if (request.Method == HttpMethod.Get)
        {
            return respondToDocumentDetails(request);
        }

        return respondToRegisterDocument(request);
    }

    private static string Row(
        string documentId,
        string number = "CERT-001",
        string status = "Active",
        string reviewStatus = "Draft",
        string documentTypeName = "Safety Certificate",
        string issuingAuthority = "Flag State",
        string issueDate = "2026-01-01",
        string expiryDate = "2026-12-31",
        int daysUntilExpiry = 120)
    {
        return $$"""
            {"documentId":"{{documentId}}","vesselId":"{{VesselId}}","number":"{{number}}",
            "issueDate":"{{issueDate}}","expiryDate":"{{expiryDate}}","issuingAuthority":"{{issuingAuthority}}",
            "status":"{{status}}","reviewStatus":"{{reviewStatus}}","documentTypeName":"{{documentTypeName}}",
            "daysUntilExpiry":{{daysUntilExpiry}}}
            """;
    }

    private static string Page(params string[] rows) => Page(1, rows.Length, rows);

    private static string Page(int pageNumber, int totalCount, params string[] rows)
    {
        return $$"""
            {"items":[{{string.Join(',', rows)}}],"pageNumber":{{pageNumber}},"pageSize":25,"totalCount":{{totalCount}}}
            """;
    }

    private static string TypeRow(
        int documentTypeId = 1,
        string name = "Safety Certificate",
        int mandatoryValidityPeriodDays = 365,
        string issuingAuthority = "Flag State",
        bool requiresSurvey = true)
    {
        var requiresSurveyText = requiresSurvey ? "true" : "false";

        return $$"""
            {"documentTypeId":{{documentTypeId}},"name":"{{name}}",
            "mandatoryValidityPeriodDays":{{mandatoryValidityPeriodDays}},
            "issuingAuthority":"{{issuingAuthority}}","requiresSurvey":{{requiresSurveyText}}}
            """;
    }

    private static string TypesPage(params string[] rows) => TypesPage(rows.Length, rows);

    private static string TypesPage(int totalCount, params string[] rows)
    {
        return $$"""
            {"items":[{{string.Join(',', rows)}}],"pageSize":100,"totalCount":{{totalCount}}}
            """;
    }

    private static string DocumentDetails(
        string documentId = DocumentId,
        bool hasFile = false,
        string reviewStatus = "Draft",
        string? fileName = null,
        string? contentType = null,
        long? sizeBytes = null,
        string? uploadedAtUtc = null)
    {
        var fileNameJson = fileName is null ? "null" : $"\"{fileName}\"";
        var contentTypeJson = contentType is null ? "null" : $"\"{contentType}\"";
        var sizeBytesJson = sizeBytes is null ? "null" : sizeBytes.Value.ToString();
        var uploadedByJson = hasFile ? $"\"{UploaderId}\"" : "null";
        var uploadedAtJson = uploadedAtUtc is null ? "null" : $"\"{uploadedAtUtc}\"";

        return $$"""
            {"id":"{{documentId}}","vesselId":"{{VesselId}}","documentTypeId":1,"documentTypeName":"Safety Certificate",
            "number":"CERT-001","issueDate":"2026-01-01","expiryDate":"2026-12-31","issuingAuthority":"Flag State",
            "status":"Active","reviewStatus":"{{reviewStatus}}","hasFile":{{(hasFile ? "true" : "false")}},
            "fileName":{{fileNameJson}},"contentType":{{contentTypeJson}},"sizeBytes":{{sizeBytesJson}},
            "uploadedBy":{{uploadedByJson}},"uploadedAtUtc":{{uploadedAtJson}}}
            """;
    }

    private static string FileResponse(
        string fileName = "certificate.pdf",
        string contentType = "application/pdf",
        long sizeBytes = 1024,
        string uploadedAtUtc = "2026-08-19T10:00:00Z")
    {
        return $$"""
            {"fileName":"{{fileName}}","contentType":"{{contentType}}","sizeBytes":{{sizeBytes}},
            "uploadedBy":"{{UploaderId}}","uploadedAtUtc":"{{uploadedAtUtc}}"}
            """;
    }

    private static string VesselSearchPage()
    {
        return $$"""
            {"items":[{"vesselId":"{{VesselId}}","name":"Aurora Borealis","imoNumber":"IMO1234567",
            "flagState":"Panama","currentStatus":"Active",
            "vesselTypeId":"60000000-0000-0000-0000-000000000006","vesselTypeName":"Tanker",
            "companyId":"70000000-0000-0000-0000-000000000007","dwt":null,
            "updatedAtUtc":"2026-01-01T00:00:00Z","lifecycleStage":null,"fleetId":null,"fleetName":null}],
            "pageNumber":1,"pageSize":10,"totalCount":1}
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
