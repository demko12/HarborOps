using System.Net;
using System.Security.Claims;
using System.Text;
using Bunit;
using HarborOps.Web.Api.Compliance;
using HarborOps.Web.Api.Documents;
using HarborOps.Web.Api.Fleet;
using HarborOps.Web.Components.Pages.Compliance;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace HarborOps.Web.Tests.Components;

public sealed class CompliancePageTests : BunitContext
{
    private static readonly Guid VesselOne = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid VesselTwo = Guid.Parse("20000000-0000-0000-0000-000000000002");
    private readonly List<string> overviewRequests = [];
    private int certificatePosts;
    private int pscPosts;
    private int nonConformityPosts;
    private Func<HttpRequestMessage, HttpResponseMessage> responder;

    public CompliancePageTests()
    {
        responder = DefaultResponse;
        Services.AddSingleton(new ComplianceApiClient(new HttpClient(new Handler(request => responder(request))) { BaseAddress = new("https://localhost/") }));
        Services.AddSingleton(new FleetApiClient(new HttpClient(new Handler(FleetResponse)) { BaseAddress = new("https://localhost/") }));
        Services.AddSingleton(new DocumentsApiClient(new HttpClient(new Handler(request => responder(request))) { BaseAddress = new("https://localhost/") }));
        ConfigureAuthorization(CurrentUserAuthorizationState.ComplianceManagePermission);
    }

    [Fact]
    public void Initial_state_does_not_request_an_overview()
    {
        var page = Render<Compliance>();
        Assert.Contains("Select a vessel", page.Markup);
        Assert.Empty(overviewRequests);
    }

    [Fact]
    public async Task Selection_renders_records_and_operational_indicators()
    {
        var page = Render<Compliance>();
        await SelectAsync(page, "Aurora");
        page.WaitForAssertion(() =>
        {
            Assert.Contains("Safety Management", page.Markup);
            Assert.Contains("Expired", page.Markup);
            Assert.Contains("Port Authority", page.Markup);
            Assert.Contains("Detained", page.Markup);
            Assert.Contains("Critical severity", page.Markup);
            Assert.Contains("Critical", page.Markup);
        });
    }

    [Fact]
    public async Task Empty_collections_render_three_distinct_empty_states()
    {
        responder = request => request.RequestUri!.AbsolutePath.Contains("overview") ? Json(HttpStatusCode.OK, EmptyOverview) : DefaultResponse(request);
        var page = Render<Compliance>();
        await SelectAsync(page, "Aurora");
        page.WaitForAssertion(() =>
        {
            Assert.Contains("No certificates recorded", page.Markup);
            Assert.Contains("No PSC inspections recorded", page.Markup);
            Assert.Contains("No open non-conformities", page.Markup);
        });
    }

    [Fact]
    public async Task Overview_error_can_retry_without_reselecting_the_vessel()
    {
        var attempts = 0;
        responder = request => request.RequestUri!.AbsolutePath.Contains("overview") && ++attempts == 1
            ? Json(HttpStatusCode.InternalServerError, "{\"title\":\"Overview failed\"}") : DefaultResponse(request);
        var page = Render<Compliance>();
        await SelectAsync(page, "Aurora");
        page.WaitForAssertion(() => Assert.Contains("Overview failed", page.Markup));
        page.Find(".api-error-state button").Click();
        page.WaitForAssertion(() => Assert.Contains("Safety Management", page.Markup));
        Assert.Equal(2, attempts);
    }

    [Fact]
    public async Task Vessel_switching_clears_previous_records_and_loads_the_new_vessel()
    {
        var page = Render<Compliance>();
        await SelectAsync(page, "Aurora");
        page.WaitForAssertion(() => Assert.Contains("CERT-OLD", page.Markup));
        page.Find(".vessel-picker-input").Input("Borealis");
        page.WaitForAssertion(() => Assert.Single(page.FindAll(".vessel-picker-option")), TimeSpan.FromSeconds(3));
        await page.InvokeAsync(() => page.Find(".vessel-picker-option").Click());
        page.WaitForAssertion(() =>
        {
            Assert.Contains("CERT-NEW", page.Markup);
            Assert.DoesNotContain("CERT-OLD", page.Markup);
        });
    }

