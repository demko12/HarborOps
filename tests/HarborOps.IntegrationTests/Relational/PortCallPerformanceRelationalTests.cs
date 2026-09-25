using HarborOps.Infrastructure.Persistence;
using HarborOps.Modules.Voyages.Application;
using HarborOps.Modules.Voyages.Contracts;
using HarborOps.Modules.Voyages.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.IntegrationTests.Relational;

public sealed class PortCallPerformanceRelationalTests : IAsyncLifetime
{
    private static readonly Guid ActorId = Guid.Parse("5e6f7081-92a3-4b4c-9d5e-6f708192a3b4");

    private readonly string databaseName =
        $"HarborOpsRelationalTests_{Guid.NewGuid():N}";

    public async Task InitializeAsync()
    {
        if (!SqlServerAvailability.IsAvailable)
        {
            return;
        }

        await using var dbContext = CreateDbContext();
        await dbContext.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        if (!SqlServerAvailability.IsAvailable)
        {
            return;
        }

        await using var dbContext = CreateDbContext();
        await dbContext.Database.EnsureDeletedAsync();
    }

    [RelationalFact]
    public async Task The_report_query_translates_to_sql_server_and_returns_exact_metrics_and_summaries()
    {
        var fixture = await SeedAsync();
        await using var dbContext = CreateDbContext();

        var result = await new ReportsPortCallPerformanceQuery(dbContext).SearchAsync(
            September(),
            CancellationToken.None);

        Assert.False(result.StopLimitExceeded);
        Assert.Equal(5, result.TotalCount);
        Assert.Equal(
            [.. fixture.RangeStartStops.Order(), fixture.A, fixture.B, fixture.RangeEndDraft],
            result.Items.Select(row => row.VoyageStopId));

        var a = result.Items.Single(row => row.VoyageStopId == fixture.A);
        Assert.Equal(Measured(720), a.Turnaround);
        Assert.Equal(Measured(90), a.ArrivalToAllFast);
        Assert.Equal(Measured(480), a.CargoElapsed);
        Assert.Equal(Measured(120), a.CargoCompletedToDeparture);
        Assert.Equal(1, a.RecordedDelayCount);
        Assert.Equal(45, a.RecordedDelayMinutes);
        Assert.Equal("Euromax 3", a.BerthName);
        Assert.Equal(new DateTime(2026, 9, 3, 8, 0, 0), a.ActualDepartureUtc);

        var b = result.Items.Single(row => row.VoyageStopId == fixture.B);
        Assert.Equal(Measured(1110), b.Turnaround);
        Assert.Equal(
            new ReportsPortCallMetric(
                null,
                ReportsPortCallPerformanceOptions.QualityAmbiguous,
                PortCallActivityType.CargoStarted),
            b.CargoElapsed);
        Assert.Equal(2, b.RecordedDelayCount);
        Assert.Equal(90, b.RecordedDelayMinutes);

        var draft = result.Items.Single(row => row.VoyageStopId == fixture.RangeEndDraft);
        Assert.Equal(ReportsPortCallPerformanceOptions.NotFinalized, draft.StatementAvailability);
        Assert.Null(draft.Turnaround);

        Assert.Equal(
            new ReportsPortCallPerformanceSummary(
                5,
                2,
                3,
                new ReportsPortCallMetricSummary(915m, 2, 0, 0, 0),
                new ReportsPortCallMetricSummary(112.5m, 2, 0, 0, 0),
                new ReportsPortCallMetricSummary(480m, 1, 0, 1, 0),
                new ReportsPortCallMetricSummary(150m, 2, 0, 0, 0),
                3,
                135),
            result.Summary);
        Assert.Equal(["Hamburg", "Rotterdam"], result.Ports.Select(port => port.PortName));
        Assert.Equal(3, result.Ports[0].Summary.NotFinalizedCount);
        Assert.Equal(2, result.Ports[1].Summary.FinalizedCount);
    }

