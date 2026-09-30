using System.Net;
using System.Security.Claims;
using System.Text;
using Bunit;
using HarborOps.Web.Api;
using HarborOps.Web.Api.Compliance;
using HarborOps.Web.Components.Pages.Compliance;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace HarborOps.Web.Tests.Components;

public sealed class ComplianceCertificateDetailsPageTests : BunitContext
{
    private static readonly Guid CertificateId = Guid.Parse("30000000-0000-0000-0000-000000000003");
    private static readonly Guid SuccessorId = Guid.Parse("30000000-0000-0000-0000-000000000009");
    private static readonly Guid FirstToken = Guid.Parse("70000000-0000-0000-0000-000000000001");
    private static readonly Guid SecondToken = Guid.Parse("70000000-0000-0000-0000-000000000002");
    private readonly List<string> posts = [];
    private int detailRequests;
    private Guid currentToken = FirstToken;
    private bool renewed;
    private bool revoked;
    private bool coveredAndExpired;
    private Func<HttpRequestMessage, string, HttpResponseMessage?> postOverride = (_, _) => null;

    public ComplianceCertificateDetailsPageTests()
    {
        Services.AddSingleton(new ComplianceApiClient(new HttpClient(new AsyncHandler(RespondAsync)) { BaseAddress = new("https://localhost/") }));
        ConfigureAuthorization(CurrentUserAuthorizationState.ComplianceManagePermission);
    }

    [Fact]
    public void Details_render_the_chain_dates_gap_and_history()
    {
        renewed = true;
        var page = RenderPage();

        page.WaitForAssertion(() => Assert.Contains("SMC-2022", page.Markup));
        var chain = page.FindAll(".certificate-chain li");
        Assert.Equal(2, chain.Count);
        Assert.Contains("SMC-2022", chain[0].TextContent);
        Assert.Contains("SMC-2027", chain[1].TextContent);
        Assert.Contains("Coverage gap: 3 days", chain[1].TextContent);
        Assert.Contains("Not yet effective", chain[1].TextContent);
        Assert.Contains("01 Jan 2027", chain[0].TextContent);
        Assert.Equal($"/compliance/certificates/{SuccessorId}", chain[1].QuerySelector("a")!.GetAttribute("href"));
        Assert.Contains("Renewed · SMC-2022", page.Find(".certificate-history").TextContent);
        Assert.Contains("Renewed by SMC-2027", page.Find(".certificate-history").TextContent);
    }

    [Fact]
    public void Breadcrumb_returns_to_the_overview_of_the_certificate_vessel()
    {
        var page = RenderPage();

        page.WaitForAssertion(() => Assert.Equal(
            "/compliance?vesselId=10000000-0000-0000-0000-000000000001",
            page.Find(".page-breadcrumb a").GetAttribute("href")));
    }

    [Fact]
    public void Summary_shows_status_validity_chain_position_and_history_count()
    {
        renewed = true;
        var page = RenderPage();

        page.WaitForAssertion(() => Assert.Contains("SMC-2027", page.Markup));
        var kpis = page.FindAll(".certificate-summary .certificate-kpi");
        Assert.Equal(4, kpis.Count);
        Assert.Equal("Valid", kpis[0].QuerySelector(".certificate-kpi-value")!.TextContent);
        Assert.Equal("01 Jan 2027", kpis[1].QuerySelector(".certificate-kpi-value")!.TextContent);
        Assert.Equal("Issued 01 Jan 2022", kpis[1].QuerySelector(".certificate-kpi-note")!.TextContent);
        Assert.Equal("2", kpis[2].QuerySelector(".certificate-kpi-value")!.TextContent);
        Assert.Equal("This is certificate 1 of 2", kpis[2].QuerySelector(".certificate-kpi-note")!.TextContent);
        Assert.Equal("1", kpis[3].QuerySelector(".certificate-kpi-value")!.TextContent);
        Assert.Equal("Last event 28 Sep 2026", kpis[3].QuerySelector(".certificate-kpi-note")!.TextContent);
    }

    [Fact]
    public void Read_only_users_see_no_renew_or_revoke_actions()
    {
        ConfigureAuthorization(CurrentUserAuthorizationState.ComplianceReadPermission);
        var page = RenderPage();

        page.WaitForAssertion(() => Assert.Contains("SMC-2022", page.Markup));
        Assert.Empty(page.FindAll(".certificate-renew-button"));
        Assert.Empty(page.FindAll(".certificate-revoke-button"));
    }