    [Fact]
    public async Task Certificate_creation_maps_errors_then_refreshes_and_preserves_selection()
    {
        var fail = true;
        responder = request => request.RequestUri!.AbsolutePath.EndsWith("/certificates") && fail
            ? Json(HttpStatusCode.BadRequest, "{\"title\":\"Validation\",\"errors\":{\"certificateNumber\":[\"Number already exists.\"],\"vesselId\":[\"Selected vessel is no longer valid.\"]}}")
            : DefaultResponse(request);
        var page = Render<Compliance>(); await SelectAsync(page, "Aurora");
        page.WaitForAssertion(() => Assert.Contains("CERT-OLD", page.Markup));
        page.FindAll(".compliance-actions button")[0].Click();
        FillCertificate(page); page.Find(".compliance-form-card form").Submit();
        page.WaitForAssertion(() =>
        {
            Assert.Contains("Number already exists.", page.Markup);
            Assert.Contains("Selected vessel is no longer valid.", page.Markup);
        });
        fail = false; page.Find(".compliance-form-card form").Submit();
        page.WaitForAssertion(() => Assert.Contains("Certificate registered successfully", page.Markup));
        Assert.True(overviewRequests.Count >= 2);
        Assert.Contains("Aurora", page.Find(".vessel-picker-input").GetAttribute("value"));
    }

    [Fact]
    public async Task Psc_creation_posts_once_and_refreshes_the_selected_vessel()
    {
        var page = Render<Compliance>(); await SelectAsync(page, "Aurora");
        page.WaitForAssertion(() => Assert.Contains("CERT-OLD", page.Markup));
        page.FindAll(".compliance-actions button")[1].Click();
        page.Find("#psc-port").Change(Guid.NewGuid().ToString()); page.Find("#psc-date").Change("2026-08-01");
        page.Find("#psc-authority").Change("Port State"); page.Find("#psc-deficiencies").Change("2");
        page.Find(".compliance-form-card form").Submit();
        page.WaitForAssertion(() => Assert.Contains("PSC inspection registered successfully", page.Markup));
        Assert.Equal(1, pscPosts);
        Assert.True(overviewRequests.Count >= 2);
    }

    [Fact]
    public async Task Insurance_policy_registration_posts_the_selected_vessel_and_refreshes_the_overview()
    {
        var posts = new List<string>();
        Services.RemoveAll<ComplianceApiClient>();
        Services.AddSingleton(new ComplianceApiClient(new HttpClient(new AsyncHandler(async request =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/insurance-policies", StringComparison.Ordinal))
            {
                posts.Add(await request.Content!.ReadAsStringAsync());
                return Json(HttpStatusCode.Created, "{\"id\":1}");
            }

            return DefaultResponse(request);
        }))
        { BaseAddress = new("https://localhost/") }));

        var page = Render<Compliance>();
        await SelectAsync(page, "Aurora");
        page.WaitForAssertion(() => Assert.Contains("Harbor Mutual", page.Markup));

        page.FindAll(".compliance-actions button")[3].Click();
        page.Find("#insurance-insurer").Change("Ocean Mutual");
        page.Find("#insurance-policy-number").Change("OM-2026-001");
        page.Find("#insurance-coverage").Change("Hull");
        page.Find("#insurance-expiry").Change("2027-01-01");
        page.Find(".compliance-form-card form").Submit();

