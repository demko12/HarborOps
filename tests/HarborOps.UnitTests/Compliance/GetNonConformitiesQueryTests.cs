using HarborOps.Infrastructure.Persistence;
using HarborOps.Modules.Compliance.Application;
using HarborOps.Modules.Compliance.Contracts;
using HarborOps.Modules.Compliance.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.UnitTests.Compliance;

public sealed class GetNonConformitiesQueryTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 8, 20, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task GetAsync_filters_by_severity()
    {
        await using var dbContext = CreateDbContext();
        var inspection = await SeedPscInspectionAsync(dbContext);
        var deadline = new DateOnly(2026, 12, 1);
        await SeedNonConformityAsync(dbContext, NonConformity.SeverityMinor, NonConformity.ClosureStatusOpen, deadline, inspection.Id);
        var major = await SeedNonConformityAsync(dbContext, NonConformity.SeverityMajor, NonConformity.ClosureStatusOpen, deadline, inspection.Id);

        var response = await QueryAsync(dbContext, new GetNonConformitiesRequest { Severity = NonConformity.SeverityMajor });

        Assert.Equal([major.Id], response.Items.Select(item => item.Id));
    }

    [Fact]
    public async Task GetAsync_filters_by_closure_status()
    {
        await using var dbContext = CreateDbContext();
        var inspection = await SeedPscInspectionAsync(dbContext);
        var deadline = new DateOnly(2026, 12, 1);
        await SeedNonConformityAsync(dbContext, NonConformity.SeverityMajor, NonConformity.ClosureStatusOpen, deadline, inspection.Id);
        var closed = await SeedNonConformityAsync(dbContext, NonConformity.SeverityMajor, NonConformity.ClosureStatusClosed, deadline, inspection.Id);

        var response = await QueryAsync(dbContext, new GetNonConformitiesRequest { ClosureStatus = NonConformity.ClosureStatusClosed });

        Assert.Equal([closed.Id], response.Items.Select(item => item.Id));
    }

    [Fact]
    public async Task GetAsync_search_matches_evidence_document_id_partial_text()
    {
        await using var dbContext = CreateDbContext();
        var inspection = await SeedPscInspectionAsync(dbContext);
        var deadline = new DateOnly(2026, 12, 1);
        var target = await SeedNonConformityAsync(dbContext, NonConformity.SeverityMajor, NonConformity.ClosureStatusOpen, deadline, inspection.Id);
        await SeedNonConformityAsync(dbContext, NonConformity.SeverityMajor, NonConformity.ClosureStatusOpen, deadline, inspection.Id);
        var searchTerm = target.EvidenceDocumentId.ToString()[..8];

        var response = await QueryAsync(dbContext, new GetNonConformitiesRequest { Search = searchTerm });

        Assert.Equal([target.Id], response.Items.Select(item => item.Id));
    }

    [Fact]
    public async Task GetAsync_overdue_true_excludes_closed_findings_with_past_deadlines()
    {
        await using var dbContext = CreateDbContext();
        var inspection = await SeedPscInspectionAsync(dbContext);
        var today = DateOnly.FromDateTime(FixedNow.UtcDateTime);
        var overdueOpen = await SeedNonConformityAsync(dbContext, NonConformity.SeverityMajor, NonConformity.ClosureStatusOpen, today.AddDays(-5), inspection.Id);
        var overdueButClosed = await SeedNonConformityAsync(dbContext, NonConformity.SeverityMajor, NonConformity.ClosureStatusClosed, today.AddDays(-5), inspection.Id);
        await SeedNonConformityAsync(dbContext, NonConformity.SeverityMajor, NonConformity.ClosureStatusOpen, today.AddDays(5), inspection.Id);

        var response = await QueryAsync(dbContext, new GetNonConformitiesRequest { Overdue = true });

        Assert.Equal([overdueOpen.Id], response.Items.Select(item => item.Id));
        Assert.DoesNotContain(response.Items, item => item.Id == overdueButClosed.Id);
    }

    [Fact]
    public async Task GetAsync_orders_by_corrective_action_deadline_then_id()
    {
        await using var dbContext = CreateDbContext();
        var inspection = await SeedPscInspectionAsync(dbContext);
        var deadline = new DateOnly(2026, 12, 1);
        var tiedOne = await SeedNonConformityAsync(dbContext, NonConformity.SeverityMajor, NonConformity.ClosureStatusOpen, deadline, inspection.Id);
        var tiedTwo = await SeedNonConformityAsync(dbContext, NonConformity.SeverityMajor, NonConformity.ClosureStatusOpen, deadline, inspection.Id);
        var earlier = await SeedNonConformityAsync(dbContext, NonConformity.SeverityMajor, NonConformity.ClosureStatusOpen, deadline.AddDays(-1), inspection.Id);

        var response = await QueryAsync(dbContext, new GetNonConformitiesRequest { PageSize = 100 });

        var expectedTiedIds = new[] { tiedOne.Id, tiedTwo.Id }.Order().ToArray();
        Assert.Equal(earlier.Id, response.Items[0].Id);
        Assert.Equal(expectedTiedIds, response.Items.Skip(1).Select(item => item.Id));
    }

    [Fact]
    public async Task GetAsync_pages_results_and_reports_total_count_before_paging()
    {
        await using var dbContext = CreateDbContext();
        var inspection = await SeedPscInspectionAsync(dbContext);
        var deadline = new DateOnly(2026, 12, 1);
        for (var i = 0; i < 5; i++)
        {
            await SeedNonConformityAsync(dbContext, NonConformity.SeverityMajor, NonConformity.ClosureStatusOpen, deadline.AddDays(i), inspection.Id);
        }

        var response = await QueryAsync(dbContext, new GetNonConformitiesRequest { PageNumber = 2, PageSize = 2 });

        Assert.Equal(5, response.TotalCount);
        Assert.Equal(2, response.Items.Count);
        Assert.Equal(2, response.PageNumber);
        Assert.Equal(2, response.PageSize);
    }

    [Fact]
    public async Task GetAsync_rejects_an_unknown_severity_value()
    {
        await using var dbContext = CreateDbContext();

        var exception = await Assert.ThrowsAsync<GetNonConformitiesValidationException>(
            () => QueryAsync(dbContext, new GetNonConformitiesRequest { Severity = "Severe" }));

        Assert.Contains(GetNonConformitiesValidator.SeverityField, exception.Errors);
    }

    [Fact]
    public async Task GetAsync_rejects_an_unknown_closure_status_value()
    {
        await using var dbContext = CreateDbContext();

        var exception = await Assert.ThrowsAsync<GetNonConformitiesValidationException>(
            () => QueryAsync(dbContext, new GetNonConformitiesRequest { ClosureStatus = "Pending" }));

        Assert.Contains(GetNonConformitiesValidator.ClosureStatusField, exception.Errors);
    }

    [Fact]
    public async Task GetAsync_rejects_a_page_size_outside_the_allowed_range()
    {
        await using var dbContext = CreateDbContext();

        var exception = await Assert.ThrowsAsync<GetNonConformitiesValidationException>(
            () => QueryAsync(dbContext, new GetNonConformitiesRequest { PageSize = 0 }));

        Assert.Contains(GetNonConformitiesValidator.PageSizeField, exception.Errors);
    }

    [Fact]
    public async Task GetAsync_rejects_a_page_number_below_one()
    {
        await using var dbContext = CreateDbContext();

        var exception = await Assert.ThrowsAsync<GetNonConformitiesValidationException>(
            () => QueryAsync(dbContext, new GetNonConformitiesRequest { PageNumber = 0 }));

        Assert.Contains(GetNonConformitiesValidator.PageNumberField, exception.Errors);
    }

    [Fact]
    public async Task GetSummaryAsync_counts_unresolved_findings_by_severity_and_overdue()
    {
        await using var dbContext = CreateDbContext();
        var inspection = await SeedPscInspectionAsync(dbContext);
        var future = new DateOnly(2026, 12, 1);
        var past = new DateOnly(2026, 8, 1);
        await SeedNonConformityAsync(dbContext, NonConformity.SeverityCritical, NonConformity.ClosureStatusOpen, past, inspection.Id);
        await SeedNonConformityAsync(dbContext, NonConformity.SeverityCritical, NonConformity.ClosureStatusInProgress, future, inspection.Id);
        await SeedNonConformityAsync(dbContext, NonConformity.SeverityMajor, NonConformity.ClosureStatusOpen, future, inspection.Id);
        await SeedNonConformityAsync(dbContext, NonConformity.SeverityMinor, NonConformity.ClosureStatusInProgress, past, inspection.Id);
        await SeedNonConformityAsync(dbContext, NonConformity.SeverityMajor, NonConformity.ClosureStatusClosed, past, inspection.Id);

        var summary = await new GetNonConformitiesQuery(dbContext, new FixedTimeProvider(FixedNow)).GetSummaryAsync(CancellationToken.None);

        Assert.Equal(new GetNonConformitiesSummaryResponse(2, 1, 1, 2), summary);
    }

    private static Task<GetNonConformitiesResponse> QueryAsync(HarborOpsDbContext dbContext, GetNonConformitiesRequest request) =>
        new GetNonConformitiesQuery(dbContext, new FixedTimeProvider(FixedNow)).GetAsync(request, CancellationToken.None);

    private static async Task<PscInspection> SeedPscInspectionAsync(HarborOpsDbContext dbContext)
    {
        var inspection = new PscInspection(Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2026, 7, 1), "Port Authority", 1, false);
        await dbContext.AddAsync(inspection);
        await dbContext.SaveChangesAsync();
        return inspection;
    }

    private static async Task<NonConformity> SeedNonConformityAsync(
        HarborOpsDbContext dbContext, string severity, string closureStatus, DateOnly deadline, int pscId)
    {
        var nonConformity = new NonConformity(Guid.NewGuid(), severity, deadline, closureStatus, pscId: pscId);
        await dbContext.AddAsync(nonConformity);
        await dbContext.SaveChangesAsync();
        return nonConformity;
    }

    private static HarborOpsDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<HarborOpsDbContext>()
            .UseInMemoryDatabase($"HarborOpsUnitTests-{Guid.NewGuid()}")
            .Options;

        return new HarborOpsDbContext(options);
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow()
        {
            return utcNow;
        }
    }
}
