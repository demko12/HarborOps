using HarborOps.Infrastructure.Persistence;
using HarborOps.Modules.Maintenance.Application;
using HarborOps.Modules.Maintenance.Contracts;
using HarborOps.Modules.Maintenance.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.UnitTests.Maintenance;

public sealed class GetDefectsQueryTests
{
    [Fact]
    public async Task GetDefectsAsync_returns_everything_when_no_filters_are_supplied()
    {
        await using var dbContext = CreateDbContext();
        var vesselId = Guid.NewGuid();
        var baseTime = new DateTime(2026, 8, 1, 8, 0, 0, DateTimeKind.Utc);
        await SeedAsync(
            dbContext,
            CreateReported(vesselId, DefectSeverity.Low, baseTime, "First"),
            CreateReported(vesselId, DefectSeverity.Medium, baseTime.AddHours(1), "Second"),
            CreateReported(vesselId, DefectSeverity.High, baseTime.AddHours(2), "Third"));

        var response = await CreateQuery(dbContext).GetDefectsAsync(new GetDefectsRequest(), CancellationToken.None);

        Assert.Equal(3, response.TotalCount);
        Assert.Equal(3, response.Items.Count);
    }

    [Fact]
    public async Task GetDefectsAsync_filters_by_status()
    {
        await using var dbContext = CreateDbContext();
        var vesselId = Guid.NewGuid();
        var baseTime = new DateTime(2026, 8, 1, 8, 0, 0, DateTimeKind.Utc);
        var reported = CreateReported(vesselId, DefectSeverity.Medium, baseTime, "Reported one");
        var triaged = CreateReported(vesselId, DefectSeverity.Medium, baseTime.AddHours(1), "Triaged one");
        triaged.Triage(DefectSeverity.Medium, WorkOrderPriority.Medium, null, Guid.NewGuid(), baseTime.AddHours(2));
        await SeedAsync(dbContext, reported, triaged);

        var response = await CreateQuery(dbContext).GetDefectsAsync(
            new GetDefectsRequest { Status = "Triaged" },
            CancellationToken.None);

        Assert.Equal(1, response.TotalCount);
        Assert.Equal(triaged.Id, Assert.Single(response.Items).Id);
    }

    [Fact]
    public async Task GetDefectsAsync_filters_by_vessel_id()
    {
        await using var dbContext = CreateDbContext();
        var targetVesselId = Guid.NewGuid();
        var otherVesselId = Guid.NewGuid();
        var baseTime = new DateTime(2026, 8, 1, 8, 0, 0, DateTimeKind.Utc);
        var target = CreateReported(targetVesselId, DefectSeverity.Medium, baseTime, "Target");
        var other = CreateReported(otherVesselId, DefectSeverity.Medium, baseTime.AddHours(1), "Other");
        await SeedAsync(dbContext, target, other);

        var response = await CreateQuery(dbContext).GetDefectsAsync(
            new GetDefectsRequest { VesselId = targetVesselId },
            CancellationToken.None);

        Assert.Equal(1, response.TotalCount);
        Assert.Equal(target.Id, Assert.Single(response.Items).Id);
    }

    [Fact]
    public async Task GetDefectsAsync_severity_filter_matches_an_untriaged_defects_reported_severity()
    {
        await using var dbContext = CreateDbContext();
        var vesselId = Guid.NewGuid();
        var baseTime = new DateTime(2026, 8, 1, 8, 0, 0, DateTimeKind.Utc);
        var defect = CreateReported(vesselId, DefectSeverity.Medium, baseTime, "Untriaged");
        await SeedAsync(dbContext, defect);

        var response = await CreateQuery(dbContext).GetDefectsAsync(
            new GetDefectsRequest { Severity = "Medium" },
            CancellationToken.None);

        Assert.Equal(defect.Id, Assert.Single(response.Items).Id);
    }

    [Fact]
    public async Task GetDefectsAsync_severity_filter_matches_the_confirmed_severity_after_triage_revises_it()
    {
        // The exact scenario the earlier correction targeted: a defect reported as Medium but
        // confirmed as Critical at triage must be findable under its revised, effective severity.
        await using var dbContext = CreateDbContext();
        var vesselId = Guid.NewGuid();
        var baseTime = new DateTime(2026, 8, 1, 8, 0, 0, DateTimeKind.Utc);
        var defect = CreateReported(vesselId, DefectSeverity.Medium, baseTime, "Escalated");
        defect.Triage(DefectSeverity.Critical, WorkOrderPriority.Critical, null, Guid.NewGuid(), baseTime.AddHours(1));
        await SeedAsync(dbContext, defect);

        var response = await CreateQuery(dbContext).GetDefectsAsync(
            new GetDefectsRequest { Severity = "Critical" },
            CancellationToken.None);

        Assert.Equal(defect.Id, Assert.Single(response.Items).Id);
    }

