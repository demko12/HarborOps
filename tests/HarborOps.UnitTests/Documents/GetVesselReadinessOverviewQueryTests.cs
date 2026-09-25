using HarborOps.Infrastructure.Persistence;
using HarborOps.Modules.Documents.Application;
using HarborOps.Modules.Documents.Contracts;
using HarborOps.Modules.Documents.Domain;

namespace HarborOps.UnitTests.Documents;

public sealed class GetVesselReadinessOverviewQueryTests
{
    private static readonly DateOnly Today = DocumentRequirementTestData.Today;

    private static readonly VesselExistenceResult ReadyVessel = new(Guid.NewGuid(), "Adriatic Star");
    private static readonly VesselExistenceResult NotReadyVessel = new(Guid.NewGuid(), "Boka Pearl");
    private static readonly VesselExistenceResult UnconfiguredVessel = new(Guid.NewGuid(), "Cetina");

    [Fact]
    public async Task Every_vessel_is_listed_with_its_own_status_including_vessels_without_requirements()
    {
        await using var dbContext = DocumentRequirementTestData.CreateDbContext();
        var safety = await DocumentRequirementTestData.SeedTypeAsync(dbContext);
        var loadLine = await DocumentRequirementTestData.SeedTypeAsync(dbContext, "Load Line Certificate");
        await DocumentRequirementTestData.SeedRequirementAsync(dbContext, ReadyVessel.VesselId, safety.Id);
        await DocumentRequirementTestData.SeedDocumentAsync(dbContext, ReadyVessel.VesselId, safety.Id);
        await DocumentRequirementTestData.SeedRequirementAsync(dbContext, NotReadyVessel.VesselId, safety.Id);
        await DocumentRequirementTestData.SeedRequirementAsync(dbContext, NotReadyVessel.VesselId, loadLine.Id);
        await DocumentRequirementTestData.SeedDocumentAsync(dbContext, NotReadyVessel.VesselId, safety.Id);

        var result = await QueryAsync(dbContext, new GetVesselReadinessOverviewRequest());

        Assert.Equal(3, result.TotalCount);
        Assert.Equal(
            [
                new VesselReadinessOverviewItem(ReadyVessel.VesselId, ReadyVessel.Name, DocumentReadinessRules.ReadyVesselStatus, new DocumentReadinessSummary(1, 1, 0)),
                new VesselReadinessOverviewItem(NotReadyVessel.VesselId, NotReadyVessel.Name, DocumentReadinessRules.NotReadyVesselStatus, new DocumentReadinessSummary(2, 1, 1)),
                new VesselReadinessOverviewItem(UnconfiguredVessel.VesselId, UnconfiguredVessel.Name, DocumentReadinessRules.NotConfiguredVesselStatus, new DocumentReadinessSummary(0, 0, 0)),
            ],
            result.Items);
    }

    [Fact]
    public async Task A_vessel_with_only_disabled_requirements_is_not_configured()
    {
        await using var dbContext = DocumentRequirementTestData.CreateDbContext();
        var documentType = await DocumentRequirementTestData.SeedTypeAsync(dbContext);
        await DocumentRequirementTestData.SeedRequirementAsync(dbContext, UnconfiguredVessel.VesselId, documentType.Id, isEnabled: false);

        var result = await QueryAsync(dbContext, new GetVesselReadinessOverviewRequest());

        var item = Assert.Single(result.Items, vessel => vessel.VesselId == UnconfiguredVessel.VesselId);
        Assert.Equal(DocumentReadinessRules.NotConfiguredVesselStatus, item.Status);
    }

