using System.Net;
using System.Text;
using Bunit;
using HarborOps.Web.Api;
using HarborOps.Web.Api.Documents;
using HarborOps.Web.Api.Fleet;
using HarborOps.Web.Components.Pages.Documents;
using Microsoft.Extensions.DependencyInjection;

namespace HarborOps.Web.Tests.Components;

public sealed class DocumentsPageTests : BunitContext
{
    private const string VesselId = "40000000-0000-0000-0000-000000000004";

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
            Assert.Contains("is-active", page.Find(".documents-all-button").ClassList);
            Assert.Equal("true", page.Find(".documents-all-button").GetAttribute("aria-pressed"));
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

    [Fact]
    public void A_vessel_with_no_lookup_match_falls_back_to_a_short_id()
    {
        respondToLookup = _ => Json(HttpStatusCode.OK, """{"vessels":[]}""");
        respondToExpiring = _ => Json(HttpStatusCode.OK, Page(Row("10000000-0000-0000-0000-000000000001")));

        var page = RenderDocumentsPage();

        page.WaitForAssertion(() =>
            Assert.Contains("Vessel 4000000", page.Markup, StringComparison.Ordinal));
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
    public async Task Changing_filters_and_clicking_search_sends_them_and_resets_to_page_one()
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

        page.Find(".documents-search-button").Click();

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
    public void Search_with_only_from_sends_the_lower_bound_and_deactivates_all()
    {
        var page = RenderDocumentsPage();
        page.WaitForAssertion(() => Assert.Contains(requestedUris, IsExpiringRequest));

        page.Find("#documents-from").Change("2026-02-01");
        page.Find(".documents-search-button").Click();

        page.WaitForAssertion(() =>
        {
            var lastQuery = requestedUris.Last(IsExpiringRequest).Query;
            Assert.Contains("from=2026-02-01", lastQuery, StringComparison.Ordinal);
            Assert.DoesNotContain("to=", lastQuery, StringComparison.Ordinal);
            Assert.DoesNotContain("is-active", page.Find(".documents-all-button").ClassList);
            Assert.Equal("false", page.Find(".documents-all-button").GetAttribute("aria-pressed"));
        });
    }

    [Fact]
    public void All_clears_date_filters_resets_paging_and_reloads_unfiltered_results()
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
        page.Find(".documents-search-button").Click();
        page.WaitForAssertion(() => Assert.Contains("from=2026-02-01", requestedUris.Last(IsExpiringRequest).Query, StringComparison.Ordinal));

        page.FindAll(".documents-pages button")[1].Click();
        page.WaitForAssertion(() => Assert.Contains("pageNumber=2", requestedUris.Last(IsExpiringRequest).Query, StringComparison.Ordinal));

        page.Find(".documents-all-button").Click();

        page.WaitForAssertion(() =>
        {
            var lastQuery = requestedUris.Last(IsExpiringRequest).Query;
            Assert.DoesNotContain("from=", lastQuery, StringComparison.Ordinal);
            Assert.DoesNotContain("to=", lastQuery, StringComparison.Ordinal);
            Assert.DoesNotContain("vesselId=", lastQuery, StringComparison.Ordinal);
            Assert.Contains("pageNumber=1", lastQuery, StringComparison.Ordinal);
            Assert.Null(page.Find("#documents-from").GetAttribute("value"));
            Assert.Null(page.Find("#documents-to").GetAttribute("value"));
            Assert.Contains("is-active", page.Find(".documents-all-button").ClassList);
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
        page.Find(".documents-search-button").Click();

        Assert.Contains(
            "\"To\" date must be on or after the \"From\" date.",
            page.Find(".documents-validation").TextContent,
            StringComparison.Ordinal);
        Assert.Equal(requestsBeforeSearch, requestedUris.Count(IsExpiringRequest));
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
    public void Submitting_an_empty_type_form_shows_client_side_validation_without_calling_the_api()
    {
        var page = RenderDocumentsPage();
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
    }

    [Fact]
    public async Task Server_validation_error_on_document_type_id_is_shown_next_to_that_field()
    {
        respondToRegisterDocument = _ => Json(
            HttpStatusCode.BadRequest,
            """{"title":"One or more validation errors occurred.","errors":{"documentTypeId":["Document type is required."]}}""");

        var page = RenderDocumentsPage();
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

    // ---- Cross-form refresh ----

    [Fact]
    public void Registering_a_document_type_refreshes_the_document_type_dropdown_options()
    {
        var page = RenderDocumentsPage();
        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll("#doc-type option")));

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

    private IRenderedComponent<Documents> RenderDocumentsPage()
    {
        var documentsHttpClient = new HttpClient(new StubHttpMessageHandler(request =>
        {
            requestedUris.Add(request.RequestUri!);

            return DispatchDocuments(request);
        }))
        {
            BaseAddress = new Uri("https://api.harborops.test/"),
        };

        var lookupHttpClient = new HttpClient(new StubHttpMessageHandler(request =>
        {
            requestedUris.Add(request.RequestUri!);

            return respondToLookup(request);
        }))
        {
            BaseAddress = new Uri("https://api.harborops.test/"),
        };

        var fleetHttpClient = new HttpClient(new StubHttpMessageHandler(request =>
        {
            requestedUris.Add(request.RequestUri!);

            return respondToVesselSearch(request);
        }))
        {
            BaseAddress = new Uri("https://api.harborops.test/"),
        };

        Services.AddSingleton(new DocumentsApiClient(documentsHttpClient));
        Services.AddSingleton(new VesselLookupApiClient(lookupHttpClient));
        Services.AddSingleton(new FleetApiClient(fleetHttpClient));

        var page = Render<Documents>();
        page.WaitForAssertion(() => Assert.NotEmpty(requestedUris));

        return page;
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

        return respondToRegisterDocument(request);
    }

    private static string Row(
        string documentId,
        string number = "CERT-001",
        string status = "Active",
        string documentTypeName = "Safety Certificate",
        string issuingAuthority = "Flag State",
        string issueDate = "2026-01-01",
        string expiryDate = "2026-12-31",
        int daysUntilExpiry = 120)
    {
        return $$"""
            {"documentId":"{{documentId}}","vesselId":"{{VesselId}}","number":"{{number}}",
            "issueDate":"{{issueDate}}","expiryDate":"{{expiryDate}}","issuingAuthority":"{{issuingAuthority}}",
            "status":"{{status}}","documentTypeName":"{{documentTypeName}}","daysUntilExpiry":{{daysUntilExpiry}}}
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
