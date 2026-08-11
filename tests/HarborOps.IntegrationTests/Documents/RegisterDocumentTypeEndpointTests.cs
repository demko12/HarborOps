using System.Net;
using System.Net.Http.Json;
using HarborOps.Infrastructure.Persistence;
using HarborOps.IntegrationTests.Authentication;
using HarborOps.Modules.Documents;
using HarborOps.Modules.Documents.Application;
using HarborOps.Modules.Documents.Contracts;
using HarborOps.Modules.Documents.Domain;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HarborOps.IntegrationTests.Documents;

public sealed class RegisterDocumentTypeEndpointTests : IntegrationTestBase
{
    private readonly HarborOpsApiFactory _application;

    public RegisterDocumentTypeEndpointTests(HarborOpsApiFactory application)
        : base(application)
    {
        _application = application;
    }

    [Fact]
    public async Task Post_registers_and_persists_document_type()
    {
        using var client = await _application.CreateAuthenticatedClientAsync();
        var request = CreateValidRequest();

        using var response = await client.PostAsJsonAsync(
            "/api/documents/types",
            request,
            CancellationToken.None);
        var result = await response.Content.ReadFromJsonAsync<RegisterDocumentTypeResponse>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(result);
        Assert.True(result.Id > 0);
        Assert.Equal($"/api/documents/types/{result.Id}", response.Headers.Location?.OriginalString);

        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();
        var documentType = await dbContext
            .Set<DocumentType>()
            .SingleAsync(candidate => candidate.Id == result.Id, CancellationToken.None);
        var entityType = dbContext.Model.FindEntityType(typeof(DocumentType))!;

        Assert.Equal(request.Name, documentType.Name);
        Assert.Equal(request.IssuingAuthority, documentType.IssuingAuthority);
        Assert.Equal(request.MandatoryValidityPeriodDays, documentType.MandatoryValidityPeriod);
        Assert.Equal(request.RequiresSurvey, documentType.RequiresSurvey);
        Assert.Equal("DocumentTypes", entityType.GetTableName());
        Assert.Equal(DocumentsModule.Schema, entityType.GetSchema());
    }

    [Fact]
    public async Task Post_returns_validation_problem_for_missing_name()
    {
        using var client = await _application.CreateAuthenticatedClientAsync();
        var request = CreateValidRequest() with { Name = " " };

        using var response = await client.PostAsJsonAsync(
            "/api/documents/types",
            request,
            CancellationToken.None);
        var problem = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotNull(problem);
        Assert.Contains(RegisterDocumentTypeValidator.NameField, problem.Errors);
    }

    [Fact]
    public async Task Post_returns_validation_problem_when_requires_survey_field_is_absent()
    {
        using var client = await _application.CreateAuthenticatedClientAsync();
        var payloadWithoutRequiresSurvey = new
        {
            name = "Port State Certificate",
            mandatoryValidityPeriodDays = 60,
            issuingAuthority = "Flag State Authority",
        };

        using var response = await client.PostAsJsonAsync(
            "/api/documents/types",
            payloadWithoutRequiresSurvey,
            CancellationToken.None);
        var problem = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotNull(problem);
        Assert.Contains(RegisterDocumentTypeValidator.RequiresSurveyField, problem.Errors);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3651)]
    public async Task Post_returns_validation_problem_for_out_of_range_mandatory_validity_period_days(int days)
    {
        using var client = await _application.CreateAuthenticatedClientAsync();
        var request = CreateValidRequest() with { MandatoryValidityPeriodDays = days };

        using var response = await client.PostAsJsonAsync(
            "/api/documents/types",
            request,
            CancellationToken.None);
        var problem = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotNull(problem);
        Assert.Contains(RegisterDocumentTypeValidator.MandatoryValidityPeriodDaysField, problem.Errors);
    }

    [Fact]
    public async Task Post_returns_conflict_problem_for_duplicate_name()
    {
        using var client = await _application.CreateAuthenticatedClientAsync();
        var firstRequest = CreateValidRequest();
        var duplicateRequest = firstRequest with { Name = " port state certificate " };

        using var firstResponse = await client.PostAsJsonAsync(
            "/api/documents/types",
            firstRequest,
            CancellationToken.None);
        Assert.Equal(HttpStatusCode.Created, firstResponse.StatusCode);

        using var response = await client.PostAsJsonAsync(
            "/api/documents/types",
            duplicateRequest,
            CancellationToken.None);
        var problem = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.NotNull(problem);
        Assert.Contains(RegisterDocumentTypeValidator.NameField, problem.Errors);
    }

    [Fact]
    public async Task Post_returns_distinct_database_generated_ids_for_consecutive_registrations()
    {
        using var client = await _application.CreateAuthenticatedClientAsync();
        var firstRequest = CreateValidRequest() with { Name = "Port State Certificate" };
        var secondRequest = CreateValidRequest() with { Name = "Safety Management Certificate" };

        using var firstResponse = await client.PostAsJsonAsync(
            "/api/documents/types",
            firstRequest,
            CancellationToken.None);
        using var secondResponse = await client.PostAsJsonAsync(
            "/api/documents/types",
            secondRequest,
            CancellationToken.None);
        var firstResult = await firstResponse.Content.ReadFromJsonAsync<RegisterDocumentTypeResponse>(
            CancellationToken.None);
        var secondResult = await secondResponse.Content.ReadFromJsonAsync<RegisterDocumentTypeResponse>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Created, firstResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Created, secondResponse.StatusCode);
        Assert.NotNull(firstResult);
        Assert.NotNull(secondResult);
        Assert.NotEqual(firstResult.Id, secondResult.Id);
        Assert.True(secondResult.Id > firstResult.Id);
    }

    [Fact]
    public async Task Post_without_authentication_returns_401()
    {
        using var client = _application.CreateClient();
        var request = CreateValidRequest();

        using var response = await client.PostAsJsonAsync(
            "/api/documents/types",
            request,
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private static RegisterDocumentTypeRequest CreateValidRequest()
    {
        return new RegisterDocumentTypeRequest
        {
            Name = "Port State Certificate",
            MandatoryValidityPeriodDays = 60,
            IssuingAuthority = "Flag State Authority",
            RequiresSurvey = true,
        };
    }
}
