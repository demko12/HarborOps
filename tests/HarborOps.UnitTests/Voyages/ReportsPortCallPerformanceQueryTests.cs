using HarborOps.Infrastructure.Persistence;
using HarborOps.Modules.Voyages.Application;
using HarborOps.Modules.Voyages.Contracts;
using HarborOps.Modules.Voyages.Domain;
using static HarborOps.UnitTests.Voyages.PortCallPerformanceScenario;

namespace HarborOps.UnitTests.Voyages;

public sealed class ReportsPortCallPerformanceQueryTests
{
    private static readonly DateOnly September1 = new(2026, 9, 1);
    private static readonly DateOnly September30 = new(2026, 9, 30);

    [Fact]
    public async Task Known_statements_produce_exact_row_metrics()
    {
        await using var dbContext = CreateDbContext();
        var fixture = await SeedKnownFixtureAsync(dbContext);

        var result = await Query(dbContext).SearchAsync(Request(September1, September30), CancellationToken.None);

        Assert.Equal(
            [fixture.A.Id, fixture.B.Id, fixture.D.Id, fixture.C.Id, fixture.E.Id],
            result.Items.Select(row => row.VoyageStopId));

        var a = result.Items[0];
        Assert.Equal(Measured(720), a.Turnaround);
        Assert.Equal(Measured(90), a.ArrivalToAllFast);
        Assert.Equal(Measured(480), a.CargoElapsed);
        Assert.Equal(Measured(120), a.CargoCompletedToDeparture);
        Assert.Equal(1, a.RecordedDelayCount);
        Assert.Equal(45, a.RecordedDelayMinutes);
        Assert.Equal("V-2026-014", a.VoyageNumber);
        Assert.Equal(2, a.StopOrder);
        Assert.Equal("Rotterdam", a.PortName);
        Assert.Equal("NLRTM", a.PortUnLocode);
        Assert.Equal("Euromax 3", a.BerthName);
        Assert.False(a.UsesLiveValues);
        Assert.Equal(ReportsPortCallPerformanceOptions.Finalized, a.StatementAvailability);
        Assert.Equal(fixture.StatementA.Id, a.StatementId);
        Assert.Equal(Utc(9, 4, 9, 12), a.FinalizedAtUtc);
        Assert.Equal(Utc(9, 2, 20), a.ActualArrivalUtc);
        Assert.Equal(Utc(9, 3, 8), a.ActualDepartureUtc);

        var b = result.Items[1];
        Assert.Equal(Measured(1110), b.Turnaround);
        Assert.Equal(Measured(135), b.ArrivalToAllFast);
        Assert.Equal(
            new ReportsPortCallMetric(
                null,
                ReportsPortCallPerformanceOptions.QualityAmbiguous,
                PortCallActivityType.CargoStarted),
            b.CargoElapsed);
        Assert.Equal(Measured(180), b.CargoCompletedToDeparture);
        Assert.Equal(0, b.RecordedDelayCount);
        Assert.Equal(0, b.RecordedDelayMinutes);

        var d = result.Items[2];
        Assert.Equal(Measured(1320), d.Turnaround);
        Assert.Equal(Measured(60), d.ArrivalToAllFast);
        Assert.Equal(
            new ReportsPortCallMetric(null, ReportsPortCallPerformanceOptions.QualityInvalidChronology, null),
            d.CargoElapsed);
        Assert.Equal(Measured(240), d.CargoCompletedToDeparture);

        var c = result.Items[3];
        Assert.Equal(Measured(540), c.Turnaround);
        Assert.Equal(
            new ReportsPortCallMetric(
                null,
                ReportsPortCallPerformanceOptions.QualityMissing,
                PortCallActivityType.AllFast),
            c.ArrivalToAllFast);
        Assert.Equal(Measured(420), c.CargoElapsed);
        Assert.Equal(Measured(0), c.CargoCompletedToDeparture);
        Assert.Equal(2, c.RecordedDelayCount);
        Assert.Equal(90, c.RecordedDelayMinutes);
        Assert.Equal("Waalhaven 2", c.BerthName);

        var e = result.Items[4];
        Assert.Equal(ReportsPortCallPerformanceOptions.NotFinalized, e.StatementAvailability);
        Assert.Null(e.StatementId);
        Assert.Null(e.FinalizedAtUtc);
        Assert.Null(e.Turnaround);
        Assert.Null(e.ArrivalToAllFast);
        Assert.Null(e.CargoElapsed);
        Assert.Null(e.CargoCompletedToDeparture);
        Assert.Null(e.RecordedDelayCount);
        Assert.Null(e.RecordedDelayMinutes);
        Assert.True(e.UsesLiveValues);
        Assert.Equal("Hamburg", e.PortName);
        Assert.Null(e.BerthName);
        Assert.Equal(Utc(9, 18, 6), e.ActualDepartureUtc);
    }