    [RelationalFact]
    public async Task Every_filter_translates_to_sql_server()
    {
        var fixture = await SeedAsync();
        await using var dbContext = CreateDbContext();
        var query = new ReportsPortCallPerformanceQuery(dbContext);
        var all = September();

        var finalized = await query.SearchAsync(
            all with { StatementAvailability = ReportsPortCallPerformanceOptions.Finalized },
            CancellationToken.None);
        var notFinalized = await query.SearchAsync(
            all with { StatementAvailability = ReportsPortCallPerformanceOptions.NotFinalized },
            CancellationToken.None);
        var byPort = await query.SearchAsync(all with { PortId = fixture.RotterdamId }, CancellationToken.None);
        var byVessel = await query.SearchAsync(all with { VesselId = fixture.VesselBId }, CancellationToken.None);
        var byVoyage = await query.SearchAsync(all with { VoyageId = fixture.VoyageAId }, CancellationToken.None);
        var sorted = await query.SearchAsync(
            all with
            {
                SortBy = ReportsPortCallPerformanceOptions.SortByTurnaround,
                SortDirection = ReportsPortCallPerformanceOptions.Descending,
                PageSize = 2,
            },
            CancellationToken.None);

        Assert.Equal([fixture.A, fixture.B], finalized.Items.Select(row => row.VoyageStopId));
        Assert.Equal(3, notFinalized.TotalCount);
        Assert.Equal([fixture.A, fixture.B], byPort.Items.Select(row => row.VoyageStopId));
        Assert.Equal([fixture.B], byVessel.Items.Select(row => row.VoyageStopId));
        Assert.Equal([fixture.A], byVoyage.Items.Select(row => row.VoyageStopId));
        Assert.Equal([fixture.B, fixture.A], sorted.Items.Select(row => row.VoyageStopId));
        Assert.Equal(5, sorted.TotalCount);
        Assert.Equal(5, sorted.Summary.StopCount);
    }

    [RelationalFact]
    public async Task The_stop_limit_is_enforced_with_a_count_before_rows_are_loaded()
    {
        await SeedAsync();
        await using var dbContext = CreateDbContext();

        var result = await new ReportsPortCallPerformanceQuery(dbContext, 4).SearchAsync(
            September(),
            CancellationToken.None);

        Assert.True(result.StopLimitExceeded);
        Assert.Equal(5, result.TotalCount);
        Assert.Empty(result.Items);
    }

    private static ReportsPortCallPerformanceRequest September() =>
        new() { DepartedFrom = new DateOnly(2026, 9, 1), DepartedTo = new DateOnly(2026, 9, 30), PageSize = 100 };

    private static ReportsPortCallMetric Measured(int minutes) =>
        new(minutes, ReportsPortCallPerformanceOptions.QualityValid, null);

    private static DateTime Utc(int month, int day, int hour, int minute = 0, int second = 0) =>
        new(2026, month, day, hour, minute, second, DateTimeKind.Utc);

    private async Task<Fixture> SeedAsync()
    {
        await using var dbContext = CreateDbContext();

        var rotterdam = Port.Create("Rotterdam", "Netherlands", unLocode: "NLRTM");
        var hamburg = Port.Create("Hamburg", "Germany", unLocode: "DEHAM");
        dbContext.AddRange(rotterdam, hamburg);

        var voyageA = SeedVoyage(dbContext, "V-2026-014", Guid.NewGuid());
        var a = SeedStop(dbContext, voyageA, rotterdam, 2, Utc(9, 2, 20), Utc(9, 3, 8));
        SeedFinalizedStatement(
            dbContext,
            voyageA,
            a,
            rotterdam,
            "Euromax 3",
            Milestone(PortCallActivityType.AllFast, Utc(9, 2, 21, 30)),
            Milestone(PortCallActivityType.CargoStarted, Utc(9, 2, 22)),
            DelayEntry("Tug", Utc(9, 2, 23), 45),
            Milestone(PortCallActivityType.CargoCompleted, Utc(9, 3, 6)));

        var vesselBId = Guid.NewGuid();
        var voyageB = SeedVoyage(dbContext, "V-2026-017", vesselBId);
        var b = SeedStop(dbContext, voyageB, rotterdam, 1, Utc(9, 8, 10), Utc(9, 9, 4, 30));
        SeedFinalizedStatement(
            dbContext,
            voyageB,
            b,
            rotterdam,
            "Euromax 3",
            Milestone(PortCallActivityType.AllFast, Utc(9, 8, 12, 15)),
            Milestone(PortCallActivityType.CargoStarted, Utc(9, 8, 13)),
            DelayEntry("Crane", Utc(9, 8, 14), 30),
            Milestone(PortCallActivityType.CargoStarted, Utc(9, 8, 13, 20)),
            DelayEntry("Crane", Utc(9, 8, 15), 60),
            Milestone(PortCallActivityType.CargoCompleted, Utc(9, 9, 1, 30)));

        var voyageC = SeedVoyage(dbContext, "V-2026-023", Guid.NewGuid());
        var rangeStartFirst = SeedStop(dbContext, voyageC, hamburg, 1, Utc(8, 31, 18), Utc(9, 1, 0));
        var rangeStartSecond = SeedStop(dbContext, voyageC, hamburg, 2, Utc(8, 31, 19), Utc(9, 1, 0));
        var rangeEndDraft = SeedStop(dbContext, voyageC, hamburg, 3, Utc(9, 30, 18), Utc(9, 30, 23, 59, 59));
        dbContext.Add(PortCallStatement.CreateDraft(voyageC.Id, rangeEndDraft.Id, ActorId, Utc(10, 1, 1)));

        var voyageOutside = SeedVoyage(dbContext, "V-2026-030", Guid.NewGuid());
        SeedStop(dbContext, voyageOutside, hamburg, 1, Utc(8, 31, 12), Utc(8, 31, 23, 59, 59));
        var afterRange = SeedStop(dbContext, voyageOutside, rotterdam, 2, Utc(9, 30, 20), Utc(10, 1, 0));
        SeedFinalizedStatement(dbContext, voyageOutside, afterRange, rotterdam, null);
        SeedStop(dbContext, voyageOutside, rotterdam, 3, Utc(10, 2, 8), null);

        await dbContext.SaveChangesAsync();

        return new Fixture(
            rotterdam.Id,
            vesselBId,
            voyageA.Id,
            a.Id,
            b.Id,
            [rangeStartFirst.Id, rangeStartSecond.Id],
            rangeEndDraft.Id);
    }

