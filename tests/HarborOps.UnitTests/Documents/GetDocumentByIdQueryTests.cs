using HarborOps.Infrastructure.Persistence;
using HarborOps.Modules.Documents.Application;
using HarborOps.Modules.Documents.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.UnitTests.Documents;

public sealed class GetDocumentByIdQueryTests
{
    [Fact]
    public async Task GetById_returns_details_for_document_without_a_file()
    {
        await using var dbContext = CreateDbContext();
        var documentType = await SeedDocumentTypeAsync(dbContext);
        var document = await SeedDocumentAsync(dbContext, documentType.Id);
        var query = new GetDocumentByIdQuery(dbContext);

        var response = await query.GetByIdAsync(document.Id, CancellationToken.None);

        Assert.Equal(document.Id, response.Id);
        Assert.Equal(document.VesselId, response.VesselId);
        Assert.Equal(documentType.Id, response.DocumentTypeId);
        Assert.Equal(documentType.Name, response.DocumentTypeName);
        Assert.Equal(document.Number, response.Number);
        Assert.Equal(document.IssueDate, response.IssueDate);
        Assert.Equal(document.ExpiryDate, response.ExpiryDate);
        Assert.Equal(document.IssuingAuthority, response.IssuingAuthority);
        Assert.Equal(document.Status, response.Status);
        Assert.False(response.HasFile);
        Assert.Null(response.FileName);
        Assert.Null(response.ContentType);
        Assert.Null(response.SizeBytes);
        Assert.Null(response.UploadedBy);
        Assert.Null(response.UploadedAtUtc);
    }

    [Fact]
    public async Task GetById_returns_details_for_document_with_a_file()
    {
        await using var dbContext = CreateDbContext();
        var documentType = await SeedDocumentTypeAsync(dbContext);
        var document = await SeedDocumentAsync(dbContext, documentType.Id);
        var uploadedBy = Guid.NewGuid();
        var uploadedAtUtc = new DateTime(2026, 8, 1, 10, 0, 0, DateTimeKind.Utc);
        document.AttachFile("certificate.pdf", "application/pdf", "storage-key-1", 2048, uploadedBy, uploadedAtUtc);
        await dbContext.SaveChangesAsync();
        var query = new GetDocumentByIdQuery(dbContext);

        var response = await query.GetByIdAsync(document.Id, CancellationToken.None);

        Assert.True(response.HasFile);
        Assert.Equal("certificate.pdf", response.FileName);
        Assert.Equal("application/pdf", response.ContentType);
        Assert.Equal(2048, response.SizeBytes);
        Assert.Equal(uploadedBy, response.UploadedBy);
        Assert.Equal(uploadedAtUtc, response.UploadedAtUtc);
    }

    [Fact]
    public async Task GetById_rejects_missing_document()
    {
        await using var dbContext = CreateDbContext();
        var query = new GetDocumentByIdQuery(dbContext);

        await Assert.ThrowsAsync<DocumentNotFoundException>(
            () => query.GetByIdAsync(Guid.NewGuid(), CancellationToken.None));
    }

    private static async Task<DocumentType> SeedDocumentTypeAsync(HarborOpsDbContext dbContext)
    {
        var documentType = new DocumentType(
            "Safety Management Certificate",
            60,
            "Flag State Authority",
            requiresSurvey: true);

        await dbContext.Set<DocumentType>().AddAsync(documentType);
        await dbContext.SaveChangesAsync();

        return documentType;
    }

    private static async Task<Document> SeedDocumentAsync(HarborOpsDbContext dbContext, int documentTypeId)
    {
        var document = new Document(
            Guid.NewGuid(),
            documentTypeId,
            "CERT-1001",
            new DateOnly(2026, 1, 1),
            new DateOnly(2027, 1, 1),
            "Flag State Authority",
            "Active");

        await dbContext.Set<Document>().AddAsync(document);
        await dbContext.SaveChangesAsync();

        return document;
    }

    private static HarborOpsDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<HarborOpsDbContext>()
            .UseInMemoryDatabase($"HarborOpsUnitTests-{Guid.NewGuid()}")
            .Options;

        return new HarborOpsDbContext(options);
    }
}
