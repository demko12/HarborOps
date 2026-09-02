using System.Net;
using System.Net.Http.Json;
using HarborOps.Infrastructure.Persistence;
using HarborOps.IntegrationTests.Authentication;
using HarborOps.Modules.Crew;
using HarborOps.Modules.Crew.Application;
using HarborOps.Modules.Crew.Contracts;
using HarborOps.Modules.Crew.Domain;
using HarborOps.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HarborOps.IntegrationTests.Crew;

public sealed class RegisterCrewCertificateEndpointTests : IntegrationTestBase
{
    private readonly HarborOpsApiFactory _application;

    public RegisterCrewCertificateEndpointTests(HarborOpsApiFactory application)
        : base(application)
    {
        _application = application;
    }

    [Fact]
    public async Task Post_registers_and_persists_crew_certificate()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Crew.Manage);
        var crewMemberId = await SeedCrewMemberAsync("IMO910001");
        var request = CreateValidRequest("STCW-9001");

        using var response = await client.PostAsJsonAsync(
            $"/api/crew/members/{crewMemberId}/certificates",
            request,
            CancellationToken.None);
        var result = await response.Content.ReadFromJsonAsync<RegisterCrewCertificateResponse>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(result);
        Assert.NotEqual(Guid.Empty, result.Id);
        Assert.Equal(
            $"/api/crew/members/{crewMemberId}/certificates/{result.Id}",
            response.Headers.Location?.OriginalString);

        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();
        var certificate = await dbContext
            .Set<CrewCertificate>()
            .SingleAsync(candidate => candidate.Id == result.Id, CancellationToken.None);
        var entityType = dbContext.Model.FindEntityType(typeof(CrewCertificate));

        Assert.Equal(crewMemberId, certificate.CrewMemberId);
        Assert.Equal(request.Type, certificate.Type);
        Assert.Equal(request.Name, certificate.Name);
        Assert.Equal(request.IssuingAuthority, certificate.IssuingAuthority);
        Assert.Equal("STCW-9001", certificate.LicenseNumber);
        Assert.Equal(request.IssueDate, certificate.IssueDate);
        Assert.Equal(request.ExpiryDate, certificate.ExpiryDate);
        Assert.Equal("Valid", certificate.Status);
        Assert.Equal("CrewCertificates", entityType!.GetTableName());
        Assert.Equal(CrewModule.Schema, entityType!.GetSchema());
    }

    [Fact]
    public async Task Post_returns_validation_problem_for_invalid_input()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Crew.Manage);
        var crewMemberId = await SeedCrewMemberAsync("IMO910002");
        var request = new RegisterCrewCertificateRequest
        {
            Type = string.Empty,
            Name = null,
            IssuingAuthority = " ",
            LicenseNumber = null,
            IssueDate = null,
            ExpiryDate = null,
        };

        using var response = await client.PostAsJsonAsync(
            $"/api/crew/members/{crewMemberId}/certificates",
            request,
            CancellationToken.None);
        var problem = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotNull(problem);
        Assert.Contains(RegisterCrewCertificateValidator.TypeField, problem.Errors);
        Assert.Contains(RegisterCrewCertificateValidator.NameField, problem.Errors);
        Assert.Contains(RegisterCrewCertificateValidator.IssuingAuthorityField, problem.Errors);
        Assert.Contains(RegisterCrewCertificateValidator.LicenseNumberField, problem.Errors);
        Assert.Contains(RegisterCrewCertificateValidator.IssueDateField, problem.Errors);
        Assert.Contains(RegisterCrewCertificateValidator.ExpiryDateField, problem.Errors);
    }

    [Fact]
    public async Task Post_returns_not_found_for_unknown_crew_member()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Crew.Manage);
        var request = CreateValidRequest("STCW-9003");

        using var response = await client.PostAsJsonAsync(
            $"/api/crew/members/{Guid.NewGuid()}/certificates",
            request,
            CancellationToken.None);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(CancellationToken.None);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.NotNull(problem);
        Assert.Equal(StatusCodes.Status404NotFound, problem.Status);
    }

    [Fact]
    public async Task Post_returns_conflict_for_duplicate_license_number()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Crew.Manage);
        var crewMemberId = await SeedCrewMemberAsync("IMO910004");
        var firstRequest = CreateValidRequest("STCW-9004");
        var duplicateRequest = CreateValidRequest(" stcw-9004 ") with { Name = "Different Certificate Name" };

        using var firstResponse = await client.PostAsJsonAsync(
            $"/api/crew/members/{crewMemberId}/certificates",
            firstRequest,
            CancellationToken.None);
        Assert.Equal(HttpStatusCode.Created, firstResponse.StatusCode);

        using var response = await client.PostAsJsonAsync(
            $"/api/crew/members/{crewMemberId}/certificates",
            duplicateRequest,
            CancellationToken.None);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(CancellationToken.None);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.NotNull(problem);
        Assert.Equal(StatusCodes.Status409Conflict, problem.Status);

        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();

        Assert.Equal(
            1,
            await dbContext
                .Set<CrewCertificate>()
                .CountAsync(
                    candidate => candidate.CrewMemberId == crewMemberId,
                    CancellationToken.None));
    }

    [Fact]
    public async Task Post_without_authentication_returns_401()
    {
        using var client = _application.CreateClient();
        var request = CreateValidRequest("STCW-9005");

        using var response = await client.PostAsJsonAsync(
            $"/api/crew/members/{Guid.NewGuid()}/certificates",
            request,
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private static RegisterCrewCertificateRequest CreateValidRequest(string licenseNumber)
    {
        return new RegisterCrewCertificateRequest
        {
            Type = "STCW Basic Safety Training",
            Name = "Basic Safety Training Certificate",
            IssuingAuthority = "Flag State Maritime Authority",
            LicenseNumber = licenseNumber,
            IssueDate = new DateOnly(2024, 1, 1),
            ExpiryDate = DateOnly.FromDateTime(HarborOpsApiFactory.FixedUtcNow).AddYears(1),
        };
    }

    private async Task<Guid> SeedCrewMemberAsync(string imoSeafarerId)
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

        await dbContext.SaveChangesAsync();

        return crewMember.Id;
    }
}