    [Fact]
    public async Task Summaries_use_exact_means_and_exclude_unavailable_values_from_each_denominator()
    {
        await using var dbContext = CreateDbContext();
        var fixture = await SeedKnownFixtureAsync(dbContext);

        var result = await Query(dbContext).SearchAsync(Request(September1, September30), CancellationToken.None);

        Assert.Equal(
            new ReportsPortCallPerformanceSummary(
                5,
                4,
                1,
                new ReportsPortCallMetricSummary(922.5m, 4, 0, 0, 0),
                new ReportsPortCallMetricSummary(95m, 3, 1, 0, 0),
                new ReportsPortCallMetricSummary(450m, 2, 0, 1, 1),
                new ReportsPortCallMetricSummary(135m, 4, 0, 0, 0),
                3,
                135),
            result.Summary);

        Assert.Equal(
            [
                new ReportsPortCallPerformancePortSummary(
                    fixture.Hamburg.Id,
                    "Hamburg",
                    "DEHAM",
                    new ReportsPortCallPerformanceSummary(
                        2,
                        1,
                        1,
                        new ReportsPortCallMetricSummary(1320m, 1, 0, 0, 0),
                        new ReportsPortCallMetricSummary(60m, 1, 0, 0, 0),
                        new ReportsPortCallMetricSummary(null, 0, 0, 0, 1),
                        new ReportsPortCallMetricSummary(240m, 1, 0, 0, 0),
                        0,
                        0)),
                new ReportsPortCallPerformancePortSummary(
                    fixture.Rotterdam.Id,
                    "Rotterdam",
                    "NLRTM",
                    new ReportsPortCallPerformanceSummary(
                        3,
                        3,
                        0,
                        new ReportsPortCallMetricSummary(790m, 3, 0, 0, 0),
                        new ReportsPortCallMetricSummary(112.5m, 2, 1, 0, 0),
                        new ReportsPortCallMetricSummary(450m, 2, 0, 1, 0),
                        new ReportsPortCallMetricSummary(100m, 3, 0, 0, 0),
                        3,
                        135)),
            ],
            result.Ports);
    }

    [Fact]
    public async Task Stops_without_a_finalized_statement_are_counted_separately_and_never_as_zero()
    {
        await using var dbContext = CreateDbContext();
        var port = await SeedPortAsync(dbContext, "Bar", "MEBAR");
        var voyage = await SeedVoyageAsync(dbContext, "V-2026-030");
        var withoutStatement = await SeedStopAsync(dbContext, voyage, port, 1, Utc(9, 5, 8), Utc(9, 5, 20));
        var withDraft = await SeedStopAsync(dbContext, voyage, port, 2, Utc(9, 6, 8), Utc(9, 6, 20));
        await SeedDraftAsync(dbContext, voyage, withDraft);
        await SeedStopAsync(dbContext, voyage, port, 3, Utc(9, 7, 8), null);

        var result = await Query(dbContext).SearchAsync(Request(September1, September30), CancellationToken.None);

        Assert.Equal([withoutStatement.Id, withDraft.Id], result.Items.Select(row => row.VoyageStopId));
        Assert.All(result.Items, row =>
        {
            Assert.Equal(ReportsPortCallPerformanceOptions.NotFinalized, row.StatementAvailability);
            Assert.Null(row.StatementId);
            Assert.Null(row.Turnaround);
            Assert.Null(row.RecordedDelayMinutes);
            Assert.True(row.UsesLiveValues);
            Assert.Equal("Bar", row.PortName);
            Assert.Equal("V-2026-030", row.VoyageNumber);
        });
        Assert.Equal(2, result.Summary.StopCount);
        Assert.Equal(0, result.Summary.FinalizedCount);
        Assert.Equal(2, result.Summary.NotFinalizedCount);
        Assert.Equal(new ReportsPortCallMetricSummary(null, 0, 0, 0, 0), result.Summary.Turnaround);
    }

