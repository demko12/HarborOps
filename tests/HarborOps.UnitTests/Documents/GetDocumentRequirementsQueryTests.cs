using HarborOps.Modules.Documents.Application;
using HarborOps.Modules.Documents.Contracts;
using HarborOps.Modules.Documents.Domain;

namespace HarborOps.UnitTests.Documents;

public sealed class GetDocumentRequirementsQueryTests
{
    private static readonly Guid VesselId = Guid.NewGuid();

    [Fact]
    public async Task GetAsync_filters_by_vessel_and_enabled_state_and_pages_deterministically()
    {
        await using var dbContext = DocumentRequirementTestData.CreateDbContext();
        var firstType = await DocumentRequirementTestData.SeedTypeAsync(dbContext, "A Certificate");
        var secondType = await DocumentRequirementTestData.SeedTypeAsync(dbContext, "B Certificate");
        var disabledType = await DocumentRequirementTestData.SeedTypeAsync(dbContext, "C Certificate");
        await DocumentRequirementTestData.SeedRequirementAsync(dbContext, VesselId, secondType.Id);
        await DocumentRequirementTestData.SeedRequirementAsync(dbContext, VesselId, firstType.Id);
        await DocumentRequirementTestData.SeedRequirementAsync(dbContext, VesselId, disabledType.Id, isEnabled: false);
        await DocumentRequirementTestData.SeedRequirementAsync(dbContext, Guid.NewGuid(), firstType.Id);

        var result = await new GetDocumentRequirementsQuery(dbContext, new FakeDistributionRecipientLookup()).GetAsync(
            new GetDocumentRequirementsRequest { VesselId = VesselId, IsEnabled = true, PageNumber = 1, PageSize = 1 },
            CancellationToken.None);

        Assert.Equal(2, result.TotalCount);
        Assert.Equal("A Certificate", Assert.Single(result.Items).DocumentTypeName);
    }

    [Fact]
    public async Task GetAsync_rejects_an_oversized_page()
    {
        await using var dbContext = DocumentRequirementTestData.CreateDbContext();

        var exception = await Assert.ThrowsAsync<GetDocumentRequirementsValidationException>(() =>
            new GetDocumentRequirementsQuery(dbContext, new FakeDistributionRecipientLookup()).GetAsync(
                new GetDocumentRequirementsRequest { PageSize = GetDocumentRequirementsQuery.MaxPageSize + 1 },
                CancellationToken.None));

        Assert.Contains(GetDocumentRequirementsQuery.PageSizeField, exception.Errors.Keys);
    }

    [Fact]
    public async Task GetByIdAsync_returns_the_history_newest_revision_first()
    {
        await using var dbContext = DocumentRequirementTestData.CreateDbContext();
        var documentType = await DocumentRequirementTestData.SeedTypeAsync(dbContext);
        var requirement = await DocumentRequirementTestData.SeedRequirementAsync(dbContext, VesselId, documentType.Id);
        var disabledBy = Guid.NewGuid();
        requirement.Disable(disabledBy, DocumentRequirementTestData.FixedUtcNow.UtcDateTime);
        await dbContext.Set<VesselDocumentRequirementHistory>()
            .AddAsync(VesselDocumentRequirementHistory.RecordDisabled(requirement, "Vessel sold."));
        await dbContext.SaveChangesAsync();
        var lookup = new FakeDistributionRecipientLookup(new Dictionary<Guid, string> { [disabledBy] = "fleet.manager@harborops.test" });

        var result = await new GetDocumentRequirementsQuery(dbContext, lookup).GetByIdAsync(requirement.Id, CancellationToken.None);

        Assert.False(result.IsEnabled);
        Assert.Equal(requirement.ConcurrencyToken, result.Version);
        Assert.Equal([2, 1], result.History.Select(entry => entry.Revision));
        Assert.Equal("Vessel sold.", result.History[0].Reason);
        Assert.Equal("fleet.manager@harborops.test", result.History[0].ActorEmail);
        Assert.Null(result.History[1].ActorEmail);
    }

    [Fact]
    public async Task GetByIdAsync_rejects_an_unknown_requirement()
    {
        await using var dbContext = DocumentRequirementTestData.CreateDbContext();

        await Assert.ThrowsAsync<DocumentRequirementNotFoundException>(() =>
            new GetDocumentRequirementsQuery(dbContext, new FakeDistributionRecipientLookup()).GetByIdAsync(Guid.NewGuid(), CancellationToken.None));
    }
}