    [Fact]
    public void Renewal_requires_confirmation_then_posts_once_with_the_loaded_token_and_refreshes()
    {
        var page = RenderPage();
        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll(".certificate-renew-button")));

        page.Find(".certificate-renew-button").Click();
        FillRenewal(page);
        page.Find(".certificate-renew-form").Submit();

        page.WaitForAssertion(() => Assert.Contains("The current certificate stays valid until 01 Jan 2027", page.Markup));
        Assert.Empty(posts);

        page.Find(".certificate-confirm-button").Click();

        page.WaitForAssertion(() => Assert.Contains("Renewal SMC-2027 registered.", page.Markup));
        var body = Assert.Single(posts);
        Assert.Contains($"\"expectedConcurrencyToken\":\"{FirstToken}\"", body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"certificateNumber\":\"SMC-2027\"", body, StringComparison.Ordinal);
        Assert.Empty(page.FindAll(".certificate-renew-button"));
        Assert.Equal(2, page.FindAll(".certificate-chain li").Count);
    }

    [Fact]
    public void Renewal_validation_blocks_the_confirmation_until_required_fields_are_present()
    {
        var page = RenderPage();
        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll(".certificate-renew-button")));

        page.Find(".certificate-renew-button").Click();
        page.Find(".certificate-renew-form").Submit();

        page.WaitForAssertion(() => Assert.Contains("Certificate number is required.", page.Markup));
        Assert.Empty(page.FindAll(".certificate-confirm-button"));
        Assert.Empty(posts);
    }

    [Fact]
    public void Server_validation_errors_are_shown_next_to_the_renewal_fields()
    {
        postOverride = (request, _) => request.RequestUri!.AbsolutePath.EndsWith("/renewals", StringComparison.Ordinal)
            ? Json(HttpStatusCode.BadRequest, "{\"title\":\"Validation\",\"errors\":{\"expiresOn\":[\"Expires on date must be later than the renewed certificate's expiry date.\"]}}")
            : null;
        var page = RenderPage();
        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll(".certificate-renew-button")));

        page.Find(".certificate-renew-button").Click();
        FillRenewal(page);
        page.Find(".certificate-renew-form").Submit();
        page.Find(".certificate-confirm-button").Click();

        page.WaitForAssertion(() => Assert.Contains("must be later than the renewed certificate's expiry date", page.Find(".certificate-field-error").TextContent));
        Assert.Equal("SMC-2027", page.Find("#renew-number").GetAttribute("value"));
    }

    [Fact]
    public void A_conflict_offers_reload_and_retry_that_keeps_the_entered_values_and_uses_the_fresh_token()
    {
        var conflictReturned = false;
        postOverride = (request, _) =>
        {
            if (!request.RequestUri!.AbsolutePath.EndsWith("/renewals", StringComparison.Ordinal) || conflictReturned)
            {
                return null;
            }

            conflictReturned = true;
            currentToken = SecondToken;
            return Json(HttpStatusCode.Conflict, "{\"title\":\"The certificate could not be changed.\",\"detail\":\"The certificate was changed by another user. Reload it and try again.\"}");
        };
        var page = RenderPage();
        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll(".certificate-renew-button")));

        page.Find(".certificate-renew-button").Click();
        FillRenewal(page);
        page.Find(".certificate-renew-form").Submit();
        page.Find(".certificate-confirm-button").Click();

        page.WaitForAssertion(() => Assert.Contains("changed by another user", page.Markup));
        var requestsBeforeReload = detailRequests;
        page.Find(".certificate-reload-button").Click();

        page.WaitForAssertion(() => Assert.True(detailRequests > requestsBeforeReload));
        page.WaitForAssertion(() => Assert.Empty(page.FindAll(".certificate-reload-button")));
        Assert.Equal("SMC-2027", page.Find("#renew-number").GetAttribute("value"));

        page.Find(".certificate-renew-form").Submit();
        page.Find(".certificate-confirm-button").Click();

        page.WaitForAssertion(() => Assert.Contains("Renewal SMC-2027 registered.", page.Markup));
        Assert.Equal(2, posts.Count);
        Assert.Contains($"\"expectedConcurrencyToken\":\"{SecondToken}\"", posts[1], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Revocation_requires_a_reason_and_confirmation_then_shows_the_revoked_state()
    {
        var page = RenderPage();
        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll(".certificate-revoke-button")));

        page.Find(".certificate-revoke-button").Click();
        page.Find("#revoke-reason").Input("Wrong");
        page.Find(".certificate-revoke-form").Submit();
        page.WaitForAssertion(() => Assert.Contains("Reason must be at least 10 characters.", page.Markup));
        Assert.Empty(posts);

        page.Find("#revoke-reason").Input("Issued in error by the flag state.");
        page.Find(".certificate-revoke-form").Submit();
        page.WaitForAssertion(() => Assert.Contains("cannot be reinstated", page.Markup));
        page.Find(".certificate-confirm-button").Click();

        page.WaitForAssertion(() => Assert.Contains("Certificate revoked.", page.Markup));
        Assert.Contains("\"reason\":\"Issued in error by the flag state.\"", Assert.Single(posts), StringComparison.Ordinal);
        Assert.Contains("Issued in error by the flag state.", page.Find(".certificate-revoked-message").TextContent);
        Assert.Empty(page.FindAll(".certificate-revoke-button"));
    }

    [Fact]
    public void An_expired_certificate_covered_by_its_renewal_is_shown_as_renewed_rather_than_at_risk()
    {
        renewed = true;
        coveredAndExpired = true;
        var page = RenderPage();

        page.WaitForAssertion(() => Assert.Contains("SMC-2027", page.Markup));
        Assert.Equal("Expired · renewed", page.Find(".certificate-overview-card .compliance-badge").TextContent);
        Assert.Contains("neutral", page.Find(".certificate-overview-card .compliance-badge").ClassName, StringComparison.Ordinal);
        var chain = page.FindAll(".certificate-chain li");
        Assert.Equal("Expired · renewed", chain[0].QuerySelector(".compliance-badge")!.TextContent);
        Assert.Equal("Valid", chain[1].QuerySelector(".compliance-badge")!.TextContent);
        Assert.Contains("Continues the previous certificate without a gap.", chain[1].TextContent);
    }

    [Fact]
    public void History_and_revocation_show_a_short_actor_with_the_full_id_as_title()
    {
        renewed = true;
        revoked = true;
        var page = RenderPage();

        page.WaitForAssertion(() => Assert.Contains("SMC-2027", page.Markup));
        var historyActor = page.Find(".certificate-history .certificate-actor");
        Assert.Equal("80000000…0008", historyActor.TextContent);
        Assert.Equal("80000000-0000-0000-0000-000000000008", historyActor.GetAttribute("title"));
        var revokedActor = page.Find(".certificate-revoked-message .certificate-actor");
        Assert.Equal("80000000…0008", revokedActor.TextContent);
        Assert.Equal("80000000-0000-0000-0000-000000000008", revokedActor.GetAttribute("title"));
    }

    [Fact]
    public void Renewal_form_prefills_the_issuer_and_shows_the_date_limits_of_the_renewed_certificate()
    {
        var page = RenderPage();
        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll(".certificate-renew-button")));

        page.Find(".certificate-renew-button").Click();

        Assert.Equal("Flag State", page.Find("#renew-authority").GetAttribute("value"));
        Assert.Equal("2022-01-01", page.Find("#renew-issued").GetAttribute("min"));
        Assert.Equal("2027-01-02", page.Find("#renew-expires").GetAttribute("min"));
        var hints = page.FindAll(".certificate-renew-form .certificate-field-hint").Select(hint => hint.TextContent).ToList();
        Assert.Contains("On or after 01 Jan 2022", hints);
        Assert.Contains("After 01 Jan 2027", hints);
    }

    [Fact]
    public void Renewal_dates_that_do_not_extend_the_renewed_certificate_are_rejected_before_confirmation()
    {
        var page = RenderPage();
        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll(".certificate-renew-button")));

        page.Find(".certificate-renew-button").Click();
        page.Find("#renew-number").Change("SMC-2027");
        page.Find("#renew-issued").Change("2021-12-31");
        page.Find("#renew-expires").Change("2027-01-01");
        page.Find(".certificate-renew-form").Submit();

        var errors = page.FindAll(".certificate-field-error").Select(error => error.TextContent).ToList();
        Assert.Contains("Issued on date must not be earlier than the renewed certificate's issued on date.", errors);
        Assert.Contains("Expires on date must be later than the renewed certificate's expiry date.", errors);
        Assert.Empty(page.FindAll(".certificate-confirm-button"));
        Assert.Empty(posts);
    }

    [Fact]
    public void Revocation_shows_a_reason_counter_and_confirms_that_an_active_renewal_stays_valid()
    {
        renewed = true;
        var page = RenderPage();
        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll(".certificate-revoke-button")));

        page.Find(".certificate-revoke-button").Click();
        page.Find("#revoke-reason").Input("Issued in error by the flag state.");

        Assert.Equal("34 / 1000", page.Find(".certificate-reason-count").TextContent);
        page.Find(".certificate-revoke-form").Submit();
        page.WaitForAssertion(() => Assert.Contains("Its renewal SMC-2027 is not revoked and stays valid on its own dates.", page.Find(".certificate-confirm").TextContent));
    }

    [Fact]
    public void A_load_failure_can_be_retried()
    {
        var attempts = 0;
        postOverride = (request, _) => request.Method == HttpMethod.Get && ++attempts == 1
            ? Json(HttpStatusCode.InternalServerError, "{\"title\":\"Certificate failed\"}")
            : null;
        var page = RenderPage();

        page.WaitForAssertion(() => Assert.Contains("Certificate failed", page.Markup));
        page.Find(".api-error-state button").Click();

        page.WaitForAssertion(() => Assert.Contains("SMC-2022", page.Markup));
    }

    private IRenderedComponent<ComplianceCertificateDetails> RenderPage() =>
        Render<ComplianceCertificateDetails>(parameters => parameters.Add(component => component.CertificateId, CertificateId));

    private static void FillRenewal(IRenderedComponent<ComplianceCertificateDetails> page)
    {
        page.Find("#renew-number").Change("SMC-2027");
        page.Find("#renew-authority").Change("Flag State");
        page.Find("#renew-issued").Change("2027-01-04");
        page.Find("#renew-expires").Change("2032-01-03");
    }

    private async Task<HttpResponseMessage> RespondAsync(HttpRequestMessage request)
    {
        var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync();
        if (postOverride(request, body) is { } overridden)
        {
            if (request.Method == HttpMethod.Post)
            {
                posts.Add(body);
            }

            return overridden;
        }

        var path = request.RequestUri!.AbsolutePath;
        if (request.Method == HttpMethod.Get)
        {
            detailRequests++;
            return Json(HttpStatusCode.OK, Detail());
        }

        posts.Add(body);
        if (path.EndsWith("/renewals", StringComparison.Ordinal))
        {
            renewed = true;
            currentToken = Guid.NewGuid();
            return Json(HttpStatusCode.Created, $$"""{"id":"{{SuccessorId}}","predecessorCertificateId":"{{CertificateId}}"}""");
        }

        revoked = true;
        currentToken = Guid.NewGuid();
        return Json(HttpStatusCode.OK, $$"""{"id":"{{CertificateId}}","revokedAtUtc":"2026-09-28T09:00:00Z","revokedByUserId":"80000000-0000-0000-0000-000000000008"}""");
    }

    private string Detail()
    {
        var chain = $$"""{"id":"{{CertificateId}}","certificateNumber":"SMC-2022","issuingAuthority":"Flag State","issuedOn":"2022-01-01","expiresOn":"2027-01-01","status":"{{OriginalStatus()}}","revokedAtUtc":{{(revoked ? "\"2026-09-28T09:00:00Z\"" : "null")}},"predecessorCertificateId":null,"gapDaysAfterPredecessor":null,"coversPredecessorExpiry":false}""";
        var history = "[]";
        if (renewed)
        {
            chain += $$""",{"id":"{{SuccessorId}}","certificateNumber":"SMC-2027","issuingAuthority":"Flag State","issuedOn":"{{(coveredAndExpired ? "2027-01-02" : "2027-01-05")}}","expiresOn":"2032-01-04","status":"{{(coveredAndExpired ? "Current" : "NotYetEffective")}}","revokedAtUtc":null,"predecessorCertificateId":"{{CertificateId}}","gapDaysAfterPredecessor":{{(coveredAndExpired ? 0 : 3)}},"coversPredecessorExpiry":{{(coveredAndExpired ? "true" : "false")}}}""";
            history = $$"""[{"id":"90000000-0000-0000-0000-000000000009","certificateId":"{{CertificateId}}","eventType":"Renewed","actorUserId":"80000000-0000-0000-0000-000000000008","occurredAtUtc":"2026-09-28T09:00:00Z","reason":null,"successorCertificateId":"{{SuccessorId}}"}]""";
        }

        return $$"""
            {"id":"{{CertificateId}}","vesselId":"10000000-0000-0000-0000-000000000001","certificateType":"Safety Management Certificate",
             "certificateNumber":"SMC-2022","issuingAuthority":"Flag State","issuedOn":"2022-01-01","expiresOn":"2027-01-01",
             "status":"{{OriginalStatus()}}","createdUtc":"2022-01-02T08:00:00Z","predecessorCertificateId":null,
             "successorCertificateId":{{(renewed ? $"\"{SuccessorId}\"" : "null")}},
             "revokedAtUtc":{{(revoked ? "\"2026-09-28T09:00:00Z\"" : "null")}},
             "revokedByUserId":{{(revoked ? "\"80000000-0000-0000-0000-000000000008\"" : "null")}},
             "revocationReason":{{(revoked ? "\"Issued in error by the flag state.\"" : "null")}},
             "canRenew":{{(!renewed && !revoked ? "true" : "false")}},"canRevoke":{{(!revoked ? "true" : "false")}},
             "concurrencyToken":"{{currentToken}}","chain":[{{chain}}],"history":{{history}}}
            """;
    }

    private string OriginalStatus() => revoked ? "Revoked" : coveredAndExpired ? "Expired" : "Current";

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

    private static HttpResponseMessage Json(HttpStatusCode status, string body) => new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    private sealed class AsyncHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> response) : HttpMessageHandler { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => response(request); }
}