    [Fact]
    public async Task Changing_live_data_after_finalization_does_not_rewrite_finalized_values()
    {
        await using var dbContext = CreateDbContext();
        var fixture = await SeedKnownFixtureAsync(dbContext);
        var before = await Query(dbContext).SearchAsync(Request(September1, September30), CancellationToken.None);

        dbContext.Entry(fixture.Rotterdam).Property(port => port.Name).CurrentValue = "Rotterdam Maasvlakte";
        dbContext.Entry(fixture.VoyageA).Property(voyage => voyage.VoyageNumber).CurrentValue = "V-RENUMBERED";
        dbContext.Entry(fixture.A).Property(stop => stop.Ata).CurrentValue = Utc(10, 9, 0);
        dbContext.Entry(fixture.A).Property(stop => stop.Atd).CurrentValue = Utc(10, 10, 0);
        await dbContext.SaveChangesAsync();
        dbContext.ChangeTracker.Clear();

        var after = await Query(dbContext).SearchAsync(Request(September1, September30), CancellationToken.None);

        Assert.Equal(before.Items, after.Items);
        Assert.Equal(before.Summary, after.Summary);
        Assert.Equal(before.Ports, after.Ports);
        Assert.Equal("Rotterdam", after.Items[0].PortName);
        Assert.Equal("V-2026-014", after.Items[0].VoyageNumber);
    }

    [Fact]
    public async Task A_port_group_is_labelled_by_its_most_recently_finalized_snapshot()
    {
        await using var dbContext = CreateDbContext();
        var port = await SeedPortAsync(dbContext, "Old Harbour", "MEOLD");
        var voyage = await SeedVoyageAsync(dbContext, "V-2026-040");
        var first = await SeedStopAsync(dbContext, voyage, port, 1, Utc(9, 2, 8), Utc(9, 2, 20));
        var second = await SeedStopAsync(dbContext, voyage, port, 2, Utc(9, 3, 8), Utc(9, 3, 20));
        await FinalizeAsync(dbContext, voyage, second, port, null, Utc(9, 4, 8));
        dbContext.Entry(port).Property(candidate => candidate.Name).CurrentValue = "New Harbour";
        await dbContext.SaveChangesAsync();
        await FinalizeAsync(dbContext, voyage, first, port, null, Utc(9, 5, 8));

        var result = await Query(dbContext).SearchAsync(Request(September1, September30), CancellationToken.None);

        Assert.Equal(["New Harbour", "Old Harbour"], result.Items.Select(row => row.PortName));
        var group = Assert.Single(result.Ports);
        Assert.Equal(port.Id, group.PortId);
        Assert.Equal("New Harbour", group.PortName);
        Assert.Equal(2, group.Summary.StopCount);
    }

