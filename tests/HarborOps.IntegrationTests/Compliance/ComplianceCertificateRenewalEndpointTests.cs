using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HarborOps.Infrastructure.Persistence;
using HarborOps.IntegrationTests.Authentication;
using HarborOps.Modules.Compliance.Application;
using HarborOps.Modules.Compliance.Contracts;
using HarborOps.Modules.Compliance.Domain;
using HarborOps.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HarborOps.IntegrationTests.Compliance;

public sealed class ComplianceCertificateRenewalEndpointTests : IClassFixture<HarborOpsApiFactory>
{
    private readonly HarborOpsApiFactory _application;

    public ComplianceCertificateRenewalEndpointTests(HarborOpsApiFactory application)
    {
        _application = application;
    }

    [Fact]
    public async Task Get_returns_the_certificate_chain_to_a_read_only_user()
    {
        var original = await SeedAsync();
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Compliance.Read);

        var detail = await client.GetFromJsonAsync<GetComplianceCertificateDetailResponse>(
            $"/api/compliance/certificates/{original.Id}");

        Assert.NotNull(detail);
        Assert.Equal(original.Id, detail.Id);
        Assert.Equal(original.ConcurrencyToken, detail.ConcurrencyToken);
        Assert.True(detail.CanRenew);
        Assert.Equal(original.Id, Assert.Single(detail.Chain).Id);
        Assert.Empty(detail.History);
    }

    [Fact]
    public async Task Get_returns_404_for_an_unknown_certificate()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Compliance.Read);

        using var response = await client.GetAsync($"/api/compliance/certificates/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Renew_creates_a_linked_certificate_attributed_to_the_authenticated_user()
    {
        var original = await SeedAsync();
        var (client, userId) = await CreateManageClientWithIdAsync();
        using var _ = client;

        using var response = await client.PostAsJsonAsync(
            $"/api/compliance/certificates/{original.Id}/renewals", RenewRequest(original.ConcurrencyToken));
        var created = await response.Content.ReadFromJsonAsync<RenewComplianceCertificateResponse>();

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(created);
        Assert.Equal($"/api/compliance/certificates/{created.Id}", response.Headers.Location?.OriginalString);

        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();
        var successor = await dbContext.Set<ComplianceCertificate>().AsNoTracking().SingleAsync(item => item.Id == created.Id);
        Assert.Equal(original.Id, successor.PredecessorCertificateId);
        Assert.Equal(original.VesselId, successor.VesselId);
        Assert.Equal(original.CertificateType, successor.CertificateType);
        var history = await dbContext.Set<ComplianceCertificateHistory>().AsNoTracking().SingleAsync(item => item.CertificateId == original.Id);
        Assert.Equal(userId, history.ActorUserId);
        Assert.Equal(created.Id, history.SuccessorCertificateId);

        var detail = await client.GetFromJsonAsync<GetComplianceCertificateDetailResponse>($"/api/compliance/certificates/{original.Id}");
        Assert.Equal(new[] { original.Id, created.Id }, detail!.Chain.Select(item => item.Id));
        Assert.False(detail.CanRenew);
    }

    [Fact]
    public async Task Renew_returns_409_for_a_stale_token_and_for_a_second_renewal()
    {
        var original = await SeedAsync();
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Compliance.Manage);

        using var stale = await client.PostAsJsonAsync(
            $"/api/compliance/certificates/{original.Id}/renewals", RenewRequest(Guid.NewGuid()));
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);

        using var first = await client.PostAsJsonAsync(
            $"/api/compliance/certificates/{original.Id}/renewals", RenewRequest(original.ConcurrencyToken));
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        using var second = await client.PostAsJsonAsync(
            $"/api/compliance/certificates/{original.Id}/renewals", RenewRequest(original.ConcurrencyToken));
        var problem = await second.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Contains("already been renewed", problem!.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Renew_returns_400_for_dates_that_do_not_extend_the_original()
    {
        var original = await SeedAsync();
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Compliance.Manage);

        using var response = await client.PostAsJsonAsync(
            $"/api/compliance/certificates/{original.Id}/renewals",
            RenewRequest(original.ConcurrencyToken) with { ExpiresOn = original.ExpiresOn });
        var problem = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(RenewComplianceCertificateValidator.ExpiresOnField, problem!.Errors);
    }

    [Fact]
    public async Task Revoke_records_the_authenticated_actor_and_blocks_a_second_revocation()
    {
        var original = await SeedAsync();
        var (client, userId) = await CreateManageClientWithIdAsync();
        using var _ = client;

        using var response = await client.PostAsJsonAsync(
            $"/api/compliance/certificates/{original.Id}/revoke", RevokeRequest(original.ConcurrencyToken));
        var revoked = await response.Content.ReadFromJsonAsync<RevokeComplianceCertificateResponse>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(userId, revoked!.RevokedByUserId);

        var detail = await client.GetFromJsonAsync<GetComplianceCertificateDetailResponse>($"/api/compliance/certificates/{original.Id}");
        Assert.Equal(ComplianceCertificate.RevokedStatus, detail!.Status);
        Assert.Equal("Issued in error by the flag state.", detail.RevocationReason);
        Assert.Equal(userId, Assert.Single(detail.History).ActorUserId);

        using var again = await client.PostAsJsonAsync(
            $"/api/compliance/certificates/{original.Id}/revoke", RevokeRequest(detail.ConcurrencyToken));
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
    }

    [Fact]
    public async Task Revoke_returns_400_for_a_reason_that_is_too_short()
    {
        var original = await SeedAsync();
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Compliance.Manage);

        using var response = await client.PostAsJsonAsync(
            $"/api/compliance/certificates/{original.Id}/revoke",
            new RevokeComplianceCertificateRequest { ExpectedConcurrencyToken = original.ConcurrencyToken, Reason = "Wrong" });
        var problem = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(RevokeComplianceCertificateValidator.ReasonField, problem!.Errors);
    }

    [Fact]
    public async Task Revoked_certificate_is_shown_as_revoked_in_the_vessel_overview()
    {
        var original = await SeedAsync();
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Compliance.Manage);

        using var revoke = await client.PostAsJsonAsync(
            $"/api/compliance/certificates/{original.Id}/revoke", RevokeRequest(original.ConcurrencyToken));
        revoke.EnsureSuccessStatusCode();

        var overview = await client.GetFromJsonAsync<GetVesselComplianceOverviewResponse>(
            $"/api/compliance/vessels/{original.VesselId}/overview");

        Assert.Equal(ComplianceCertificate.RevokedStatus, Assert.Single(overview!.Certificates).Status);
    }

    [Fact]
    public async Task Renew_and_revoke_return_403_for_a_read_only_user()
    {
        var original = await SeedAsync();
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Compliance.Read);

        using var renew = await client.PostAsJsonAsync(
            $"/api/compliance/certificates/{original.Id}/renewals", RenewRequest(original.ConcurrencyToken));
        using var revoke = await client.PostAsJsonAsync(
            $"/api/compliance/certificates/{original.Id}/revoke", RevokeRequest(original.ConcurrencyToken));

        Assert.Equal(HttpStatusCode.Forbidden, renew.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, revoke.StatusCode);
    }

    [Fact]
    public async Task Endpoints_return_401_without_authentication()
    {
        var original = await SeedAsync();
        using var client = _application.CreateClient();

        using var detail = await client.GetAsync($"/api/compliance/certificates/{original.Id}");
        using var renew = await client.PostAsJsonAsync(
            $"/api/compliance/certificates/{original.Id}/renewals", RenewRequest(original.ConcurrencyToken));

        Assert.Equal(HttpStatusCode.Unauthorized, detail.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, renew.StatusCode);
    }

    private static RenewComplianceCertificateRequest RenewRequest(Guid token) => new()
    {
        ExpectedConcurrencyToken = token,
        CertificateNumber = "SMC-2031",
        IssuingAuthority = "Flag State Administration",
        IssuedOn = new DateOnly(2026, 12, 1),
        ExpiresOn = new DateOnly(2031, 12, 1),
    };

    private static RevokeComplianceCertificateRequest RevokeRequest(Guid token) => new()
    {
        ExpectedConcurrencyToken = token,
        Reason = "Issued in error by the flag state.",
    };

    private async Task<(HttpClient Client, Guid UserId)> CreateManageClientWithIdAsync()
    {
        var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Compliance.Manage);
        using var meResponse = await client.GetAsync("/api/identity/me");
        meResponse.EnsureSuccessStatusCode();
        var account = await meResponse.Content.ReadFromJsonAsync<JsonElement>();
        return (client, account.GetProperty("id").GetGuid());
    }

    private async Task<ComplianceCertificate> SeedAsync()
    {
        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();
        var certificate = ComplianceCertificate.Register(
            Guid.NewGuid(), "Safety Management Certificate", "SMC-2022", "Flag State", new DateOnly(2022, 1, 1), new DateOnly(2027, 1, 1));

        await dbContext.AddAsync(certificate);
        await dbContext.SaveChangesAsync();

        return certificate;
    }
}
