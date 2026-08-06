using HarborOps.Infrastructure.Persistence;
using HarborOps.Modules.Documents.Application;
using HarborOps.Modules.Documents.Contracts;
using HarborOps.Modules.Documents.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.UnitTests.Documents;

public sealed class RegisterDocumentServiceTests
{
    [Fact]
    public async Task Register_rejects_invalid_input()
    {
        await using var dbContext = CreateDbContext();
        var service = new RegisterDocumentService(dbContext);
        var request = new RegisterDocumentRequest
        {
            VesselId = null,
            DocumentTypeId = null,
            Number = " ",
            IssueDate = null,
            ExpiryDate = null,
            IssuingAuthority = null,
            Status = string.Empty,
        };

        var exception = await Assert.ThrowsAsync<RegisterDocumentValidationException>(
            () => service.RegisterAsync(request, CancellationToken.None));

        Assert.Equal(7, exception.Errors.Count);
        Assert.Contains(RegisterDocumentValidator.VesselIdField, exception.Errors);
        Assert.Contains(RegisterDocumentValidator.DocumentTypeIdField, exception.Errors);
        Assert.Contains(RegisterDocumentValidator.NumberField, exception.Errors);
        Assert.Contains(RegisterDocumentValidator.IssueDateField, exception.Errors);
        Assert.Contains(RegisterDocumentValidator.ExpiryDateField, exception.Errors);
        Assert.Contains(RegisterDocumentValidator.IssuingAuthorityField, exception.Errors);
        Assert.Contains(RegisterDocumentValidator.StatusField, exception.Errors);
    }

    [Fact]
    public async Task Register_rejects_empty_vessel_id()
    {
        await using var dbContext = CreateDbContext();
        var documentTypeId = await SeedDocumentTypeAsync(dbContext);
        var service = new RegisterDocumentService(dbContext);
        var request = CreateValidRequest(Guid.Empty, documentTypeId);

        var exception = await Assert.ThrowsAsync<RegisterDocumentValidationException>(
            () => service.RegisterAsync(request, CancellationToken.None));

        Assert.Contains(RegisterDocumentValidator.VesselIdField, exception.Errors);
    }

    [Fact]
    public async Task Register_rejects_expiry_date_before_issue_date()
    {
        await using var dbContext = CreateDbContext();
        var documentTypeId = await SeedDocumentTypeAsync(dbContext);
        var service = new RegisterDocumentService(dbContext);
        var request = CreateValidRequest(Guid.NewGuid(), documentTypeId) with
        {
            IssueDate = new DateOnly(2026, 6, 1),
            ExpiryDate = new DateOnly(2026, 1, 1),
        };

        var exception = await Assert.ThrowsAsync<RegisterDocumentValidationException>(
            () => service.RegisterAsync(request, CancellationToken.None));

        Assert.Contains(RegisterDocumentValidator.ExpiryDateField, exception.Errors);
    }

    [Fact]
    public async Task Register_allows_same_document_number_for_different_vessel()
    {
        await using var dbContext = CreateDbContext();
        var documentTypeId = await SeedDocumentTypeAsync(dbContext);
        var service = new RegisterDocumentService(dbContext);
        var firstRequest = CreateValidRequest(Guid.NewGuid(), documentTypeId);
        var secondRequest = CreateValidRequest(Guid.NewGuid(), documentTypeId);

        await service.RegisterAsync(firstRequest, CancellationToken.None);
        var response = await service.RegisterAsync(secondRequest, CancellationToken.None);

        Assert.NotEqual(Guid.Empty, response.Id);
        var documentCount = await dbContext.Set<Document>().CountAsync(CancellationToken.None);
        Assert.Equal(2, documentCount);
    }

    [Fact]
    public async Task Register_rejects_duplicate_document_number()
    {
        await using var dbContext = CreateDbContext();
        var documentTypeId = await SeedDocumentTypeAsync(dbContext);
        var service = new RegisterDocumentService(dbContext);
        var vesselId = Guid.NewGuid();
        var firstRequest = CreateValidRequest(vesselId, documentTypeId);
        var duplicateRequest = firstRequest with { IssuingAuthority = " Different Authority " };

        await service.RegisterAsync(firstRequest, CancellationToken.None);

        var exception = await Assert.ThrowsAsync<DuplicateDocumentNumberException>(
            () => service.RegisterAsync(duplicateRequest, CancellationToken.None));

        Assert.Equal(
            $"A document with number 'CERT-1001' already exists for vessel '{vesselId}'.",
            exception.Message);
    }

    [Fact]
    public async Task Register_rejects_unknown_document_type()
    {
        await using var dbContext = CreateDbContext();
        var service = new RegisterDocumentService(dbContext);
        var request = CreateValidRequest(Guid.NewGuid(), 999_999);

        var exception = await Assert.ThrowsAsync<DocumentTypeNotFoundException>(
            () => service.RegisterAsync(request, CancellationToken.None));

        Assert.Equal(request.DocumentTypeId, exception.DocumentTypeId);
    }

    [Fact]
    public async Task Register_persists_document_and_returns_id()
    {
        await using var dbContext = CreateDbContext();
        var documentTypeId = await SeedDocumentTypeAsync(dbContext);
        var service = new RegisterDocumentService(dbContext);
        var vesselId = Guid.NewGuid();
        var request = CreateValidRequest(vesselId, documentTypeId);

        var response = await service.RegisterAsync(request, CancellationToken.None);

        var document = await dbContext
            .Set<Document>()
            .SingleAsync(CancellationToken.None);

        Assert.NotEqual(Guid.Empty, response.Id);
        Assert.Equal(response.Id, document.Id);
        Assert.Equal(vesselId, document.VesselId);
        Assert.Equal(documentTypeId, document.DocumentTypeId);
        Assert.Equal("CERT-1001", document.Number);
        Assert.Equal(new DateOnly(2026, 1, 1), document.IssueDate);
        Assert.Equal(new DateOnly(2027, 1, 1), document.ExpiryDate);
        Assert.Equal("Flag State Authority", document.IssuingAuthority);
        Assert.Equal("Active", document.Status);
    }

    private static RegisterDocumentRequest CreateValidRequest(Guid vesselId, int documentTypeId)
    {
        return new RegisterDocumentRequest
        {
            VesselId = vesselId,
            DocumentTypeId = documentTypeId,
            Number = " CERT-1001 ",
            IssueDate = new DateOnly(2026, 1, 1),
            ExpiryDate = new DateOnly(2027, 1, 1),
            IssuingAuthority = " Flag State Authority ",
            Status = " Active ",
        };
    }

    private static async Task<int> SeedDocumentTypeAsync(HarborOpsDbContext dbContext)
    {
        var documentType = new DocumentType(
            "Safety Management Certificate",
            60,
            "Flag State Authority",
            requiresSurvey: true);

        await dbContext.Set<DocumentType>().AddAsync(documentType);
        await dbContext.SaveChangesAsync();

        return documentType.Id;
    }

    private static HarborOpsDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<HarborOpsDbContext>()
            .UseInMemoryDatabase($"HarborOpsUnitTests-{Guid.NewGuid()}")
            .Options;

        return new HarborOpsDbContext(options);
    }
}
