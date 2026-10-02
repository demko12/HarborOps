using System.Net;
using System.Security.Claims;
using System.Text;
using Bunit;
using HarborOps.Web.Api;
using HarborOps.Web.Api.Compliance;
using HarborOps.Web.Api.Documents;
using HarborOps.Web.Api.Fleet;
using HarborOps.Web.Api.Voyages;
using HarborOps.Web.Components.Pages.Compliance;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace HarborOps.Web.Tests.Components;

public sealed class CompliancePageTests : BunitContext
{
    private static readonly Guid VesselOne = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid VesselTwo = Guid.Parse("20000000-0000-0000-0000-000000000002");
    private static readonly Guid PortBar = Guid.Parse("40000000-0000-0000-0000-000000000004");
    private static readonly Guid EvidenceDocument = Guid.Parse("50000000-0000-0000-0000-000000000005");
    private readonly List<string> overviewRequests = [];
    private readonly List<string> pscBodies = [];
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
        Services.AddSingleton(new VoyagesApiClient(new HttpClient(new Handler(request => responder(request))) { BaseAddress = new("https://localhost/") }));
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

    [Theory]
    [InlineData("Released", "Released", "neutral")]
    [InlineData("NotDetained", "Clear", "success")]
    [InlineData("Detained", "Detained", "danger")]
    public async Task Psc_card_shows_the_detention_state_from_the_detention_workflow(string state, string label, string tone)
    {
        responder = request => request.RequestUri!.AbsolutePath.Contains("overview")
            ? Json(HttpStatusCode.OK, Overview("CERT-1").Replace("\"vesselDetained\":true", $"\"vesselDetained\":true,\"detentionState\":\"{state}\""))
            : DefaultResponse(request);
        var page = Render<Compliance>();
        await SelectAsync(page, "Aurora");
        page.WaitForAssertion(() =>
        {
            var badge = page.Find(".inspection-card .record-status-block .compliance-badge");
            Assert.Equal(label, badge.TextContent);
            Assert.Contains(tone, badge.ClassList);
            Assert.Equal(state == "Detained", page.Find(".inspection-card").ClassList.Contains("is-detained"));
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
        await SelectPortAsync(page);
        page.Find("#psc-date").Change("2026-08-01");
        page.Find("#psc-authority").Change("Port State"); page.Find("#psc-deficiencies").Change("2");
        page.Find(".compliance-form-card form").Submit();
        page.WaitForAssertion(() => Assert.Contains("PSC inspection registered successfully", page.Markup));
        Assert.Equal(1, pscPosts);
        Assert.Contains($"\"portId\":\"{PortBar}\"", Assert.Single(pscBodies), StringComparison.OrdinalIgnoreCase);
        Assert.True(overviewRequests.Count >= 2);
    }

    [Fact]
    public async Task Psc_form_requires_a_port_picked_from_the_port_directory()
    {
        var page = Render<Compliance>(); await SelectAsync(page, "Aurora");
        page.WaitForAssertion(() => Assert.Contains("CERT-OLD", page.Markup));
        page.FindAll(".compliance-actions button")[1].Click();

        Assert.Equal("text", page.Find("#psc-port").GetAttribute("type"));
        Assert.Contains("port-picker-input", page.Find("#psc-port").ClassName, StringComparison.Ordinal);
        page.Find("#psc-date").Change("2026-08-01");
        page.Find("#psc-authority").Change("Port State"); page.Find("#psc-deficiencies").Change("2");
        page.Find(".compliance-form-card form").Submit();

        page.WaitForAssertion(() => Assert.Contains("The Port field is required.", page.Markup));
        Assert.Equal(0, pscPosts);
    }

    [Fact]
    public async Task Psc_inspections_show_the_port_name_instead_of_its_identifier()
    {
        var page = Render<Compliance>(); await SelectAsync(page, "Aurora");
        page.WaitForAssertion(() => Assert.Contains("Port of Bar", page.Find(".inspection-card").TextContent));
        Assert.DoesNotContain(PortBar.ToString(), page.Markup, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Unresolved_port_and_document_names_fall_back_to_a_readable_label()
    {
        responder = request => request.RequestUri!.AbsolutePath.StartsWith("/api/voyages", StringComparison.Ordinal)
            || request.RequestUri.AbsolutePath.StartsWith("/api/documents/", StringComparison.Ordinal)
                ? Json(HttpStatusCode.Forbidden, "{\"title\":\"Forbidden\"}")
                : DefaultResponse(request);
        var page = Render<Compliance>(); await SelectAsync(page, "Aurora");

        page.WaitForAssertion(() => Assert.Contains("Unknown port", page.Find(".inspection-card").TextContent));
        Assert.Contains("Unknown document", page.Find(".issue-card").TextContent);
        Assert.DoesNotContain(PortBar.ToString(), page.Markup, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(EvidenceDocument.ToString(), page.Markup, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Non_conformities_show_the_evidence_document_and_link_its_file_for_document_readers()
    {
        ConfigureAuthorization(CurrentUserAuthorizationState.ComplianceReadPermission, CurrentUserAuthorizationState.DocumentsReadPermission);
        var page = Render<Compliance>(); await SelectAsync(page, "Aurora");

        page.WaitForAssertion(() => Assert.Contains("Class Certificate — CLS-2026-7", page.Find(".issue-card").TextContent));
        var link = page.Find(".issue-card a.record-reference-name");
        Assert.Equal($"/documents/{EvidenceDocument}/file/download", link.GetAttribute("href"));
        Assert.Equal("_blank", link.GetAttribute("target"));
    }

    [Fact]
    public async Task Evidence_documents_are_not_linked_without_the_documents_read_permission()
    {
        var page = Render<Compliance>(); await SelectAsync(page, "Aurora");

        page.WaitForAssertion(() => Assert.Contains("Class Certificate — CLS-2026-7", page.Find(".issue-card").TextContent));
        Assert.Empty(page.FindAll(".issue-card a"));
    }

    [Fact]
    public async Task Expiring_certificates_and_overdue_findings_are_counted_labelled_and_summarised()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var overviewJson = FullOverview(
            [
                DatedCertificateJson("30000000-0000-0000-0000-000000000021", "SMC-SOON", "Current", today.AddDays(-355), today.AddDays(10), isRenewalCovered: false),
                DatedCertificateJson("30000000-0000-0000-0000-000000000022", "SMC-RENEWED", "Current", today.AddDays(-360), today.AddDays(5), isRenewalCovered: true),
                DatedCertificateJson("30000000-0000-0000-0000-000000000023", "SMC-LATER", "Current", today.AddDays(-65), today.AddDays(300), isRenewalCovered: false),
            ],
            [
                NonConformityJson(1, "Critical", today.AddDays(-3)),
                NonConformityJson(2, "Minor", today.AddDays(10)),
            ]);
        responder = request => request.RequestUri!.AbsolutePath.Contains("overview") ? Json(HttpStatusCode.OK, overviewJson) : DefaultResponse(request);
        var page = Render<Compliance>(); await SelectAsync(page, "Aurora");
        page.WaitForAssertion(() => Assert.Contains("SMC-SOON", page.Markup));

        var expiringKpi = page.FindAll(".compliance-summary article")[1];
        Assert.Contains("Expiring soon", expiringKpi.TextContent);
        Assert.Equal("1", expiringKpi.QuerySelector("strong")!.TextContent);
        Assert.Contains("is-warning", expiringKpi.ClassName);

        var soonRow = page.FindAll(".certificate-row").Single(row => row.TextContent.Contains("SMC-SOON", StringComparison.Ordinal));
        Assert.Equal("Expires in 10 days", soonRow.QuerySelector(".certificate-countdown.warning")!.TextContent);
        Assert.Contains("warning", soonRow.QuerySelector(".certificate-validity-bar")!.ClassName);
        var laterRow = page.FindAll(".certificate-row").Single(row => row.TextContent.Contains("SMC-LATER", StringComparison.Ordinal));
        Assert.Equal("Expires in 300 days", laterRow.QuerySelector(".certificate-countdown.success")!.TextContent);
        Assert.Equal("width: 18%", laterRow.QuerySelector(".certificate-validity-bar > span")!.GetAttribute("style"));

        var issues = page.FindAll(".issue-card");
        Assert.Contains("is-overdue", issues[0].ClassName);
        Assert.Equal("3 days overdue", issues[0].QuerySelector(".compliance-overdue")!.TextContent);
        Assert.Empty(issues[1].QuerySelectorAll(".compliance-overdue"));

        var health = page.Find(".compliance-health");
        Assert.Contains("danger", health.ClassName);
        Assert.Contains("Action required", health.TextContent);
        Assert.Contains("1 certificate expires within 30 days · 1 critical finding overdue", health.TextContent);
    }

    [Fact]
    public async Task A_vessel_without_open_risks_is_reported_as_all_clear()
    {
        responder = request => request.RequestUri!.AbsolutePath.Contains("overview") ? Json(HttpStatusCode.OK, EmptyOverview) : DefaultResponse(request);
        var page = Render<Compliance>(); await SelectAsync(page, "Aurora");

        page.WaitForAssertion(() => Assert.Contains("All clear", page.Find(".compliance-health").TextContent));
        Assert.Contains("success", page.Find(".compliance-health").ClassName);
    }

    [Fact]
    public async Task Selecting_a_vessel_keeps_it_in_the_address()
    {
        var page = Render<Compliance>();
        await SelectAsync(page, "Aurora");

        page.WaitForAssertion(() => Assert.Contains("CERT-OLD", page.Markup));
        Assert.EndsWith($"/compliance?vesselId={VesselOne}", Services.GetRequiredService<NavigationManager>().Uri, StringComparison.Ordinal);
    }

    [Fact]
    public void Returning_with_a_vessel_in_the_address_reopens_its_overview()
    {
        Services.GetRequiredService<NavigationManager>().NavigateTo($"/compliance?vesselId={VesselOne}");

        var page = Render<Compliance>();

        page.WaitForAssertion(() =>
        {
            Assert.Contains("CERT-OLD", page.Markup);
            Assert.Equal("Aurora", page.Find(".vessel-picker-input").GetAttribute("value"));
        });
        Assert.Contains(VesselOne.ToString(), Assert.Single(overviewRequests), StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("2026-01-01", 0)]
    [InlineData("2026-01-06", 50)]
    [InlineData("2026-02-01", 100)]
    public void Validity_progress_is_the_elapsed_share_of_the_validity_period(string today, int expected)
    {
        Assert.Equal(expected, Compliance.ValidityElapsedPercent(new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 11), DateOnly.Parse(today, System.Globalization.CultureInfo.InvariantCulture)));
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
    public async Task Register_certificate_button_is_hidden_without_the_manage_permission()
    {
        ConfigureAuthorization(CurrentUserAuthorizationState.ComplianceReadPermission);
        var page = Render<Compliance>();
        await SelectAsync(page, "Aurora");
        page.WaitForAssertion(() => Assert.Contains("CERT-OLD", page.Markup));
        Assert.DoesNotContain("Register certificate", page.Markup);
    }

    [Fact]
    public async Task Register_psc_inspection_button_is_hidden_without_the_manage_permission()
    {
        ConfigureAuthorization(CurrentUserAuthorizationState.ComplianceReadPermission);
        var page = Render<Compliance>();
        await SelectAsync(page, "Aurora");
        page.WaitForAssertion(() => Assert.Contains("CERT-OLD", page.Markup));
        Assert.DoesNotContain("Register PSC inspection", page.Markup);
    }

    [Fact]
    public async Task Certificate_summary_counts_valid_uncovered_expired_and_revoked_certificates()
    {
        var overviewJson = CertificatesOverview(
            CertificateJson("30000000-0000-0000-0000-000000000011", "SMC-VALID", "Current", isRenewalCovered: false),
            CertificateJson("30000000-0000-0000-0000-000000000012", "SMC-COVERED", "Expired", isRenewalCovered: true),
            CertificateJson("30000000-0000-0000-0000-000000000013", "SMC-LAPSED", "Expired", isRenewalCovered: false),
            CertificateJson("30000000-0000-0000-0000-000000000014", "SMC-REVOKED", "Revoked", isRenewalCovered: false),
            CertificateJson("30000000-0000-0000-0000-000000000015", "SMC-FUTURE", "NotYetEffective", isRenewalCovered: false));
        responder = request => request.RequestUri!.AbsolutePath.Contains("overview") ? Json(HttpStatusCode.OK, overviewJson) : DefaultResponse(request);
        var page = Render<Compliance>();
        await SelectAsync(page, "Aurora");
        page.WaitForAssertion(() => Assert.Contains("SMC-VALID", page.Markup));

        var summary = page.Find(".compliance-summary article");
        Assert.Contains("Valid certificates", summary.TextContent);
        Assert.Equal("1", summary.QuerySelector("strong")!.TextContent);
        Assert.Contains("1 expired · 1 revoked", summary.TextContent);
    }

    [Fact]
    public async Task Expired_certificate_covered_by_its_renewal_is_not_shown_as_a_risk()
    {
        var overviewJson = CertificatesOverview(
            CertificateJson("30000000-0000-0000-0000-000000000012", "SMC-COVERED", "Expired", isRenewalCovered: true),
            CertificateJson("30000000-0000-0000-0000-000000000013", "SMC-LAPSED", "Expired", isRenewalCovered: false),
            CertificateJson("30000000-0000-0000-0000-000000000014", "SMC-REVOKED", "Revoked", isRenewalCovered: false));
        responder = request => request.RequestUri!.AbsolutePath.Contains("overview") ? Json(HttpStatusCode.OK, overviewJson) : DefaultResponse(request);
        var page = Render<Compliance>();
        await SelectAsync(page, "Aurora");
        page.WaitForAssertion(() => Assert.Contains("SMC-COVERED", page.Markup));

        AssertRiskStyling(page.FindAll(".certificate-row"));

        page.Find(".certificate-view-switch button[aria-label='Card view']").Click();

        AssertRiskStyling(page.FindAll(".certificate-tile"));

        static void AssertRiskStyling(IReadOnlyList<AngleSharp.Dom.IElement> records)
        {
            var covered = records.Single(record => record.TextContent.Contains("SMC-COVERED", StringComparison.Ordinal));
            var lapsed = records.Single(record => record.TextContent.Contains("SMC-LAPSED", StringComparison.Ordinal));
            var revoked = records.Single(record => record.TextContent.Contains("SMC-REVOKED", StringComparison.Ordinal));
            Assert.DoesNotContain("is-at-risk", covered.ClassName, StringComparison.Ordinal);
            Assert.Equal("Expired · renewed", covered.QuerySelector(".compliance-badge.neutral")!.TextContent);
            Assert.Contains("is-at-risk", lapsed.ClassName, StringComparison.Ordinal);
            Assert.Equal("Expired", lapsed.QuerySelector(".compliance-badge.danger")!.TextContent);
            Assert.Contains("is-at-risk", revoked.ClassName, StringComparison.Ordinal);
            Assert.Equal("Revoked", revoked.QuerySelector(".compliance-badge.danger")!.TextContent);
        }
    }

    [Fact]
    public async Task Certificates_default_to_a_list_whose_rows_open_the_certificate_and_show_the_renewal_gap()
    {
        var page = Render<Compliance>();
        await SelectAsync(page, "Aurora");
        page.WaitForAssertion(() => Assert.Contains("CERT-OLD", page.Markup));

        Assert.Equal("true", page.Find(".certificate-view-switch button[aria-label='List view']").GetAttribute("aria-pressed"));
        Assert.Empty(page.FindAll(".certificate-tile"));
        var row = page.Find(".certificate-row");
        Assert.Equal("/compliance/certificates/30000000-0000-0000-0000-000000000003", row.QuerySelector("a")!.GetAttribute("href"));
        Assert.Contains("Renewed by", row.TextContent);
        Assert.Contains("CERT-RENEWED", row.TextContent);
        Assert.Contains("Coverage gap: 4 days", row.TextContent);
        Assert.Contains("Expired", row.TextContent);

        row.Click();

        Assert.EndsWith("/compliance/certificates/30000000-0000-0000-0000-000000000003", Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>().Uri, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Card_view_shows_certificate_tiles_that_link_to_details_with_the_renewal_gap()
    {
        var page = Render<Compliance>();
        await SelectAsync(page, "Aurora");
        page.WaitForAssertion(() => Assert.Contains("CERT-OLD", page.Markup));

        page.Find(".certificate-view-switch button[aria-label='Card view']").Click();

        Assert.Equal("true", page.Find(".certificate-view-switch button[aria-label='Card view']").GetAttribute("aria-pressed"));
        Assert.Empty(page.FindAll(".certificate-row"));
        var tile = page.Find(".certificate-tile[href]");
        Assert.Equal("/compliance/certificates/30000000-0000-0000-0000-000000000003", tile.GetAttribute("href"));
        Assert.Contains("Renewed by", tile.TextContent);
        Assert.Contains("CERT-RENEWED", tile.TextContent);
        Assert.Contains("Coverage gap: 4 days", tile.TextContent);
        Assert.Contains("01 Jan 2020", tile.TextContent);
        Assert.Contains("31 Dec 2020", tile.TextContent);
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
        if (path.EndsWith("/psc-inspections")) { pscPosts++; pscBodies.Add(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult()); return Json(HttpStatusCode.Created, "{\"id\":1}"); }
        if (path == "/api/voyages/ports/names") { return Json(HttpStatusCode.OK, $$"""{"ports":[{"portId":"{{PortBar}}","name":"Port of Bar","country":"Montenegro"}]}"""); }
        if (path == "/api/voyages/ports/lookup") { return Json(HttpStatusCode.OK, $$"""{"items":[{"portId":"{{PortBar}}","name":"Port of Bar","country":"Montenegro","unLocode":"MEBAR"}]}"""); }
        if (path == $"/api/documents/{EvidenceDocument}") { return Json(HttpStatusCode.OK, $$"""{"id":"{{EvidenceDocument}}","vesselId":"{{VesselOne}}","documentTypeId":1,"documentTypeName":"Class Certificate","number":"CLS-2026-7","issueDate":"2026-01-01","expiryDate":"2027-01-01","issuingAuthority":"DNV","status":"Active","reviewStatus":"Approved","hasFile":true,"fileName":"cls.pdf","contentType":"application/pdf","sizeBytes":100,"uploadedBy":null,"uploadedAtUtc":null}"""); }
        if (path.EndsWith("/non-conformities")) { nonConformityPosts++; return Json(HttpStatusCode.Created, "{\"id\":1}"); }
        if (path == "/api/documents") { return Json(HttpStatusCode.OK, DocumentsPageJson); }
        return FleetResponse(request);
    }

    private static HttpResponseMessage FleetResponse(HttpRequestMessage request)
    {
        if (request.RequestUri!.AbsolutePath == "/api/fleet/vessels/lookup")
        {
            return Json(HttpStatusCode.OK, $$"""{"vessels":[{"id":"{{VesselOne}}","name":"Aurora"}]}""");
        }

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

    private static async Task SelectPortAsync(IRenderedComponent<Compliance> page)
    {
        page.Find("#psc-port").Focus();
        page.WaitForAssertion(() => Assert.Single(page.FindAll(".port-picker-option")), TimeSpan.FromSeconds(3));
        await page.InvokeAsync(() => page.Find(".port-picker-option").Click());
    }

    private static void FillCertificate(IRenderedComponent<Compliance> page)
    {
        page.Find("#certificate-type").Change("Safety"); page.Find("#certificate-number").Change("CERT-2");
        page.Find("#certificate-authority").Change("Authority"); page.Find("#certificate-issued").Change("2026-01-01");
        page.Find("#certificate-expires").Change("2027-01-01");
    }

    private static string Overview(string number) => $$"""{"certificates":[{"id":"30000000-0000-0000-0000-000000000003","vesselId":"{{VesselOne}}","certificateType":"Safety Management","certificateNumber":"{{number}}","issuingAuthority":"Flag State","issuedOn":"2020-01-01","expiresOn":"2020-12-31","status":"Expired","predecessorCertificateId":null,"revokedAtUtc":null,"successorCertificateId":"30000000-0000-0000-0000-000000000009","successorCertificateNumber":"CERT-RENEWED","successorIssuedOn":"2021-01-05","successorIsRevoked":false,"isRenewalCovered":false,"renewalGapDays":4}],"pscInspections":[{"id":1,"vesselId":"{{VesselOne}}","portId":"40000000-0000-0000-0000-000000000004","inspectionDate":"2026-08-01","inspectingAuthority":"Port Authority","deficiencyCount":2,"vesselDetained":true}],"surveyAudits":[{"id":1,"vesselId":"{{VesselOne}}","date":"2026-07-01","type":"Annual Survey","result":"Pass"}],"openNonConformities":[{"id":1,"vesselId":"{{VesselOne}}","evidenceDocumentId":"50000000-0000-0000-0000-000000000005","severity":"Critical","correctiveActionDeadline":"2026-09-01","closureStatus":"Open"}],"insurancePolicies":[{"id":1,"vesselId":"{{VesselOne}}","insurer":"Harbor Mutual","policyNumber":"HM-2026-001","coverage":"P&I","expiryDate":"2026-08-20","expiryClassification":"Expiring soon"}]}""";
    private static string CertificatesOverview(params string[] certificates) => $$"""{"certificates":[{{string.Join(",", certificates)}}],"pscInspections":[],"surveyAudits":[],"openNonConformities":[],"insurancePolicies":[]}""";
    private static string CertificateJson(string id, string number, string status, bool isRenewalCovered) => $$"""{"id":"{{id}}","vesselId":"{{VesselOne}}","certificateType":"Safety Management","certificateNumber":"{{number}}","issuingAuthority":"Flag State","issuedOn":"2020-01-01","expiresOn":"2020-12-31","status":"{{status}}","predecessorCertificateId":null,"revokedAtUtc":null,"successorCertificateId":null,"successorCertificateNumber":null,"successorIssuedOn":null,"successorIsRevoked":false,"isRenewalCovered":{{(isRenewalCovered ? "true" : "false")}},"renewalGapDays":null}""";
    private static string FullOverview(string[] certificates, string[] nonConformities) => $$"""{"certificates":[{{string.Join(",", certificates)}}],"pscInspections":[],"surveyAudits":[],"openNonConformities":[{{string.Join(",", nonConformities)}}],"insurancePolicies":[]}""";
    private static string DatedCertificateJson(string id, string number, string status, DateOnly issuedOn, DateOnly expiresOn, bool isRenewalCovered) => $$"""{"id":"{{id}}","vesselId":"{{VesselOne}}","certificateType":"Safety Management","certificateNumber":"{{number}}","issuingAuthority":"Flag State","issuedOn":"{{issuedOn:yyyy-MM-dd}}","expiresOn":"{{expiresOn:yyyy-MM-dd}}","status":"{{status}}","predecessorCertificateId":null,"revokedAtUtc":null,"successorCertificateId":null,"successorCertificateNumber":null,"successorIssuedOn":null,"successorIsRevoked":false,"isRenewalCovered":{{(isRenewalCovered ? "true" : "false")}},"renewalGapDays":null}""";
    private static string NonConformityJson(int id, string severity, DateOnly deadline) => $$"""{"id":{{id}},"vesselId":"{{VesselOne}}","evidenceDocumentId":"{{EvidenceDocument}}","severity":"{{severity}}","correctiveActionDeadline":"{{deadline:yyyy-MM-dd}}","closureStatus":"Open"}""";
    private const string EmptyOverview = "{\"certificates\":[],\"pscInspections\":[],\"surveyAudits\":[],\"openNonConformities\":[],\"insurancePolicies\":[]}";
    private const string DocumentsPageJson = """{"items":[{"documentId":"60000000-0000-0000-0000-000000000006","vesselId":"10000000-0000-0000-0000-000000000001","number":"BRZ-2024-0091","documentTypeName":"Bill of Lading","status":"Active","expiryDate":"2027-01-01"}],"pageNumber":1,"pageSize":10,"totalCount":1}""";
    private static HttpResponseMessage Json(HttpStatusCode status, string body) => new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => Task.FromResult(response(request)); }
    private sealed class AsyncHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> response) : HttpMessageHandler { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => response(request); }
}