    [Fact]
    public async Task Corrected_and_voided_activities_and_delays_are_read_as_captured_by_finalization()
    {
        await using var dbContext = PortCallLogScenario.CreateDbContext();
        var (voyageId, stopId) = await PortCallLogScenario.SeedStopAsync(dbContext);
        var arrival = PortCallLogScenario.ActualArrivalUtc;

        await PortCallLogScenario.RecordAsync(
            dbContext, voyageId, stopId, PortCallActivityType.AllFast, PortCallLogScenario.At(arrival.AddMinutes(30), 2));
        var duplicateAllFast = await PortCallLogScenario.RecordAsync(
            dbContext, voyageId, stopId, PortCallActivityType.AllFast, PortCallLogScenario.At(arrival.AddHours(1)));
        await new VoidPortCallActivityService(dbContext, PortCallLogScenario.Clock()).VoidAsync(
            voyageId,
            stopId,
            duplicateAllFast.ActivityId,
            new VoidPortCallActivityRequest { Reason = "Recorded twice", Version = duplicateAllFast.Version },
            PortCallLogScenario.ActorId,
            PortCallLogScenario.ActorName,
            CancellationToken.None);
        dbContext.ChangeTracker.Clear();

        var cargoStarted = await PortCallLogScenario.RecordAsync(
            dbContext, voyageId, stopId, PortCallActivityType.CargoStarted, PortCallLogScenario.At(arrival.AddHours(1)));
        await new CorrectPortCallActivityService(dbContext, PortCallLogScenario.Clock()).CorrectAsync(
            voyageId,
            stopId,
            cargoStarted.ActivityId,
            new CorrectPortCallActivityRequest
            {
                ActivityType = PortCallActivityType.CargoStarted,
                OccurredAt = PortCallLogScenario.At(arrival.AddHours(2)),
                Reason = "Wrong start time",
                Version = cargoStarted.Version,
            },
            PortCallLogScenario.ActorId,
            PortCallLogScenario.ActorName,
            CancellationToken.None);
        dbContext.ChangeTracker.Clear();

        await PortCallLogScenario.RecordAsync(
            dbContext, voyageId, stopId, PortCallActivityType.CargoCompleted, PortCallLogScenario.At(arrival.AddHours(7)));
        await PortCallLogScenario.SeedDelayAsync(dbContext, voyageId, stopId, "Tug", arrival.AddHours(3), 45);
        await dbContext.Set<Delay>().AddAsync(Delay.Create(voyageId, "Weather", 120, arrival.AddHours(4)));
        await dbContext.SaveChangesAsync();
        dbContext.ChangeTracker.Clear();

        await PortCallLogScenario.DepartAsync(dbContext, stopId);
        var preview = await PortCallLogScenario.CreateStatementAsync(dbContext, voyageId, stopId);
        await PortCallLogScenario.FinalizeAsync(dbContext, voyageId, stopId, preview);

        var result = await Query(dbContext).SearchAsync(Request(September1, September1), CancellationToken.None);

        var row = Assert.Single(result.Items);
        Assert.Equal(ReportsPortCallPerformanceOptions.Finalized, row.StatementAvailability);
        Assert.Equal(Measured(480), row.Turnaround);
        Assert.Equal(Measured(30), row.ArrivalToAllFast);
        Assert.Equal(Measured(300), row.CargoElapsed);
        Assert.Equal(Measured(60), row.CargoCompletedToDeparture);
        Assert.Equal(1, row.RecordedDelayCount);
        Assert.Equal(45, row.RecordedDelayMinutes);
    }

    [Fact]
    public async Task The_departure_range_covers_whole_utc_days_for_finalized_and_live_departures()
    {
        await using var dbContext = CreateDbContext();
        var port = await SeedPortAsync(dbContext, "Bar", "MEBAR");
        var voyage = await SeedVoyageAsync(dbContext, "V-2026-050");
        var beforeRange = Utc(8, 31, 23, 59, 59);
        var rangeStart = Utc(9, 1, 0);
        var rangeEnd = Utc(9, 30, 23, 59, 59);
        var afterRange = Utc(10, 1, 0);
        await SeedStopAsync(dbContext, voyage, port, 1, beforeRange.AddHours(-6), beforeRange);
        var first = await SeedStopAsync(dbContext, voyage, port, 2, rangeStart.AddHours(-6), rangeStart);
        await FinalizeAsync(dbContext, voyage, first, port, null, Utc(9, 2, 0));
        var last = await SeedStopAsync(dbContext, voyage, port, 3, rangeEnd.AddHours(-6), rangeEnd);
        var excluded = await SeedStopAsync(dbContext, voyage, port, 4, afterRange.AddHours(-6), afterRange);
        await FinalizeAsync(dbContext, voyage, excluded, port, null, Utc(10, 2, 0));

        var result = await Query(dbContext).SearchAsync(Request(September1, September30), CancellationToken.None);

        Assert.Equal([first.Id, last.Id], result.Items.Select(row => row.VoyageStopId));
        Assert.Equal(2, result.TotalCount);
    }

