using HarborOps.Infrastructure.Persistence;
using HarborOps.Modules.Documents.Application;
using HarborOps.Modules.Documents.Contracts;
using HarborOps.Modules.Documents.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.UnitTests.Documents;

public sealed class RetentionPolicyAdminServiceTests
{
    [Fact]
    public async Task UpdatePolicyAsync_creates_policy_when_none_exists()
    {
        await using var dbContext = CreateDbContext();
        var documentTypeId = await SeedDocumentTypeAsync(dbContext);
        var service = new RetentionPolicyAdminService(dbContext);

        await service.UpdatePolicyAsync(documentTypeId, new UpdateRetentionPolicyRequest(365), CancellationToken.None);

        var policy = await dbContext.Set<RetentionPolicy>().SingleAsync(p => p.DocumentTypeId == documentTypeId);
        Assert.Equal(365, policy.RetentionPeriodDays);
    }

    [Fact]
    public async Task UpdatePolicyAsync_updates_existing_policy_and_changes_concurrency_token()
    {
        await using var dbContext = CreateDbContext();
        var documentTypeId = await SeedDocumentTypeAsync(dbContext);
        var service = new RetentionPolicyAdminService(dbContext);
        await service.UpdatePolicyAsync(documentTypeId, new UpdateRetentionPolicyRequest(365), CancellationToken.None);
        var tokenBefore = (await dbContext.Set<RetentionPolicy>().AsNoTracking().SingleAsync(p => p.DocumentTypeId == documentTypeId)).ConcurrencyToken;

        await service.UpdatePolicyAsync(documentTypeId, new UpdateRetentionPolicyRequest(730), CancellationToken.None);

        var policy = await dbContext.Set<RetentionPolicy>().AsNoTracking().SingleAsync(p => p.DocumentTypeId == documentTypeId);
        Assert.Equal(730, policy.RetentionPeriodDays);
        Assert.NotEqual(tokenBefore, policy.ConcurrencyToken);
    }

    [Fact]
    public async Task UpdatePolicyAsync_rejects_unknown_document_type()
    {
        await using var dbContext = CreateDbContext();
        var service = new RetentionPolicyAdminService(dbContext);

        await Assert.ThrowsAsync<DocumentTypeNotFoundException>(
            () => service.UpdatePolicyAsync(999, new UpdateRetentionPolicyRequest(365), CancellationToken.None));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task UpdatePolicyAsync_surfaces_non_positive_retention_period_as_validation_failure(int? retentionPeriodDays)
    {
        await using var dbContext = CreateDbContext();
        var documentTypeId = await SeedDocumentTypeAsync(dbContext);
        var service = new RetentionPolicyAdminService(dbContext);

        var exception = await Assert.ThrowsAsync<RetentionPolicyValidationException>(
            () => service.UpdatePolicyAsync(documentTypeId, new UpdateRetentionPolicyRequest(retentionPeriodDays), CancellationToken.None));

        Assert.Contains(RetentionPolicyAdminService.RetentionPeriodDaysField, exception.Errors);
    }

    private static async Task<int> SeedDocumentTypeAsync(HarborOpsDbContext dbContext)
    {
        var documentType = new DocumentType("Safety Management Certificate", 60, "Flag State Authority", requiresSurvey: true);

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
