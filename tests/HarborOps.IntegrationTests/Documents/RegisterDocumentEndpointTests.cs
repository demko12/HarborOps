using System.Net;
using System.Net.Http.Json;
using HarborOps.Infrastructure.Persistence;
using HarborOps.Modules.Documents;
using HarborOps.Modules.Documents.Application;
using HarborOps.Modules.Documents.Contracts;
using HarborOps.Modules.Documents.Domain;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HarborOps.IntegrationTests.Documents;

public sealed class RegisterDocumentEndpointTests : IClassFixture<HarborOpsApiFactory>
{
    private readonly HarborOpsApiFactory _application;

    public RegisterDocumentEndpointTests(HarborOpsApiFactory application)
    {
        _application = application;
    }

    [Fact]
    public async Task Post_registers_and_persists_document()
    {
        using var client = _application.CreateClient();
        var vesselId = Guid.NewGuid();
        var documentTypeId = await SeedDocumentTypeAsync();
        var request = CreateValidRequest(vesselId, documentTypeId);

        using var response = await client.PostAsJsonAsync(
            "/api/documents",
            request,
            CancellationToken.None);
        var result = await response.Content.ReadFromJsonAsync<RegisterDocumentResponse>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(result);
        Assert.NotEqual(Guid.Empty, result.Id);
        Assert.Equal($"/api/documents/{result.Id}", response.Headers.Location?.OriginalString);

        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();
        var document = await dbContext
            .Set<Document>()
            .SingleAsync(
                candidate => candidate.Id == result.Id,
                CancellationToken.None);
        var entityType = dbContext.Model.FindEntityType(typeof(Document))!;

        Assert.Equal(request.Number, document.Number);
        Assert.Equal(request.VesselId, document.VesselId);
        Assert.Equal(request.DocumentTypeId, document.DocumentTypeId);
        Assert.Equal(request.IssueDate, document.IssueDate);
        Assert.Equal(request.ExpiryDate, document.ExpiryDate);
        Assert.Equal(request.IssuingAuthority, document.IssuingAuthority);
        Assert.Equal(request.Status, document.Status);
        Assert.Equal("Documents", entityType.GetTableName());
        Assert.Equal(DocumentsModule.Schema, entityType.GetSchema());
    }

    [Fact]
    public async Task Post_returns_validation_problem_for_invalid_input()
    {
        using var client = _application.CreateClient();
        var request = new RegisterDocumentRequest
        {
            VesselId = null,
            DocumentTypeId = null,
            Number = string.Empty,
            IssueDate = null,
            ExpiryDate = null,
            IssuingAuthority = "Flag State Authority",
            Status = "Active",
        };

        using var response = await client.PostAsJsonAsync(
            "/api/documents",
            request,
            CancellationToken.None);
        var problem = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotNull(problem);
        Assert.Contains(RegisterDocumentValidator.VesselIdField, problem.Errors);
        Assert.Contains(RegisterDocumentValidator.DocumentTypeIdField, problem.Errors);
        Assert.Contains(RegisterDocumentValidator.NumberField, problem.Errors);
        Assert.Contains(RegisterDocumentValidator.IssueDateField, problem.Errors);
        Assert.Contains(RegisterDocumentValidator.ExpiryDateField, problem.Errors);
    }

    [Fact]
    public async Task Post_returns_bad_request_problem_for_unknown_document_type()
    {
        using var client = _application.CreateClient();
        var request = CreateValidRequest(Guid.NewGuid(), 999_999);

        using var response = await client.PostAsJsonAsync(
            "/api/documents",
            request,
            CancellationToken.None);
        var problem = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotNull(problem);
        Assert.Contains(RegisterDocumentValidator.DocumentTypeIdField, problem.Errors);
    }

    [Fact]
    public async Task Post_returns_conflict_problem_for_duplicate_document_number()
    {
        using var client = _application.CreateClient();
        var vesselId = Guid.NewGuid();
        var documentTypeId = await SeedDocumentTypeAsync();
        var firstRequest = CreateValidRequest(vesselId, documentTypeId);
        var duplicateRequest = firstRequest with { IssuingAuthority = "Different Authority" };

        using var firstResponse = await client.PostAsJsonAsync(
            "/api/documents",
            firstRequest,
            CancellationToken.None);
        Assert.Equal(HttpStatusCode.Created, firstResponse.StatusCode);

        using var response = await client.PostAsJsonAsync(
            "/api/documents",
            duplicateRequest,
            CancellationToken.None);
        var problem = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.NotNull(problem);
        Assert.Contains(RegisterDocumentValidator.NumberField, problem.Errors);
    }

    private static RegisterDocumentRequest CreateValidRequest(Guid vesselId, int documentTypeId)
    {
        return new RegisterDocumentRequest
        {
            VesselId = vesselId,
            DocumentTypeId = documentTypeId,
            Number = "CERT-1001",
            IssueDate = new DateOnly(2026, 1, 1),
            ExpiryDate = new DateOnly(2027, 1, 1),
            IssuingAuthority = "Flag State Authority",
            Status = "Active",
        };
    }

    private async Task<int> SeedDocumentTypeAsync()
    {
        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();
        var documentType = new DocumentType(
            "Safety Management Certificate",
            60,
            "Flag State Authority",
            requiresSurvey: true);

        await dbContext.Set<DocumentType>().AddAsync(documentType);
        await dbContext.SaveChangesAsync();

        return documentType.Id;
    }
}