    [Fact]
    public async Task Filters_narrow_rows_and_summaries_together()
    {
        await using var dbContext = CreateDbContext();
        var fixture = await SeedKnownFixtureAsync(dbContext);
        var query = Query(dbContext);
        var all = Request(September1, September30);

        var byVoyage = await query.SearchAsync(all with { VoyageId = fixture.VoyageB.Id }, CancellationToken.None);
        var byVessel = await query.SearchAsync(all with { VesselId = fixture.HamburgVesselId }, CancellationToken.None);
        var byPort = await query.SearchAsync(all with { PortId = fixture.Hamburg.Id }, CancellationToken.None);
        var finalized = await query.SearchAsync(
            all with { StatementAvailability = ReportsPortCallPerformanceOptions.Finalized },
            CancellationToken.None);
        var notFinalized = await query.SearchAsync(
            all with { StatementAvailability = ReportsPortCallPerformanceOptions.NotFinalized },
            CancellationToken.None);

        Assert.Equal([fixture.B.Id], byVoyage.Items.Select(row => row.VoyageStopId));
        Assert.Equal([fixture.D.Id, fixture.E.Id], byVessel.Items.Select(row => row.VoyageStopId));
        Assert.Equal([fixture.D.Id, fixture.E.Id], byPort.Items.Select(row => row.VoyageStopId));
        Assert.Equal(2, byPort.Summary.StopCount);
        Assert.Equal(1, byPort.Summary.NotFinalizedCount);
        Assert.Equal(
            [fixture.A.Id, fixture.B.Id, fixture.D.Id, fixture.C.Id],
            finalized.Items.Select(row => row.VoyageStopId));
        Assert.Equal(0, finalized.Summary.NotFinalizedCount);
        Assert.Equal([fixture.E.Id], notFinalized.Items.Select(row => row.VoyageStopId));
        Assert.Equal(1, notFinalized.TotalCount);
        Assert.Equal(0, notFinalized.Summary.Turnaround.SampleCount);
    }

    [Fact]
    public async Task Paging_is_deterministic_and_summaries_always_cover_the_full_filtered_set()
    {
        await using var dbContext = CreateDbContext();
        var fixture = await SeedKnownFixtureAsync(dbContext);
        var query = Query(dbContext);
        var full = await query.SearchAsync(Request(September1, September30), CancellationToken.None);

        var pages = new List<ReportsPortCallPerformanceResult>();
        for (var pageNumber = 1; pageNumber <= 4; pageNumber++)
        {
            pages.Add(await query.SearchAsync(
                Request(September1, September30) with { PageNumber = pageNumber, PageSize = 2 },
                CancellationToken.None));
        }

        Assert.Equal([fixture.A.Id, fixture.B.Id], pages[0].Items.Select(row => row.VoyageStopId));
        Assert.Equal([fixture.D.Id, fixture.C.Id], pages[1].Items.Select(row => row.VoyageStopId));
        Assert.Equal([fixture.E.Id], pages[2].Items.Select(row => row.VoyageStopId));
        Assert.Empty(pages[3].Items);
        Assert.All(pages, page =>
        {
            Assert.Equal(5, page.TotalCount);
            Assert.Equal(2, page.PageSize);
            Assert.Equal(full.Summary, page.Summary);
            Assert.Equal(full.Ports, page.Ports);
        });
    }

