using HarborOps.Infrastructure.Persistence;
using HarborOps.Modules.Documents.Application;
using HarborOps.Modules.Documents.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.UnitTests.Documents;

public sealed class GetRetentionPoliciesQueryTests
{
    [Fact]
    public async Task GetRetentionPoliciesAsync_returns_policies_with_document_type_name_ordered_by_type_id()
    {
        await using var dbContext = CreateDbContext();
        var typeAId = await SeedDocumentTypeAsync(dbContext, "Zeta Certificate");
        var typeBId = await SeedDocumentTypeAsync(dbContext, "Alpha Certificate");
        await dbContext.Set<RetentionPolicy>().AddRangeAsync(
            new RetentionPolicy(typeBId, 730),
            new RetentionPolicy(typeAId, 365));
        await dbContext.SaveChangesAsync();

        var query = new GetRetentionPoliciesQuery(dbContext);
        var response = await query.GetRetentionPoliciesAsync(CancellationToken.None);

        Assert.Equal(2, response.Items.Count);
        var ordered = response.Items.OrderBy(item => item.DocumentTypeId).ToList();
        Assert.Equal(response.Items, ordered);

        var itemA = response.Items.Single(item => item.DocumentTypeId == typeAId);
        Assert.Equal("Zeta Certificate", itemA.DocumentTypeName);
        Assert.Equal(365, itemA.RetentionPeriodDays);
    }

    [Fact]
    public async Task GetRetentionPoliciesAsync_returns_empty_list_when_no_policies_exist()
    {
        await using var dbContext = CreateDbContext();

        var query = new GetRetentionPoliciesQuery(dbContext);
        var response = await query.GetRetentionPoliciesAsync(CancellationToken.None);

        Assert.Empty(response.Items);
    }

    private static async Task<int> SeedDocumentTypeAsync(HarborOpsDbContext dbContext, string name)
    {
        var documentType = new DocumentType(name, 60, "Flag State Authority", requiresSurvey: true);

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
