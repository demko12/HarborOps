using System.Net;
using System.Net.Http.Json;
using HarborOps.Infrastructure.Persistence;
using HarborOps.IntegrationTests.Authentication;
using HarborOps.Modules.Compliance.Application;
using HarborOps.Modules.Compliance.Contracts;
using HarborOps.Modules.Compliance.Domain;
using HarborOps.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HarborOps.IntegrationTests.Compliance;

public sealed class RegisterNonConformityEndpointTests : IClassFixture<HarborOpsApiFactory>
{
    private readonly HarborOpsApiFactory _application;

    public RegisterNonConformityEndpointTests(HarborOpsApiFactory application)
    {
        _application = application;
    }

    [Fact]
    public async Task Post_registers_and_persists_non_conformity_from_survey_audit()
    {
        var surveyId = await SeedSurveyAuditAsync();
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Compliance.Manage);
        var request = CreateValidRequest() with { SurveyId = surveyId, PscId = null };

        using var response = await client.PostAsJsonAsync(
            "/api/compliance/non-conformities",
            request,
            CancellationToken.None);
        var result = await response.Content.ReadFromJsonAsync<RegisterNonConformityResponse>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(result);
        Assert.Equal(
            $"/api/compliance/non-conformities/{result.Id}",
            response.Headers.Location?.OriginalString);

        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();
        var nonConformity = await dbContext
            .Set<NonConformity>()
            .SingleAsync(
                candidate => candidate.Id == result.Id,
                CancellationToken.None);

        Assert.Equal(surveyId, nonConformity.SurveyId);
        Assert.Null(nonConformity.PscId);
        Assert.Equal(request.EvidenceDocumentId, nonConformity.EvidenceDocumentId);
        Assert.Equal(request.Severity, nonConformity.Severity);
        Assert.Equal(request.CorrectiveActionDeadline, nonConformity.CorrectiveActionDeadline);
        Assert.Equal(NonConformity.ClosureStatusOpen, nonConformity.ClosureStatus);
    }

    [Fact]
    public async Task Post_registers_and_persists_non_conformity_from_psc_inspection()
    {
        var pscId = await SeedPscInspectionAsync();
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Compliance.Manage);
        var request = CreateValidRequest() with { SurveyId = null, PscId = pscId };

        using var response = await client.PostAsJsonAsync(
            "/api/compliance/non-conformities",
            request,
            CancellationToken.None);
        var result = await response.Content.ReadFromJsonAsync<RegisterNonConformityResponse>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(result);
        Assert.Equal(
            $"/api/compliance/non-conformities/{result.Id}",
            response.Headers.Location?.OriginalString);

        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();
        var nonConformity = await dbContext
            .Set<NonConformity>()
            .SingleAsync(
                candidate => candidate.Id == result.Id,
                CancellationToken.None);

        Assert.Equal(pscId, nonConformity.PscId);
        Assert.Null(nonConformity.SurveyId);
        Assert.Equal(request.EvidenceDocumentId, nonConformity.EvidenceDocumentId);
        Assert.Equal(request.Severity, nonConformity.Severity);
        Assert.Equal(request.CorrectiveActionDeadline, nonConformity.CorrectiveActionDeadline);
        Assert.Equal(NonConformity.ClosureStatusOpen, nonConformity.ClosureStatus);
    }

    [Fact]
    public async Task Post_returns_400_when_both_survey_id_and_psc_id_provided()
    {
        var surveyId = await SeedSurveyAuditAsync();
        var pscId = await SeedPscInspectionAsync();
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Compliance.Manage);
        var request = CreateValidRequest() with { SurveyId = surveyId, PscId = pscId };

        using var response = await client.PostAsJsonAsync(
            "/api/compliance/non-conformities",
            request,
            CancellationToken.None);
        var problem = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotNull(problem);
        Assert.Contains(RegisterNonConformityValidator.SourceField, problem.Errors);
    }

    [Fact]
    public async Task Post_returns_400_when_neither_survey_id_nor_psc_id_provided()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Compliance.Manage);
        var request = CreateValidRequest() with { SurveyId = null, PscId = null };

        using var response = await client.PostAsJsonAsync(
            "/api/compliance/non-conformities",
            request,
            CancellationToken.None);
        var problem = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotNull(problem);
        Assert.Contains(RegisterNonConformityValidator.SourceField, problem.Errors);
    }

    [Fact]
    public async Task Post_returns_404_when_survey_id_does_not_exist()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Compliance.Manage);
        var request = CreateValidRequest() with { SurveyId = 999_999, PscId = null };

        using var response = await client.PostAsJsonAsync(
            "/api/compliance/non-conformities",
            request,
            CancellationToken.None);
        var problem = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.NotNull(problem);
        Assert.Contains(RegisterNonConformityValidator.SurveyIdField, problem.Errors);
    }

    [Fact]
    public async Task Post_returns_404_when_psc_id_does_not_exist()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(Permissions.Compliance.Manage);
        var request = CreateValidRequest() with { SurveyId = null, PscId = 999_999 };

        using var response = await client.PostAsJsonAsync(
            "/api/compliance/non-conformities",
            request,
            CancellationToken.None);
        var problem = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.NotNull(problem);
        Assert.Contains(RegisterNonConformityValidator.PscIdField, problem.Errors);
    }

    private async Task<int> SeedSurveyAuditAsync()
    {
        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();
        var survey = new SurveyAudit(
            Guid.NewGuid(),
            new DateOnly(2026, 7, 1),
            "Annual Survey",
            "Pass",
            "No major findings.");

        await dbContext.AddAsync(survey);
        await dbContext.SaveChangesAsync();

        return survey.Id;
    }

    private async Task<int> SeedPscInspectionAsync()
    {
        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();
        var inspection = new PscInspection(
            Guid.NewGuid(),
            Guid.NewGuid(),
            new DateOnly(2026, 7, 1),
            "Port Authority",
            2,
            false);

        await dbContext.AddAsync(inspection);
        await dbContext.SaveChangesAsync();

        return inspection.Id;
    }

    private static RegisterNonConformityRequest CreateValidRequest()
    {
        return new RegisterNonConformityRequest
        {
            SurveyId = null,
            PscId = null,
            EvidenceDocumentId = Guid.NewGuid(),
            Severity = NonConformity.SeverityMajor,
            CorrectiveActionDeadline = new DateOnly(2026, 12, 1),
        };
    }
}
