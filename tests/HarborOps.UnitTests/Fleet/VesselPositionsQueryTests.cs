using HarborOps.Infrastructure.Persistence;
using HarborOps.Modules.Fleet.Application;
using HarborOps.Modules.Fleet.Contracts;
using HarborOps.Modules.Fleet.Domain;
using Microsoft.EntityFrameworkCore;
using FleetEntity = HarborOps.Modules.Fleet.Domain.Fleet;

namespace HarborOps.UnitTests.Fleet;

public sealed class VesselPositionsQueryTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 8, 25, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Get_returns_first_page_using_default_paging()
    {
        await using var dbContext = CreateDbContext();
        var vesselTypeId = await SeedVesselTypeAsync(dbContext);

        for (var index = 1; index <= 51; index++)
        {
            await SeedVesselAsync(dbContext, vesselTypeId, $"Vessel {index:D2}", $"{1000000 + index}");
        }

        var response = await CreateQuery(dbContext).GetAsync(
            new VesselPositionsRequest(),
            CancellationToken.None);

        Assert.Equal(1, response.PageNumber);
        Assert.Equal(VesselPositionsValidator.DefaultPageSize, response.PageSize);
        Assert.Equal(51, response.TotalCount);
        Assert.Equal(50, response.Items.Count);
    }

    [Fact]
    public async Task Get_returns_the_requested_page()
    {
        await using var dbContext = CreateDbContext();
        var vesselTypeId = await SeedVesselTypeAsync(dbContext);
        await SeedVesselAsync(dbContext, vesselTypeId, "Vessel 01", "1000001");
        await SeedVesselAsync(dbContext, vesselTypeId, "Vessel 02", "1000002");
        await SeedVesselAsync(dbContext, vesselTypeId, "Vessel 03", "1000003");

        var response = await CreateQuery(dbContext).GetAsync(
            new VesselPositionsRequest { PageNumber = 2, PageSize = 1 },
            CancellationToken.None);

        Assert.Equal(2, response.PageNumber);
        Assert.Equal(3, response.TotalCount);
        Assert.Equal("Vessel 02", Assert.Single(response.Items).Name);
    }

    [Fact]
    public async Task Get_returns_an_empty_page_when_no_vessels_are_registered()
    {
        await using var dbContext = CreateDbContext();

        var response = await CreateQuery(dbContext).GetAsync(
            new VesselPositionsRequest(),
            CancellationToken.None);

        Assert.Equal(0, response.TotalCount);
        Assert.Empty(response.Items);
        Assert.Equal(1, response.PageNumber);
    }

    [Fact]
    public async Task Get_returns_the_configured_stale_threshold_and_evaluation_time()
    {
        await using var dbContext = CreateDbContext();

        var response = await CreateQuery(dbContext).GetAsync(
            new VesselPositionsRequest(),
            CancellationToken.None);

        Assert.Equal(VesselPositionsQuery.StaleAfterHours, response.StaleAfterHours);
        Assert.Equal(FixedNow.UtcDateTime, response.EvaluatedAtUtc);
    }

    [Fact]
    public async Task Get_filters_by_current_status()
    {
        await using var dbContext = CreateDbContext();
        var vesselTypeId = await SeedVesselTypeAsync(dbContext);
        await SeedVesselAsync(dbContext, vesselTypeId, "North Star", "1000001", "Active");
        await SeedVesselAsync(dbContext, vesselTypeId, "Southern Cross", "1000002", "At Port");
        await SeedVesselAsync(dbContext, vesselTypeId, "Western Gale", "1000003", "At Port");

        var response = await CreateQuery(dbContext).GetAsync(
            new VesselPositionsRequest { Status = "At Port" },
            CancellationToken.None);

        Assert.Equal(2, response.TotalCount);
        Assert.All(response.Items, item => Assert.Equal("At Port", item.CurrentStatus));
    }

    [Fact]
    public async Task Get_by_fleet_excludes_ended_assignments()
    {
        await using var dbContext = CreateDbContext();
        var vesselTypeId = await SeedVesselTypeAsync(dbContext);
        var assigned = await SeedVesselAsync(dbContext, vesselTypeId, "North Star", "1000001");
        var released = await SeedVesselAsync(dbContext, vesselTypeId, "Southern Cross", "1000002");
        var fleet = await SeedFleetAsync(dbContext, "Atlantic Fleet");

        await SeedActiveAssignmentAsync(dbContext, assigned.Id, fleet.Id);
        await SeedEndedAssignmentAsync(dbContext, released.Id, fleet.Id);

        var filtered = await CreateQuery(dbContext).GetAsync(
            new VesselPositionsRequest { FleetId = fleet.Id },
            CancellationToken.None);

        Assert.Equal(1, filtered.TotalCount);
        Assert.Equal(assigned.Id, Assert.Single(filtered.Items).VesselId);
    }

    [Fact]
    public async Task Get_projects_the_active_fleet_and_position()
    {
        await using var dbContext = CreateDbContext();
        var vesselTypeId = await SeedVesselTypeAsync(dbContext);
        var reportedAtUtc = FixedNow.UtcDateTime.AddHours(-1);
        var vessel = await SeedVesselAsync(
            dbContext,
            vesselTypeId,
            "North Star",
            "1000001",
            position: (35.898765m, -5.123456m),
            reportedAtUtc: reportedAtUtc);
        var fleet = await SeedFleetAsync(dbContext, "Atlantic Fleet");
        await SeedActiveAssignmentAsync(dbContext, vessel.Id, fleet.Id);

        var response = await CreateQuery(dbContext).GetAsync(
            new VesselPositionsRequest(),
            CancellationToken.None);

        var item = Assert.Single(response.Items);

        Assert.Equal(vessel.Id, item.VesselId);
        Assert.Equal("North Star", item.Name);
        Assert.Equal("1000001", item.ImoNumber);
        Assert.Equal("Active", item.CurrentStatus);
        Assert.Equal(fleet.Id, item.FleetId);
        Assert.Equal("Atlantic Fleet", item.FleetName);
        Assert.Equal(35.898765m, item.CurrentLatitude);
        Assert.Equal(-5.123456m, item.CurrentLongitude);
        Assert.Equal(reportedAtUtc, item.LastPositionReportedAtUtc);
        Assert.True(item.HasPosition);
        Assert.False(item.IsPositionStale);
    }

    [Fact]
    public async Task Get_leaves_the_fleet_empty_when_no_assignment_is_active()
    {
        await using var dbContext = CreateDbContext();
        var vesselTypeId = await SeedVesselTypeAsync(dbContext);
        var vessel = await SeedVesselAsync(dbContext, vesselTypeId, "North Star", "1000001");
        var fleet = await SeedFleetAsync(dbContext, "Atlantic Fleet");
        await SeedEndedAssignmentAsync(dbContext, vessel.Id, fleet.Id);

        var response = await CreateQuery(dbContext).GetAsync(
            new VesselPositionsRequest(),
            CancellationToken.None);

        var item = Assert.Single(response.Items);

        Assert.Null(item.FleetId);
        Assert.Null(item.FleetName);
    }

    [Fact]
    public async Task Get_treats_a_report_exactly_on_the_threshold_as_fresh()
    {
        await using var dbContext = CreateDbContext();
        var vesselTypeId = await SeedVesselTypeAsync(dbContext);
        await SeedVesselAsync(
            dbContext,
            vesselTypeId,
            "North Star",
            "1000001",
            position: (10m, 20m),
            reportedAtUtc: FixedNow.UtcDateTime.AddHours(-VesselPositionsQuery.StaleAfterHours));

        var response = await CreateQuery(dbContext).GetAsync(
            new VesselPositionsRequest(),
            CancellationToken.None);

        Assert.False(Assert.Single(response.Items).IsPositionStale);
    }

    [Fact]
    public async Task Get_marks_a_report_older_than_the_threshold_as_stale()
    {
        await using var dbContext = CreateDbContext();
        var vesselTypeId = await SeedVesselTypeAsync(dbContext);
        await SeedVesselAsync(
            dbContext,
            vesselTypeId,
            "North Star",
            "1000001",
            position: (10m, 20m),
            reportedAtUtc: FixedNow.UtcDateTime
                .AddHours(-VesselPositionsQuery.StaleAfterHours)
                .AddMinutes(-1));

        var response = await CreateQuery(dbContext).GetAsync(
            new VesselPositionsRequest(),
            CancellationToken.None);

        var item = Assert.Single(response.Items);

        Assert.True(item.IsPositionStale);
        Assert.True(item.HasPosition);
    }

    [Fact]
    public async Task Get_marks_a_missing_position_as_stale()
    {
        await using var dbContext = CreateDbContext();
        var vesselTypeId = await SeedVesselTypeAsync(dbContext);
        await SeedVesselAsync(dbContext, vesselTypeId, "North Star", "1000001");

        var response = await CreateQuery(dbContext).GetAsync(
            new VesselPositionsRequest(),
            CancellationToken.None);

        var item = Assert.Single(response.Items);

        Assert.Null(item.CurrentLatitude);
        Assert.Null(item.CurrentLongitude);
        Assert.Null(item.LastPositionReportedAtUtc);
        Assert.False(item.HasPosition);
        Assert.True(item.IsPositionStale);
    }

    [Fact]
    public async Task Get_marks_a_preserved_position_without_a_report_timestamp_as_stale()
    {
        // Rows migrated from before position reporting existed keep their coordinates but have no
        // known observation time, so they must never be presented as fresh.
        await using var dbContext = CreateDbContext();
        var vesselTypeId = await SeedVesselTypeAsync(dbContext);
        await SeedMigratedPositionAsync(dbContext, vesselTypeId, "North Star", "1000001", 10m, 20m);

        var response = await CreateQuery(dbContext).GetAsync(
            new VesselPositionsRequest(),
            CancellationToken.None);

        var item = Assert.Single(response.Items);

        Assert.Equal(10m, item.CurrentLatitude);
        Assert.Null(item.LastPositionReportedAtUtc);
        Assert.True(item.IsPositionStale);
    }

    [Fact]
    public async Task Get_filters_to_stale_vessels_only()
    {
        await using var dbContext = CreateDbContext();
        var vesselTypeId = await SeedVesselTypeAsync(dbContext);
        await SeedVesselAsync(
            dbContext,
            vesselTypeId,
            "Fresh",
            "1000001",
            position: (10m, 20m),
            reportedAtUtc: FixedNow.UtcDateTime.AddHours(-1));
        await SeedVesselAsync(
            dbContext,
            vesselTypeId,
            "Stale",
            "1000002",
            position: (11m, 21m),
            reportedAtUtc: FixedNow.UtcDateTime.AddDays(-3));
        await SeedVesselAsync(dbContext, vesselTypeId, "Missing", "1000003");

        var response = await CreateQuery(dbContext).GetAsync(
            new VesselPositionsRequest { StaleOnly = true },
            CancellationToken.None);

        Assert.Equal(2, response.TotalCount);
        Assert.All(response.Items, item => Assert.True(item.IsPositionStale));
        Assert.DoesNotContain(response.Items, item => item.Name == "Fresh");
    }

    [Fact]
    public async Task Get_ignores_the_stale_filter_when_it_is_not_requested()
    {
        await using var dbContext = CreateDbContext();
        var vesselTypeId = await SeedVesselTypeAsync(dbContext);
        await SeedVesselAsync(
            dbContext,
            vesselTypeId,
            "Fresh",
            "1000001",
            position: (10m, 20m),
            reportedAtUtc: FixedNow.UtcDateTime.AddHours(-1));
        await SeedVesselAsync(dbContext, vesselTypeId, "Missing", "1000002");

        var response = await CreateQuery(dbContext).GetAsync(
            new VesselPositionsRequest { StaleOnly = false },
            CancellationToken.None);

        Assert.Equal(2, response.TotalCount);
    }

    [Fact]
    public async Task Get_orders_most_recent_reports_first_then_unreported_vessels_by_name_and_imo_number()
    {
        await using var dbContext = CreateDbContext();
        var vesselTypeId = await SeedVesselTypeAsync(dbContext);

        var fresh = await SeedVesselAsync(
            dbContext,
            vesselTypeId,
            "Aurora",
            "1000001",
            position: (10m, 20m),
            reportedAtUtc: FixedNow.UtcDateTime.AddHours(-1));
        var staleAurora = await SeedVesselAsync(
            dbContext,
            vesselTypeId,
            "Aurora",
            "1000003",
            position: (11m, 21m),
            reportedAtUtc: FixedNow.UtcDateTime.AddDays(-2));
        var staleBorealisLowImo = await SeedVesselAsync(dbContext, vesselTypeId, "Borealis", "1000002");
        var staleBorealisHighImo = await SeedVesselAsync(dbContext, vesselTypeId, "Borealis", "1000004");
        var firstTwin = await SeedVesselAsync(dbContext, vesselTypeId, "Zephyr", "1000005");
        var secondTwin = await SeedVesselAsync(dbContext, vesselTypeId, "Zephyr", "1000005");

        var response = await CreateQuery(dbContext).GetAsync(
            new VesselPositionsRequest(),
            CancellationToken.None);

        var expectedTwinIds = new[] { firstTwin.Id, secondTwin.Id }.OrderBy(id => id).ToArray();

        Assert.Equal(
            [
                fresh.Id,
                staleAurora.Id,
                staleBorealisLowImo.Id,
                staleBorealisHighImo.Id,
                expectedTwinIds[0],
                expectedTwinIds[1],
            ],
            response.Items.Select(item => item.VesselId));
    }

    [Fact]
    public async Task Get_rejects_page_number_below_one()
    {
        await using var dbContext = CreateDbContext();

        var exception = await Assert.ThrowsAsync<VesselPositionsValidationException>(
            () => CreateQuery(dbContext).GetAsync(
                new VesselPositionsRequest { PageNumber = 0 },
                CancellationToken.None));

        Assert.Contains(VesselPositionsValidator.PageNumberField, exception.Errors);
    }

    [Fact]
    public async Task Get_rejects_page_size_above_maximum()
    {
        await using var dbContext = CreateDbContext();

        var exception = await Assert.ThrowsAsync<VesselPositionsValidationException>(
            () => CreateQuery(dbContext).GetAsync(
                new VesselPositionsRequest { PageSize = VesselPositionsValidator.MaxPageSize + 1 },
                CancellationToken.None));

        Assert.Contains(VesselPositionsValidator.PageSizeField, exception.Errors);
    }

    [Fact]
    public async Task Get_accepts_maximum_page_size()
    {
        await using var dbContext = CreateDbContext();
        var vesselTypeId = await SeedVesselTypeAsync(dbContext);
        await SeedVesselAsync(dbContext, vesselTypeId, "North Star", "1000001");

        var response = await CreateQuery(dbContext).GetAsync(
            new VesselPositionsRequest { PageSize = VesselPositionsValidator.MaxPageSize },
            CancellationToken.None);

        Assert.Equal(VesselPositionsValidator.MaxPageSize, response.PageSize);
        Assert.Single(response.Items);
    }

    [Fact]
    public async Task Get_rejects_empty_fleet_identifier()
    {
        await using var dbContext = CreateDbContext();

        var exception = await Assert.ThrowsAsync<VesselPositionsValidationException>(
            () => CreateQuery(dbContext).GetAsync(
                new VesselPositionsRequest { FleetId = Guid.Empty },
                CancellationToken.None));

        Assert.Contains(VesselPositionsValidator.FleetIdField, exception.Errors);
    }

    [Fact]
    public async Task Get_rejects_a_status_outside_the_vocabulary()
    {
        await using var dbContext = CreateDbContext();

        var exception = await Assert.ThrowsAsync<VesselPositionsValidationException>(
            () => CreateQuery(dbContext).GetAsync(
                new VesselPositionsRequest { Status = "Sinking" },
                CancellationToken.None));

        Assert.Contains(VesselPositionsValidator.StatusField, exception.Errors);
    }

    [Fact]
    public async Task Search_matches_mmsi_and_includes_it_in_position_results()
    {
        await using var dbContext = CreateDbContext();
        var vesselTypeId = await SeedVesselTypeAsync(dbContext);
        var vessel = await SeedVesselAsync(dbContext, vesselTypeId, "Tracked", "1000001");
        dbContext.Entry(vessel).Property(item => item.Mmsi).CurrentValue = "227441980";
        await dbContext.SaveChangesAsync();
        var response = await CreateQuery(dbContext).GetAsync(new VesselPositionsRequest { Search = "227441980" }, CancellationToken.None);
        Assert.Equal("227441980", Assert.Single(response.Items).Mmsi);
    }

    [Fact]
    public async Task Unsaved_ais_cache_does_not_override_persisted_positions()
    {
        await using var dbContext = CreateDbContext();
        var vesselTypeId = await SeedVesselTypeAsync(dbContext);
        await SeedVesselAsync(dbContext, vesselTypeId, "Alpha", "1000001");
        await SeedVesselAsync(dbContext, vesselTypeId, "Zulu", "1000002");
        var state = new AisTrackingState();
        state.Record("1000002", new AisPosition(42, 10, FixedNow.UtcDateTime));
        var query = new VesselPositionsQuery(dbContext, new FixedTimeProvider(FixedNow), state);

        var firstPage = await query.GetAsync(new VesselPositionsRequest { PageSize = 1 }, CancellationToken.None);
        Assert.Equal("Alpha", Assert.Single(firstPage.Items).Name);
        Assert.Null(firstPage.Items[0].CurrentLatitude);
        var stale = await query.GetAsync(new VesselPositionsRequest { StaleOnly = true }, CancellationToken.None);
        Assert.Equal(2, stale.TotalCount);
        Assert.All(dbContext.Set<Vessel>(), vessel => Assert.Null(vessel.CurrentLatitude));
        Assert.Empty(dbContext.Set<VesselPositionReport>());
    }

    [Fact]
    public async Task Newer_manual_report_takes_precedence_and_old_ais_observation_remains_stale()
    {
        await using var dbContext = CreateDbContext();
        var vesselTypeId = await SeedVesselTypeAsync(dbContext);
        await SeedVesselAsync(dbContext, vesselTypeId, "Manual", "1000001", position: (1, 2));
        await SeedVesselAsync(dbContext, vesselTypeId, "Delayed", "1000002");
        var state = new AisTrackingState();
        state.Record("1000001", new AisPosition(42, 10, FixedNow.AddHours(-1).UtcDateTime));
        state.Record("1000002", new AisPosition(42, 10, FixedNow.AddHours(-13).UtcDateTime));
        var query = new VesselPositionsQuery(dbContext, new FixedTimeProvider(FixedNow), state);
        var response = await query.GetAsync(new VesselPositionsRequest(), CancellationToken.None);
        var manual = response.Items.Single(item => item.Name == "Manual");
        Assert.Equal(1, manual.CurrentLatitude);
        Assert.Null(manual.PositionSource);
        Assert.True(response.Items.Single(item => item.Name == "Delayed").IsPositionStale);
    }

    private static VesselPositionsQuery CreateQuery(HarborOpsDbContext dbContext) =>
        new(dbContext, new FixedTimeProvider(FixedNow));

    private static async Task<Guid> SeedVesselTypeAsync(HarborOpsDbContext dbContext)
    {
        var vesselType = VesselType.Register("Container Ship", "CNTR", "Container carrying vessel");

        await dbContext.Set<VesselType>().AddAsync(vesselType);
        await dbContext.SaveChangesAsync();

        return vesselType.Id;
    }

    private static async Task<Vessel> SeedVesselAsync(
        HarborOpsDbContext dbContext,
        Guid vesselTypeId,
        string name,
        string imoNumber,
        string currentStatus = "Active",
        (decimal Latitude, decimal Longitude)? position = null,
        DateTime? reportedAtUtc = null)
    {
        var vessel = Vessel.Register(
            Guid.NewGuid(),
            vesselTypeId,
            name,
            imoNumber,
            "Malta",
            currentStatus);

        if (position is { } coordinates)
        {
            vessel.ReportPosition(
                coordinates.Latitude,
                coordinates.Longitude,
                reportedAtUtc ?? FixedNow.UtcDateTime);
        }

        await dbContext.Set<Vessel>().AddAsync(vessel);
        await dbContext.SaveChangesAsync();

        return vessel;
    }

    private static async Task<Vessel> SeedMigratedPositionAsync(
        HarborOpsDbContext dbContext,
        Guid vesselTypeId,
        string name,
        string imoNumber,
        decimal latitude,
        decimal longitude)
    {
        var vessel = await SeedVesselAsync(
            dbContext,
            vesselTypeId,
            name,
            imoNumber,
            position: (latitude, longitude));

        dbContext.Entry(vessel).Property(v => v.LastPositionReportedAtUtc).CurrentValue = null;
        await dbContext.SaveChangesAsync();

        return vessel;
    }

    private static async Task<FleetEntity> SeedFleetAsync(HarborOpsDbContext dbContext, string name)
    {
        var fleet = FleetEntity.Register(name, "Fleet used by vessel position tests.");

        await dbContext.Set<FleetEntity>().AddAsync(fleet);
        await dbContext.SaveChangesAsync();

        return fleet;
    }

    private static async Task SeedActiveAssignmentAsync(
        HarborOpsDbContext dbContext,
        Guid vesselId,
        Guid fleetId)
    {
        await dbContext.Set<FleetAssignment>().AddAsync(FleetAssignment.Register(vesselId, fleetId));
        await dbContext.SaveChangesAsync();
    }

    private static async Task SeedEndedAssignmentAsync(
        HarborOpsDbContext dbContext,
        Guid vesselId,
        Guid fleetId)
    {
        var assignment = FleetAssignment.Register(vesselId, fleetId);
        assignment.End();

        await dbContext.Set<FleetAssignment>().AddAsync(assignment);
        await dbContext.SaveChangesAsync();
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
