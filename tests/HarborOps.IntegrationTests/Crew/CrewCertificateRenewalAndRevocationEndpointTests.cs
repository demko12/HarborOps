using System.Net;
using System.Net.Http.Json;
using HarborOps.Infrastructure.Persistence;
using HarborOps.IntegrationTests.Authentication;
using HarborOps.Modules.Crew.Application;
using HarborOps.Modules.Crew.Contracts;
using HarborOps.Modules.Crew.Domain;
using HarborOps.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HarborOps.IntegrationTests.Crew;

public sealed class CrewCertificateRenewalAndRevocationEndpointTests : IntegrationTestBase
{
    private readonly HarborOpsApiFactory _application;

    public CrewCertificateRenewalAndRevocationEndpointTests(HarborOpsApiFactory application)
        : base(application)
    {
        _application = application;
    }

    [Fact]
    public async Task Renew_creates_a_linked_successor_and_returns_201()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Crew.Manage);
        var certificate = await SeedCertificateAsync("IMO920001", "STCW-A1");
        var request = RenewalRequest("STCW-A2");

        using var response = await client.PostAsJsonAsync(
            $"/api/crew/certificates/{certificate.Id}/renew", request, CancellationToken.None);
        var result = await response.Content.ReadFromJsonAsync<RenewCrewCertificateResponse>(CancellationToken.None);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(result);
        Assert.Equal(certificate.Id, result.PredecessorCertificateId);
        Assert.Equal(
            $"/api/crew/members/{certificate.CrewMemberId}/certificates/{result.Id}",
            response.Headers.Location?.OriginalString);

        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();
        var original = await dbContext.Set<CrewCertificate>().SingleAsync(c => c.Id == certificate.Id);
        Assert.Null(original.PredecessorCertificateId);
        Assert.Equal("STCW-A1", original.LicenseNumber);
    }

    [Fact]
    public async Task Renew_returns_validation_problem_for_invalid_input()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Crew.Manage);
        var certificate = await SeedCertificateAsync("IMO920002", "STCW-B1");
        var request = new RenewCrewCertificateRequest
        {
            Name = null,
            IssuingAuthority = string.Empty,
            LicenseNumber = " ",
            IssueDate = null,
            ExpiryDate = null,
        };

        using var response = await client.PostAsJsonAsync(
            $"/api/crew/certificates/{certificate.Id}/renew", request, CancellationToken.None);
        var problem = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>(CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotNull(problem);
        Assert.Contains(RenewCrewCertificateValidator.NameField, problem.Errors);
    }

    [Fact]
    public async Task Renew_returns_not_found_for_unknown_certificate()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Crew.Manage);

        using var response = await client.PostAsJsonAsync(
            $"/api/crew/certificates/{Guid.NewGuid()}/renew", RenewalRequest("STCW-C1"), CancellationToken.None);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(CancellationToken.None);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.NotNull(problem);
        Assert.Equal(StatusCodes.Status404NotFound, problem.Status);
    }

    [Fact]
    public async Task Renew_returns_conflict_for_a_second_renewal_of_the_same_certificate()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Crew.Manage);
        var certificate = await SeedCertificateAsync("IMO920003", "STCW-D1");

        using var firstResponse = await client.PostAsJsonAsync(
            $"/api/crew/certificates/{certificate.Id}/renew", RenewalRequest("STCW-D2"), CancellationToken.None);
        Assert.Equal(HttpStatusCode.Created, firstResponse.StatusCode);

        using var response = await client.PostAsJsonAsync(
            $"/api/crew/certificates/{certificate.Id}/renew", RenewalRequest("STCW-D3"), CancellationToken.None);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(CancellationToken.None);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.NotNull(problem);
        Assert.Equal(StatusCodes.Status409Conflict, problem.Status);
    }

    [Fact]
    public async Task Renew_with_only_crew_read_permission_returns_403()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Crew.Read);
        var certificate = await SeedCertificateAsync("IMO920004", "STCW-E1");

        using var response = await client.PostAsJsonAsync(
            $"/api/crew/certificates/{certificate.Id}/renew", RenewalRequest("STCW-E2"), CancellationToken.None);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Revoke_marks_the_certificate_terminal_and_records_history()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Crew.Manage);
        var certificate = await SeedCertificateAsync("IMO920005", "STCW-F1");
        var request = new RevokeCrewCertificateRequest { Reason = "Issued in error" };

        using var response = await client.PostAsJsonAsync(
            $"/api/crew/certificates/{certificate.Id}/revoke", request, CancellationToken.None);
        var result = await response.Content.ReadFromJsonAsync<RevokeCrewCertificateResponse>(CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(result);
        Assert.Equal(certificate.Id, result.Id);
        Assert.Empty(result.AffectedActiveAssignments);

        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();
        var revoked = await dbContext.Set<CrewCertificate>().SingleAsync(c => c.Id == certificate.Id);
        Assert.True(revoked.IsRevoked);
        Assert.Equal("Issued in error", revoked.RevocationReason);

        var history = await dbContext.Set<CrewCertificateHistory>().SingleAsync(h => h.CertificateId == certificate.Id);
        Assert.Equal(CrewCertificateHistory.RevokedEvent, history.EventType);
    }

    [Fact]
    public async Task Revoke_returns_validation_problem_for_a_missing_reason()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Crew.Manage);
        var certificate = await SeedCertificateAsync("IMO920006", "STCW-G1");
        var request = new RevokeCrewCertificateRequest { Reason = " " };

        using var response = await client.PostAsJsonAsync(
            $"/api/crew/certificates/{certificate.Id}/revoke", request, CancellationToken.None);
        var problem = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>(CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotNull(problem);
        Assert.Contains(RevokeCrewCertificateValidator.ReasonField, problem.Errors);
    }

    [Fact]
    public async Task Revoke_returns_not_found_for_unknown_certificate()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Crew.Manage);
        var request = new RevokeCrewCertificateRequest { Reason = "Issued in error" };

        using var response = await client.PostAsJsonAsync(
            $"/api/crew/certificates/{Guid.NewGuid()}/revoke", request, CancellationToken.None);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(CancellationToken.None);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.NotNull(problem);
        Assert.Equal(StatusCodes.Status404NotFound, problem.Status);
    }

    [Fact]
    public async Task Revoke_returns_conflict_for_double_revocation()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Crew.Manage);
        var certificate = await SeedCertificateAsync("IMO920007", "STCW-H1");
        var request = new RevokeCrewCertificateRequest { Reason = "Issued in error" };

        using var firstResponse = await client.PostAsJsonAsync(
            $"/api/crew/certificates/{certificate.Id}/revoke", request, CancellationToken.None);
        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);

        using var response = await client.PostAsJsonAsync(
            $"/api/crew/certificates/{certificate.Id}/revoke", request, CancellationToken.None);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(CancellationToken.None);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.NotNull(problem);
        Assert.Equal(StatusCodes.Status409Conflict, problem.Status);
    }

    [Fact]
    public async Task Revoke_with_only_crew_read_permission_returns_403()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Crew.Read);
        var certificate = await SeedCertificateAsync("IMO920008", "STCW-I1");
        var request = new RevokeCrewCertificateRequest { Reason = "Issued in error" };

        using var response = await client.PostAsJsonAsync(
            $"/api/crew/certificates/{certificate.Id}/revoke", request, CancellationToken.None);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetHistory_returns_the_chain_and_history_with_crew_read_permission()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Crew.Manage);
        var certificate = await SeedCertificateAsync("IMO920009", "STCW-J1");
        using var renewResponse = await client.PostAsJsonAsync(
            $"/api/crew/certificates/{certificate.Id}/renew", RenewalRequest("STCW-J2"), CancellationToken.None);
        var renewal = await renewResponse.Content.ReadFromJsonAsync<RenewCrewCertificateResponse>(CancellationToken.None);

        using var readOnlyClient = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Crew.Read);
        using var response = await readOnlyClient.GetAsync(
            $"/api/crew/certificates/{certificate.Id}/history", CancellationToken.None);
        var result = await response.Content.ReadFromJsonAsync<GetCrewCertificateHistoryResponse>(CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(result);
        Assert.Equal([certificate.Id, renewal!.Id], result.ChainCertificateIds);
        Assert.Single(result.History);
    }

    [Fact]
    public async Task GetHistory_returns_not_found_for_unknown_certificate()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Crew.Read);

        using var response = await client.GetAsync(
            $"/api/crew/certificates/{Guid.NewGuid()}/history", CancellationToken.None);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(CancellationToken.None);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.NotNull(problem);
        Assert.Equal(StatusCodes.Status404NotFound, problem.Status);
    }

    [Fact]
    public async Task Renew_without_authentication_returns_401()
    {
        using var client = _application.CreateClient();

        using var response = await client.PostAsJsonAsync(
            $"/api/crew/certificates/{Guid.NewGuid()}/renew", RenewalRequest("STCW-K1"), CancellationToken.None);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private static RenewCrewCertificateRequest RenewalRequest(string licenseNumber)
    {
        return new RenewCrewCertificateRequest
        {
            Name = "Basic Safety Training Certificate",
            IssuingAuthority = "Flag State Maritime Authority",
            LicenseNumber = licenseNumber,
            IssueDate = DateOnly.FromDateTime(HarborOpsApiFactory.FixedUtcNow),
            ExpiryDate = DateOnly.FromDateTime(HarborOpsApiFactory.FixedUtcNow).AddYears(2),
        };
    }

    private async Task<CrewCertificate> SeedCertificateAsync(string imoSeafarerId, string licenseNumber)
    {
        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();
        var crewPosition = CrewPosition.Create("Bosun", "Deck", "Senior");
        await dbContext.Set<CrewPosition>().AddAsync(crewPosition);

        var crewMember = CrewMember.Create(
            crewPosition.Id,
            "Ivan",
            "Petrović",
            new DateOnly(1990, 5, 14),
            "Montenegro",
            imoSeafarerId,
            "P1234567",
            new DateOnly(2028, 1, 1),
            "CDC987654",
            new DateOnly(2024, 1, 15),
            "Active");
        await dbContext.Set<CrewMember>().AddAsync(crewMember);

        var certificate = CrewCertificate.Create(
            crewMember.Id,
            "STCW Basic Safety Training",
            "Basic Safety Training Certificate",
            "Flag State Maritime Authority",
            licenseNumber,
            new DateOnly(2024, 1, 1),
            DateOnly.FromDateTime(HarborOpsApiFactory.FixedUtcNow).AddYears(1),
            "Valid");
        await dbContext.Set<CrewCertificate>().AddAsync(certificate);

        await dbContext.SaveChangesAsync();

        return certificate;
    }
}