        page.WaitForAssertion(() => Assert.Contains("Insurance policy registered successfully", page.Markup));
        Assert.Contains($"\"vesselId\":\"{VesselOne}\"", Assert.Single(posts), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Certificate_duplicate_submit_is_blocked_while_the_first_request_is_in_flight()
    {
        var release = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var postCount = 0;
        Services.RemoveAll<ComplianceApiClient>();
        Services.AddSingleton(new ComplianceApiClient(new HttpClient(new AsyncHandler(async request =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/certificates", StringComparison.Ordinal))
            {
                postCount++;
                return await release.Task;
            }

            return DefaultResponse(request);
        }))
        { BaseAddress = new("https://localhost/") }));

        var page = Render<Compliance>();
        await SelectAsync(page, "Aurora");
        page.WaitForAssertion(() => Assert.Contains("CERT-OLD", page.Markup));
        page.FindAll(".compliance-actions button")[0].Click();
        FillCertificate(page);
        var form = page.Find(".compliance-form-card form");
        form.Submit();
        page.WaitForAssertion(() => Assert.True(page.Find(".compliance-submit").HasAttribute("disabled")));
        form.Submit();

        Assert.Equal(1, postCount);
        release.SetResult(Json(HttpStatusCode.Created, "{\"id\":\"30000000-0000-0000-0000-000000000003\"}"));
        page.WaitForAssertion(() => Assert.Contains("Certificate registered successfully", page.Markup));
    }

    [Fact]
    public async Task Non_conformity_button_is_hidden_without_the_manage_permission()
    {
        ConfigureAuthorization(CurrentUserAuthorizationState.ComplianceReadPermission);
        var page = Render<Compliance>();
        await SelectAsync(page, "Aurora");
        page.WaitForAssertion(() => Assert.Contains("CERT-OLD", page.Markup));
        Assert.DoesNotContain("Register non-conformity", page.Markup);
    }

    [Fact]
    public async Task Non_conformity_button_opens_a_form_defaulting_to_the_survey_source()
    {
        var page = Render<Compliance>();
        await SelectAsync(page, "Aurora");
        page.WaitForAssertion(() => Assert.Contains("CERT-OLD", page.Markup));
        Assert.Contains("Register non-conformity", page.Markup);

        page.FindAll(".compliance-actions button")[2].Click();

        Assert.NotEmpty(page.FindAll("#nc-survey"));
        Assert.Empty(page.FindAll("#nc-psc"));
    }

    [Fact]
    public async Task Registering_a_non_conformity_from_a_survey_source_posts_the_survey_id_and_refreshes()
    {
        var posts = new List<string>();
        Services.RemoveAll<ComplianceApiClient>();
        Services.AddSingleton(new ComplianceApiClient(new HttpClient(new AsyncHandler(async request =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/non-conformities", StringComparison.Ordinal))
            {
                posts.Add(await request.Content!.ReadAsStringAsync());
                return Json(HttpStatusCode.Created, "{\"id\":1}");
            }

            return DefaultResponse(request);
        }))
        { BaseAddress = new("https://localhost/") }));

        var page = Render<Compliance>();
        await SelectAsync(page, "Aurora");
        page.WaitForAssertion(() => Assert.Contains("CERT-OLD", page.Markup));
        var overviewCountBeforeSubmit = overviewRequests.Count;

        page.FindAll(".compliance-actions button")[2].Click();
        page.Find("#nc-survey").Change("1");
        await SelectEvidenceDocumentAsync(page, "BRZ");
        page.Find("#nc-severity").Change("Major");
        page.Find("#nc-deadline").Change("2026-12-01");
        page.Find(".compliance-form-card form").Submit();