    [Theory]
    [InlineData(ReportsPortCallPerformanceOptions.SortByCargoElapsed, ReportsPortCallPerformanceOptions.Descending, "ACBDE")]
    [InlineData(ReportsPortCallPerformanceOptions.SortByCargoElapsed, ReportsPortCallPerformanceOptions.Ascending, "CABDE")]
    [InlineData(ReportsPortCallPerformanceOptions.SortByTurnaround, ReportsPortCallPerformanceOptions.Descending, "DBACE")]
    [InlineData(ReportsPortCallPerformanceOptions.SortByArrivalToAllFast, ReportsPortCallPerformanceOptions.Ascending, "DABCE")]
    [InlineData(ReportsPortCallPerformanceOptions.SortByCargoCompletedToDeparture, ReportsPortCallPerformanceOptions.Ascending, "CABDE")]
    [InlineData(ReportsPortCallPerformanceOptions.SortByRecordedDelay, ReportsPortCallPerformanceOptions.Descending, "CABDE")]
    [InlineData(ReportsPortCallPerformanceOptions.SortByPort, ReportsPortCallPerformanceOptions.Ascending, "DEABC")]
    [InlineData(ReportsPortCallPerformanceOptions.SortByPort, ReportsPortCallPerformanceOptions.Descending, "ABCDE")]
    [InlineData(ReportsPortCallPerformanceOptions.SortByVoyage, ReportsPortCallPerformanceOptions.Ascending, "ABDCE")]
    [InlineData(ReportsPortCallPerformanceOptions.SortByActualDeparture, ReportsPortCallPerformanceOptions.Descending, "ECDBA")]
    public async Task Sorting_puts_unavailable_values_last_and_breaks_ties_by_departure(
        string sortBy,
        string direction,
        string expectedOrder)
    {
        await using var dbContext = CreateDbContext();
        var fixture = await SeedKnownFixtureAsync(dbContext);
        var names = new Dictionary<Guid, char>
        {
            [fixture.A.Id] = 'A',
            [fixture.B.Id] = 'B',
            [fixture.C.Id] = 'C',
            [fixture.D.Id] = 'D',
            [fixture.E.Id] = 'E',
        };

        var result = await Query(dbContext).SearchAsync(
            Request(September1, September30) with { SortBy = sortBy, SortDirection = direction },
            CancellationToken.None);

        Assert.Equal(expectedOrder, new string(result.Items.Select(row => names[row.VoyageStopId]).ToArray()));
    }

    [Fact]
    public async Task Rows_with_identical_sort_values_are_ordered_by_stop_id()
    {
        await using var dbContext = CreateDbContext();
        var port = await SeedPortAsync(dbContext, "Bar", "MEBAR");
        var voyage = await SeedVoyageAsync(dbContext, "V-2026-060");
        var stops = new List<Guid>();
        for (var order = 1; order <= 4; order++)
        {
            var stop = await SeedStopAsync(dbContext, voyage, port, order, Utc(9, 10, 8), Utc(9, 10, 20));
            stops.Add(stop.Id);
        }

        var result = await Query(dbContext).SearchAsync(Request(September1, September30), CancellationToken.None);

        Assert.Equal(stops.Order(), result.Items.Select(row => row.VoyageStopId));
    }

    [Fact]
    public async Task An_empty_result_has_zero_counts_and_no_means()
    {
        await using var dbContext = CreateDbContext();
        await SeedKnownFixtureAsync(dbContext);

        var result = await Query(dbContext).SearchAsync(
            Request(new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 30)),
            CancellationToken.None);