    [Fact]
    public async Task Required_through_and_the_validity_buffer_decide_the_vessel_status()
    {
        await using var dbContext = DocumentRequirementTestData.CreateDbContext();
        var documentType = await DocumentRequirementTestData.SeedTypeAsync(dbContext);
        await DocumentRequirementTestData.SeedRequirementAsync(dbContext, ReadyVessel.VesselId, documentType.Id, minimumRemainingValidityDays: 30);
        await DocumentRequirementTestData.SeedDocumentAsync(dbContext, ReadyVessel.VesselId, documentType.Id, expiryDate: Today.AddDays(40));

        var boundary = await QueryAsync(dbContext, new GetVesselReadinessOverviewRequest { RequiredThrough = Today.AddDays(10) });
        var beyond = await QueryAsync(dbContext, new GetVesselReadinessOverviewRequest { RequiredThrough = Today.AddDays(11) });

        Assert.Equal(DocumentReadinessRules.ReadyVesselStatus, boundary.Items.Single(vessel => vessel.VesselId == ReadyVessel.VesselId).Status);
        Assert.Equal(DocumentReadinessRules.NotReadyVesselStatus, beyond.Items.Single(vessel => vessel.VesselId == ReadyVessel.VesselId).Status);
        Assert.Equal(Today.AddDays(11), beyond.RequiredThrough);
        Assert.Equal(Today, beyond.Today);
    }

    [Fact]
    public async Task Paging_is_delegated_to_the_fleet_directory()
    {
        await using var dbContext = DocumentRequirementTestData.CreateDbContext();
        var directory = new FakeDocumentsVesselDirectory(ReadyVessel, NotReadyVessel, UnconfiguredVessel);

        var result = await new GetVesselReadinessOverviewQuery(
                dbContext,
                directory,
                new FixedTimeProvider(DocumentRequirementTestData.FixedUtcNow))
            .GetAsync(new GetVesselReadinessOverviewRequest { PageNumber = 2, PageSize = 2 }, CancellationToken.None);

        Assert.Equal((2, 2), directory.LastRequest);
        Assert.Equal(UnconfiguredVessel.VesselId, Assert.Single(result.Items).VesselId);
        Assert.Equal(3, result.TotalCount);
        Assert.Equal(2, result.PageNumber);
        Assert.Equal(2, result.PageSize);
    }

    [Fact]
    public async Task Querying_does_not_create_requirements_or_documents()
    {
        await using var dbContext = DocumentRequirementTestData.CreateDbContext();

        await QueryAsync(dbContext, new GetVesselReadinessOverviewRequest());

        Assert.Empty(dbContext.Set<VesselDocumentRequirement>());
        Assert.Empty(dbContext.Set<Document>());
    }

    [Theory]
    [InlineData(-1, null, null, GetDocumentReadinessQuery.RequiredThroughField)]
    [InlineData(366, null, null, GetDocumentReadinessQuery.RequiredThroughField)]
    [InlineData(null, 0, null, GetDocumentReadinessQuery.PageNumberField)]
    [InlineData(null, null, 101, GetDocumentReadinessQuery.PageSizeField)]
    public async Task Invalid_filters_are_rejected(int? requiredThroughOffset, int? pageNumber, int? pageSize, string field)
    {
        await using var dbContext = DocumentRequirementTestData.CreateDbContext();

        var exception = await Assert.ThrowsAsync<GetDocumentReadinessValidationException>(() => QueryAsync(
            dbContext,
            new GetVesselReadinessOverviewRequest
            {
                RequiredThrough = requiredThroughOffset is { } offset ? Today.AddDays(offset) : null,
                PageNumber = pageNumber,
                PageSize = pageSize,
            }));

        Assert.Contains(field, exception.Errors.Keys);
    }

    private static Task<GetVesselReadinessOverviewResponse> QueryAsync(
        HarborOpsDbContext dbContext,
        GetVesselReadinessOverviewRequest request) =>
        new GetVesselReadinessOverviewQuery(
                dbContext,
                new FakeDocumentsVesselDirectory(ReadyVessel, NotReadyVessel, UnconfiguredVessel),
                new FixedTimeProvider(DocumentRequirementTestData.FixedUtcNow))
            .GetAsync(request, CancellationToken.None);
}