    private static Voyage SeedVoyage(HarborOpsDbContext dbContext, string voyageNumber, Guid vesselId)
    {
        var voyage = Voyage.Schedule(
            vesselId, voyageNumber, "NLRTM", "DEHAM", Utc(8, 1, 0), Utc(12, 31, 0));
        dbContext.Add(voyage);

        return voyage;
    }

    private static VoyageStop SeedStop(
        HarborOpsDbContext dbContext,
        Voyage voyage,
        Port port,
        int stopOrder,
        DateTime actualArrivalUtc,
        DateTime? actualDepartureUtc)
    {
        var stop = VoyageStop.Create(
            voyage.Id,
            port.Id,
            stopOrder,
            actualArrivalUtc,
            actualDepartureUtc ?? actualArrivalUtc.AddHours(12));
        stop.RecordArrival(actualArrivalUtc);

        if (actualDepartureUtc is { } departure)
        {
            stop.RecordDeparture(departure);
        }

        dbContext.Add(stop);

        return stop;
    }

    private static void SeedFinalizedStatement(
        HarborOpsDbContext dbContext,
        Voyage voyage,
        VoyageStop stop,
        Port port,
        string? berthName,
        params PortCallLogEntry[] entries)
    {
        var statement = PortCallStatement.CreateDraft(voyage.Id, stop.Id, ActorId, stop.Atd!.Value);
        var captured = statement.Finalize(
            new PortCallStatementSnapshot(
                voyage.VoyageNumber,
                stop.StopOrder,
                port.Name,
                port.UnLocode,
                null,
                berthName,
                stop.Ata!.Value,
                stop.Atd.Value,
                new string('D', 64),
                entries),
            ActorId,
            "reports@harborops.test",
            stop.Atd.Value.AddDays(1));

        dbContext.Add(statement);
        dbContext.AddRange(captured);
    }

    private static PortCallLogEntry Milestone(string activityType, DateTime occurredAtUtc) =>
        new(PortCallLogSource.ManualActivity, Guid.NewGuid(), 1, 0, activityType, occurredAtUtc, null, null, null);

    private static PortCallLogEntry DelayEntry(string reason, DateTime createdAtUtc, int durationMinutes) =>
        new(PortCallLogSource.Delay, Guid.NewGuid(), null, 0, reason, createdAtUtc, null, null, durationMinutes);

    private HarborOpsDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<HarborOpsDbContext>()
            .UseSqlServer(SqlServerAvailability.ConnectionStringFor(databaseName))
            .Options;

        return new HarborOpsDbContext(options);
    }

    private sealed record Fixture(
        Guid RotterdamId,
        Guid VesselBId,
        Guid VoyageAId,
        Guid A,
        Guid B,
        IReadOnlyList<Guid> RangeStartStops,
        Guid RangeEndDraft);
}