        var empty = new ReportsPortCallMetricSummary(null, 0, 0, 0, 0);
        Assert.Empty(result.Items);
        Assert.Empty(result.Ports);
        Assert.Equal(0, result.TotalCount);
        Assert.False(result.StopLimitExceeded);
        Assert.Equal(
            new ReportsPortCallPerformanceSummary(0, 0, 0, empty, empty, empty, empty, 0, 0),
            result.Summary);
    }

    [Theory]
    [InlineData(4, true)]
    [InlineData(5, false)]
    public async Task A_filtered_set_above_the_stop_limit_is_reported_instead_of_being_truncated(
        int maxStops,
        bool exceeded)
    {
        await using var dbContext = CreateDbContext();
        await SeedKnownFixtureAsync(dbContext);

        var result = await Query(dbContext, maxStops).SearchAsync(
            Request(September1, September30),
            CancellationToken.None);

        Assert.Equal(exceeded, result.StopLimitExceeded);
        Assert.Equal(5, result.TotalCount);
        Assert.Equal(exceeded ? 0 : 5, result.Items.Count);
        Assert.Equal(exceeded ? 0 : 5, result.Summary.StopCount);
    }

    [Fact]
    public async Task Unknown_option_values_are_rejected()
    {
        await using var dbContext = CreateDbContext();
        var query = Query(dbContext);
        var request = Request(September1, September30);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            query.SearchAsync(request with { StatementAvailability = "Draft" }, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            query.SearchAsync(request with { SortBy = "Eta" }, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            query.SearchAsync(request with { SortDirection = "Up" }, CancellationToken.None));
    }

    [Fact]
    public async Task Cancellation_is_propagated()
    {
        await using var dbContext = CreateDbContext();
        await SeedKnownFixtureAsync(dbContext);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Query(dbContext).SearchAsync(Request(September1, September30), cancellation.Token));
    }

    private static ReportsPortCallMetric Measured(int minutes) =>
        new(minutes, ReportsPortCallPerformanceOptions.QualityValid, null);

    private static async Task<KnownFixture> SeedKnownFixtureAsync(HarborOpsDbContext dbContext)
    {
        var rotterdam = await SeedPortAsync(dbContext, "Rotterdam", "NLRTM");
        var hamburg = await SeedPortAsync(dbContext, "Hamburg", "DEHAM");
        var hamburgVesselId = Guid.NewGuid();

        var voyageA = await SeedVoyageAsync(dbContext, "V-2026-014");
        var a = await SeedStopAsync(dbContext, voyageA, rotterdam, 2, Utc(9, 2, 20), Utc(9, 3, 8));
        var statementA = await FinalizeAsync(
            dbContext,
            voyageA,
            a,
            rotterdam,
            "Euromax 3",
            Utc(9, 4, 9, 12),
            Milestone(PortCallActivityType.AllFast, Utc(9, 2, 21, 30)),
            Milestone(PortCallActivityType.CargoStarted, Utc(9, 2, 22)),
            DelayEntry("Tug", Utc(9, 2, 23), 45),
            Milestone(PortCallActivityType.CargoCompleted, Utc(9, 3, 6)));

        var voyageB = await SeedVoyageAsync(dbContext, "V-2026-017");
        var b = await SeedStopAsync(dbContext, voyageB, rotterdam, 1, Utc(9, 8, 10), Utc(9, 9, 4, 30));
        await FinalizeAsync(
            dbContext,
            voyageB,
            b,
            rotterdam,
            "Euromax 3",
            Utc(9, 10, 14, 40),
            Milestone(PortCallActivityType.AllFast, Utc(9, 8, 12, 15)),
            Milestone(PortCallActivityType.CargoStarted, Utc(9, 8, 13)),
            Milestone(PortCallActivityType.CargoStarted, Utc(9, 8, 13, 20)),
            Milestone(PortCallActivityType.CargoCompleted, Utc(9, 9, 1, 30)));

        var voyageC = await SeedVoyageAsync(dbContext, "V-2026-021");
        var c = await SeedStopAsync(dbContext, voyageC, rotterdam, 3, Utc(9, 14, 20), Utc(9, 15, 5));
        await FinalizeAsync(
            dbContext,
            voyageC,
            c,
            rotterdam,
            "Waalhaven 2",
            Utc(9, 16, 11, 30),
            DelayEntry("Pilot", Utc(9, 14, 21), 30),
            Milestone(PortCallActivityType.CargoStarted, Utc(9, 14, 22)),
            DelayEntry("Crane", Utc(9, 14, 23), 60),
            Milestone(PortCallActivityType.CargoCompleted, Utc(9, 15, 5)));

        var voyageD = await SeedVoyageAsync(dbContext, "V-2026-019", hamburgVesselId);
        var d = await SeedStopAsync(dbContext, voyageD, hamburg, 2, Utc(9, 11, 8), Utc(9, 12, 6));
        await FinalizeAsync(
            dbContext,
            voyageD,
            d,
            hamburg,
            "CTA 1",
            Utc(9, 13, 8, 5),
            Milestone(PortCallActivityType.AllFast, Utc(9, 11, 9)),
            Milestone(PortCallActivityType.CargoCompleted, Utc(9, 12, 2)),
            Milestone(PortCallActivityType.CargoStarted, Utc(9, 12, 2, 20)));

        var voyageE = await SeedVoyageAsync(dbContext, "V-2026-023", hamburgVesselId);
        var e = await SeedStopAsync(dbContext, voyageE, hamburg, 1, Utc(9, 17, 20), Utc(9, 18, 6));

        return new KnownFixture(rotterdam, hamburg, hamburgVesselId, voyageA, voyageB, a, b, c, d, e, statementA);
    }

    private sealed record KnownFixture(
        Port Rotterdam,
        Port Hamburg,
        Guid HamburgVesselId,
        Voyage VoyageA,
        Voyage VoyageB,
        VoyageStop A,
        VoyageStop B,
        VoyageStop C,
        VoyageStop D,
        VoyageStop E,
        PortCallStatement StatementA);
}