    [Fact]
    public async Task GetDefectsAsync_severity_filter_no_longer_matches_the_superseded_reported_severity_after_triage()
    {
        await using var dbContext = CreateDbContext();
        var vesselId = Guid.NewGuid();
        var baseTime = new DateTime(2026, 8, 1, 8, 0, 0, DateTimeKind.Utc);
        var defect = CreateReported(vesselId, DefectSeverity.Medium, baseTime, "Escalated");
        defect.Triage(DefectSeverity.Critical, WorkOrderPriority.Critical, null, Guid.NewGuid(), baseTime.AddHours(1));
        await SeedAsync(dbContext, defect);

        var response = await CreateQuery(dbContext).GetDefectsAsync(
            new GetDefectsRequest { Severity = "Medium" },
            CancellationToken.None);

        Assert.Empty(response.Items);
        Assert.Equal(0, response.TotalCount);
    }

    [Fact]
    public async Task GetDefectsAsync_combines_status_severity_and_vessel_filters_with_and_semantics()
    {
        await using var dbContext = CreateDbContext();
        var vesselA = Guid.NewGuid();
        var vesselB = Guid.NewGuid();
        var baseTime = new DateTime(2026, 8, 1, 8, 0, 0, DateTimeKind.Utc);

        var match = CreateReported(vesselA, DefectSeverity.Medium, baseTime, "Match");
        match.Triage(DefectSeverity.Critical, WorkOrderPriority.Critical, null, Guid.NewGuid(), baseTime.AddHours(1));

        var wrongVessel = CreateReported(vesselB, DefectSeverity.Medium, baseTime.AddHours(2), "Wrong vessel");
        wrongVessel.Triage(DefectSeverity.Critical, WorkOrderPriority.Critical, null, Guid.NewGuid(), baseTime.AddHours(3));

        var wrongStatus = CreateReported(vesselA, DefectSeverity.Critical, baseTime.AddHours(4), "Wrong status");

        var wrongSeverity = CreateReported(vesselA, DefectSeverity.Medium, baseTime.AddHours(5), "Wrong severity");
        wrongSeverity.Triage(DefectSeverity.Medium, WorkOrderPriority.Medium, null, Guid.NewGuid(), baseTime.AddHours(6));

        await SeedAsync(dbContext, match, wrongVessel, wrongStatus, wrongSeverity);

        var response = await CreateQuery(dbContext).GetDefectsAsync(
            new GetDefectsRequest { Status = "Triaged", Severity = "Critical", VesselId = vesselA },
            CancellationToken.None);

        Assert.Equal(match.Id, Assert.Single(response.Items).Id);
    }

    [Fact]
    public async Task GetDefectsAsync_pages_a_larger_result_set()
    {
        await using var dbContext = CreateDbContext();
        var vesselId = Guid.NewGuid();
        var baseTime = new DateTime(2026, 8, 1, 8, 0, 0, DateTimeKind.Utc);
        var defects = new VesselDefect[30];
        for (var index = 1; index <= 30; index++)
        {
            defects[index - 1] = CreateReported(vesselId, DefectSeverity.Medium, baseTime.AddHours(index), $"Defect {index:D2}");
        }
        await SeedAsync(dbContext, defects);

        var firstPage = await CreateQuery(dbContext).GetDefectsAsync(new GetDefectsRequest(), CancellationToken.None);

        Assert.Equal(1, firstPage.PageNumber);
        Assert.Equal(25, firstPage.PageSize);
        Assert.Equal(30, firstPage.TotalCount);
        Assert.Equal(25, firstPage.Items.Count);
        Assert.Equal("Defect 30", firstPage.Items[0].Title);
        Assert.Equal("Defect 06", firstPage.Items[^1].Title);

        var secondPage = await CreateQuery(dbContext).GetDefectsAsync(
            new GetDefectsRequest { PageNumber = 2, PageSize = 25 },
            CancellationToken.None);

        Assert.Equal(30, secondPage.TotalCount);
        Assert.Equal(5, secondPage.Items.Count);
        Assert.Equal("Defect 05", secondPage.Items[0].Title);
        Assert.Equal("Defect 01", secondPage.Items[^1].Title);
    }