        page.WaitForAssertion(() => Assert.Contains("Non-conformity registered successfully", page.Markup));
        var body = Assert.Single(posts);
        Assert.Contains("\"surveyId\":1", body, StringComparison.Ordinal);
        Assert.Contains("\"pscId\":null", body, StringComparison.Ordinal);
        Assert.True(overviewRequests.Count > overviewCountBeforeSubmit);
    }

    [Fact]
    public async Task Registering_a_non_conformity_from_a_psc_source_posts_the_psc_id_and_refreshes()
    {
        var posts = new List<string>();
        Services.RemoveAll<ComplianceApiClient>();
        Services.AddSingleton(new ComplianceApiClient(new HttpClient(new AsyncHandler(async request =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/non-conformities", StringComparison.Ordinal))
            {
                posts.Add(await request.Content!.ReadAsStringAsync());
                return Json(HttpStatusCode.Created, "{\"id\":1}");
            }

            return DefaultResponse(request);
        }))
        { BaseAddress = new("https://localhost/") }));

        var page = Render<Compliance>();
        await SelectAsync(page, "Aurora");
        page.WaitForAssertion(() => Assert.Contains("CERT-OLD", page.Markup));

        page.FindAll(".compliance-actions button")[2].Click();
        page.Find("input[value=\"Psc\"]").Change("Psc");
        Assert.NotEmpty(page.FindAll("#nc-psc"));

        page.Find("#nc-psc").Change("1");
        await SelectEvidenceDocumentAsync(page, "BRZ");
        page.Find("#nc-severity").Change("Minor");
        page.Find("#nc-deadline").Change("2026-12-01");
        page.Find(".compliance-form-card form").Submit();

        page.WaitForAssertion(() => Assert.Contains("Non-conformity registered successfully", page.Markup));
        var body = Assert.Single(posts);
        Assert.Contains("\"pscId\":1", body, StringComparison.Ordinal);
        Assert.Contains("\"surveyId\":null", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Submitting_without_severity_or_deadline_shows_validation_and_blocks_the_post()
    {
        var page = Render<Compliance>();
        await SelectAsync(page, "Aurora");
        page.WaitForAssertion(() => Assert.Contains("CERT-OLD", page.Markup));

        page.FindAll(".compliance-actions button")[2].Click();
        page.Find("#nc-survey").Change("1");
        await SelectEvidenceDocumentAsync(page, "BRZ");
        page.Find(".compliance-form-card form").Submit();

        page.WaitForAssertion(() =>
        {
            Assert.Contains("The Severity field is required.", page.Markup);
            Assert.Contains("The CorrectiveActionDeadline field is required.", page.Markup);
        });
        Assert.Equal(0, nonConformityPosts);
    }

    [Fact]
    public async Task A_missing_survey_audit_shows_the_failure_title_and_preserves_entered_values()
    {
        var postCount = 0;
        Services.RemoveAll<ComplianceApiClient>();
        Services.AddSingleton(new ComplianceApiClient(new HttpClient(new AsyncHandler(async request =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/non-conformities", StringComparison.Ordinal))
            {
                postCount++;
                return Json(
                    HttpStatusCode.NotFound,
                    "{\"title\":\"The requested survey audit was not found.\",\"errors\":{\"surveyId\":[\"Survey audit '1' was not found.\"]}}");
            }

            return DefaultResponse(request);
        }))
        { BaseAddress = new("https://localhost/") }));

        var page = Render<Compliance>();
        await SelectAsync(page, "Aurora");
        page.WaitForAssertion(() => Assert.Contains("CERT-OLD", page.Markup));

        page.FindAll(".compliance-actions button")[2].Click();
        page.Find("#nc-survey").Change("1");
        await SelectEvidenceDocumentAsync(page, "BRZ");
        page.Find("#nc-severity").Change("Critical");
        page.Find("#nc-deadline").Change("2026-12-15");
        page.Find(".compliance-form-card form").Submit();

        page.WaitForAssertion(() => Assert.Contains("The requested survey audit was not found.", page.Markup));
        Assert.Equal("2026-12-15", page.Find("#nc-deadline").GetAttribute("value"));
        Assert.Equal("Critical", page.Find("#nc-severity").GetAttribute("value"));
        Assert.Equal(1, postCount);
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

    private HttpResponseMessage DefaultResponse(HttpRequestMessage request)
    {
        var path = request.RequestUri!.AbsolutePath;
        if (path.Contains("/overview")) { overviewRequests.Add(path); return Json(HttpStatusCode.OK, path.Contains(VesselTwo.ToString()) ? Overview("CERT-NEW") : Overview("CERT-OLD")); }
        if (path.EndsWith("/certificates")) { certificatePosts++; return Json(HttpStatusCode.Created, "{\"id\":\"30000000-0000-0000-0000-000000000003\"}"); }
        if (path.EndsWith("/psc-inspections")) { pscPosts++; return Json(HttpStatusCode.Created, "{\"id\":1}"); }
        if (path.EndsWith("/non-conformities")) { nonConformityPosts++; return Json(HttpStatusCode.Created, "{\"id\":1}"); }
        if (path == "/api/documents") { return Json(HttpStatusCode.OK, DocumentsPageJson); }
        return FleetResponse(request);
    }

    private static HttpResponseMessage FleetResponse(HttpRequestMessage request)
    {
        var second = request.RequestUri!.Query.Contains("Borealis", StringComparison.OrdinalIgnoreCase);
        return Json(HttpStatusCode.OK, $$"""{"items":[{"vesselId":"{{(second ? VesselTwo : VesselOne)}}","name":"{{(second ? "Borealis" : "Aurora")}}","imoNumber":"IMO123","flagState":"ME","status":"Active"}],"pageNumber":1,"pageSize":10,"totalCount":1}""");
    }

    private async Task SelectAsync(IRenderedComponent<Compliance> page, string text)
    {
        page.Find(".vessel-picker-input").Input(text);
        page.WaitForAssertion(() => Assert.Single(page.FindAll(".vessel-picker-option")), TimeSpan.FromSeconds(3));
        await page.InvokeAsync(() => page.Find(".vessel-picker-option").Click());
    }

    private static async Task SelectEvidenceDocumentAsync(IRenderedComponent<Compliance> page, string search)
    {
        page.Find(".document-picker-input").Input(search);
        page.WaitForAssertion(() => Assert.Single(page.FindAll(".document-picker-option")), TimeSpan.FromSeconds(3));
        await page.InvokeAsync(() => page.Find(".document-picker-option").Click());
    }

    private static void FillCertificate(IRenderedComponent<Compliance> page)
    {
        page.Find("#certificate-type").Change("Safety"); page.Find("#certificate-number").Change("CERT-2");
        page.Find("#certificate-authority").Change("Authority"); page.Find("#certificate-issued").Change("2026-01-01");
        page.Find("#certificate-expires").Change("2027-01-01");
    }

    private static string Overview(string number) => $$"""{"certificates":[{"id":"30000000-0000-0000-0000-000000000003","vesselId":"{{VesselOne}}","certificateType":"Safety Management","certificateNumber":"{{number}}","issuingAuthority":"Flag State","issuedOn":"2020-01-01","expiresOn":"2020-12-31"}],"pscInspections":[{"id":1,"vesselId":"{{VesselOne}}","portId":"40000000-0000-0000-0000-000000000004","inspectionDate":"2026-08-01","inspectingAuthority":"Port Authority","deficiencyCount":2,"vesselDetained":true}],"surveyAudits":[{"id":1,"vesselId":"{{VesselOne}}","date":"2026-07-01","type":"Annual Survey","result":"Pass"}],"openNonConformities":[{"id":1,"vesselId":"{{VesselOne}}","evidenceDocumentId":"50000000-0000-0000-0000-000000000005","severity":"Critical","correctiveActionDeadline":"2026-09-01","closureStatus":"Open"}],"insurancePolicies":[{"id":1,"vesselId":"{{VesselOne}}","insurer":"Harbor Mutual","policyNumber":"HM-2026-001","coverage":"P&I","expiryDate":"2026-08-20","expiryClassification":"Expiring soon"}]}""";
    private const string EmptyOverview = "{\"certificates\":[],\"pscInspections\":[],\"surveyAudits\":[],\"openNonConformities\":[],\"insurancePolicies\":[]}";
    private const string DocumentsPageJson = """{"items":[{"documentId":"60000000-0000-0000-0000-000000000006","vesselId":"10000000-0000-0000-0000-000000000001","number":"BRZ-2024-0091","documentTypeName":"Bill of Lading","status":"Active","expiryDate":"2027-01-01"}],"pageNumber":1,"pageSize":10,"totalCount":1}""";
    private static HttpResponseMessage Json(HttpStatusCode status, string body) => new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => Task.FromResult(response(request)); }
    private sealed class AsyncHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> response) : HttpMessageHandler { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => response(request); }
}