    [Fact]
    public async Task GetDefectsAsync_a_page_beyond_the_available_data_returns_empty_items_with_the_correct_total_count()
    {
        await using var dbContext = CreateDbContext();
        var vesselId = Guid.NewGuid();
        var baseTime = new DateTime(2026, 8, 1, 8, 0, 0, DateTimeKind.Utc);
        await SeedAsync(
            dbContext,
            CreateReported(vesselId, DefectSeverity.Medium, baseTime, "First"),
            CreateReported(vesselId, DefectSeverity.Medium, baseTime.AddHours(1), "Second"),
            CreateReported(vesselId, DefectSeverity.Medium, baseTime.AddHours(2), "Third"));

        var response = await CreateQuery(dbContext).GetDefectsAsync(
            new GetDefectsRequest { PageNumber = 5, PageSize = 25 },
            CancellationToken.None);

        Assert.Empty(response.Items);
        Assert.Equal(3, response.TotalCount);
        Assert.Equal(5, response.PageNumber);
    }

    [Fact]
    public async Task GetDefectsAsync_orders_by_reported_at_utc_descending()
    {
        await using var dbContext = CreateDbContext();
        var vesselId = Guid.NewGuid();
        var baseTime = new DateTime(2026, 8, 1, 8, 0, 0, DateTimeKind.Utc);
        var oldest = CreateReported(vesselId, DefectSeverity.Medium, baseTime, "Oldest");
        var middle = CreateReported(vesselId, DefectSeverity.Medium, baseTime.AddHours(1), "Middle");
        var newest = CreateReported(vesselId, DefectSeverity.Medium, baseTime.AddHours(2), "Newest");
        await SeedAsync(dbContext, middle, oldest, newest);

        var response = await CreateQuery(dbContext).GetDefectsAsync(new GetDefectsRequest(), CancellationToken.None);

        Assert.Equal([newest.Id, middle.Id, oldest.Id], response.Items.Select(item => item.Id));
    }

    [Fact]
    public async Task GetDefectsAsync_breaks_ties_by_id_ascending_and_is_deterministic_across_runs()
    {
        await using var dbContext = CreateDbContext();
        var vesselId = Guid.NewGuid();
        var sameReportedAtUtc = new DateTime(2026, 8, 5, 8, 0, 0, DateTimeKind.Utc);
        var first = CreateReported(vesselId, DefectSeverity.Medium, sameReportedAtUtc, "Tied A");
        var second = CreateReported(vesselId, DefectSeverity.Medium, sameReportedAtUtc, "Tied B");
        await SeedAsync(dbContext, first, second);

        var expectedOrder = new[] { first.Id, second.Id }.OrderBy(id => id).ToArray();
        var query = CreateQuery(dbContext);

        var firstRun = await query.GetDefectsAsync(new GetDefectsRequest(), CancellationToken.None);
        var secondRun = await query.GetDefectsAsync(new GetDefectsRequest(), CancellationToken.None);

        Assert.Equal(expectedOrder, firstRun.Items.Select(item => item.Id).ToArray());
        Assert.Equal(expectedOrder, secondRun.Items.Select(item => item.Id).ToArray());
    }

    [Fact]
    public async Task GetDefectsAsync_does_not_track_returned_entities()
    {
        await using var dbContext = CreateDbContext();
        var vesselId = Guid.NewGuid();
        await SeedAsync(dbContext, CreateReported(vesselId, DefectSeverity.Medium, new DateTime(2026, 8, 1, 8, 0, 0, DateTimeKind.Utc), "First"));
        dbContext.ChangeTracker.Clear();

        await CreateQuery(dbContext).GetDefectsAsync(new GetDefectsRequest(), CancellationToken.None);

        Assert.Empty(dbContext.ChangeTracker.Entries());
    }

    [Fact]
    public async Task GetDefectsAsync_rejects_an_invalid_status()
    {
        await using var dbContext = CreateDbContext();

        var exception = await Assert.ThrowsAsync<GetDefectsValidationException>(
            () => CreateQuery(dbContext).GetDefectsAsync(
                new GetDefectsRequest { Status = "Unknown" },
                CancellationToken.None));

        Assert.Contains(GetDefectsValidator.StatusField, exception.Errors);
    }

    private static VesselDefect CreateReported(Guid vesselId, DefectSeverity reportedSeverity, DateTime reportedAtUtc, string title)
    {
        return VesselDefect.Register(
            vesselId,
            null,
            title,
            null,
            reportedSeverity,
            reportedAtUtc.AddHours(-1),
            Guid.NewGuid(),
            reportedAtUtc);
    }

    private static async Task SeedAsync(HarborOpsDbContext dbContext, params VesselDefect[] defects)
    {
        await dbContext.Set<VesselDefect>().AddRangeAsync(defects);
        await dbContext.SaveChangesAsync();
    }

    private static GetDefectsQuery CreateQuery(HarborOpsDbContext dbContext)
    {
        return new GetDefectsQuery(dbContext);
    }

    private static HarborOpsDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<HarborOpsDbContext>()
            .UseInMemoryDatabase($"HarborOpsUnitTests-{Guid.NewGuid()}")
            .Options;

        return new HarborOpsDbContext(options);
    }
}
