using HarborOps.Infrastructure.Persistence;
using HarborOps.Modules.Voyages.Application;
using HarborOps.Modules.Voyages.Contracts;
using HarborOps.Modules.Voyages.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.UnitTests.Voyages;

public sealed class GetVoyagesQueryTests
{
    private static readonly DateOnly WindowFrom = new(2026, 9, 1);
    private static readonly DateOnly WindowTo = new(2026, 9, 30);

    [Fact]
    public async Task Window_includes_voyages_on_both_boundary_days()
    {
        await using var dbContext = CreateDbContext();
        await SeedAsync(dbContext, "VOY-501", new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc));
        await SeedAsync(dbContext, "VOY-502", new DateTime(2026, 9, 30, 23, 59, 0, DateTimeKind.Utc));
        await SeedAsync(dbContext, "VOY-503", new DateTime(2026, 8, 31, 23, 59, 0, DateTimeKind.Utc));
        await SeedAsync(dbContext, "VOY-504", new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc));

        var response = await CreateQuery(dbContext).GetVoyagesAsync(
            new GetVoyagesRequest { From = WindowFrom, To = WindowTo },
            CancellationToken.None);

        Assert.Equal(2, response.TotalCount);
        Assert.Equal(
            ["VOY-501", "VOY-502"],
            response.Items.Select(item => item.VoyageNumber));
    }

    [Fact]
    public async Task Status_filter_returns_only_matching_voyages()
    {
        await using var dbContext = CreateDbContext();
        await SeedAsync(dbContext, "VOY-511", new DateTime(2026, 9, 3, 8, 0, 0, DateTimeKind.Utc));
        var departed = await SeedAsync(
            dbContext,
            "VOY-512",
            new DateTime(2026, 9, 4, 8, 0, 0, DateTimeKind.Utc));

        departed.Depart(new DateTime(2026, 9, 4, 9, 0, 0, DateTimeKind.Utc));
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var response = await CreateQuery(dbContext).GetVoyagesAsync(
            new GetVoyagesRequest
            {
                From = WindowFrom,
                To = WindowTo,
                Status = Voyage.InProgressStatus,
            },
            CancellationToken.None);

        Assert.Equal(1, response.TotalCount);
        Assert.Equal("VOY-512", Assert.Single(response.Items).VoyageNumber);
    }

    [Fact]
    public async Task Blank_status_is_treated_as_no_filter()
    {
        await using var dbContext = CreateDbContext();
        await SeedAsync(dbContext, "VOY-521", new DateTime(2026, 9, 5, 8, 0, 0, DateTimeKind.Utc));
        await SeedAsync(dbContext, "VOY-522", new DateTime(2026, 9, 6, 8, 0, 0, DateTimeKind.Utc));

        var response = await CreateQuery(dbContext).GetVoyagesAsync(
            new GetVoyagesRequest { From = WindowFrom, To = WindowTo, Status = string.Empty },
            CancellationToken.None);

        Assert.Equal(2, response.TotalCount);
    }

    [Fact]
    public async Task Active_voyages_stay_in_the_result_when_they_started_before_the_window()
    {
        await using var dbContext = CreateDbContext();
        var atSea = await SeedAsync(
            dbContext,
            "VOY-921",
            new DateTime(2026, 7, 20, 8, 0, 0, DateTimeKind.Utc));
        var overdue = await SeedAsync(
            dbContext,
            "VOY-922",
            new DateTime(2026, 7, 25, 8, 0, 0, DateTimeKind.Utc));
        await SeedAsync(dbContext, "VOY-923", new DateTime(2026, 9, 9, 8, 0, 0, DateTimeKind.Utc));

        atSea.Depart(new DateTime(2026, 7, 20, 9, 0, 0, DateTimeKind.Utc));
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var response = await CreateQuery(dbContext).GetVoyagesAsync(
            new GetVoyagesRequest { From = WindowFrom, To = WindowTo, IncludeActive = true },
            CancellationToken.None);

        Assert.Equal(3, response.TotalCount);
        Assert.Contains(response.Items, item => item.VoyageId == atSea.Id);
        Assert.Contains(response.Items, item => item.VoyageId == overdue.Id);
    }

    [Fact]
    public async Task Completed_voyages_before_the_window_stay_out_of_the_result()
    {
        await using var dbContext = CreateDbContext();
        var finished = await SeedAsync(
            dbContext,
            "VOY-931",
            new DateTime(2026, 7, 20, 8, 0, 0, DateTimeKind.Utc));

        finished.Depart(new DateTime(2026, 7, 20, 9, 0, 0, DateTimeKind.Utc));
        finished.Arrive(new DateTime(2026, 7, 24, 9, 0, 0, DateTimeKind.Utc));
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var response = await CreateQuery(dbContext).GetVoyagesAsync(
            new GetVoyagesRequest { From = WindowFrom, To = WindowTo, IncludeActive = true },
            CancellationToken.None);

        Assert.Equal(0, response.TotalCount);
    }

    [Fact]
    public async Task Active_voyages_outside_the_window_are_excluded_by_default()
    {
        await using var dbContext = CreateDbContext();
        var atSea = await SeedAsync(
            dbContext,
            "VOY-941",
            new DateTime(2026, 7, 20, 8, 0, 0, DateTimeKind.Utc));

        atSea.Depart(new DateTime(2026, 7, 20, 9, 0, 0, DateTimeKind.Utc));
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var response = await CreateQuery(dbContext).GetVoyagesAsync(
            new GetVoyagesRequest { From = WindowFrom, To = WindowTo },
            CancellationToken.None);

        Assert.Equal(0, response.TotalCount);
    }

    [Fact]
    public async Task Search_matches_part_of_the_voyage_number()
    {
        await using var dbContext = CreateDbContext();
        await SeedAsync(dbContext, "VOY-901", new DateTime(2026, 9, 7, 8, 0, 0, DateTimeKind.Utc));
        await SeedAsync(dbContext, "ATL-902", new DateTime(2026, 9, 8, 8, 0, 0, DateTimeKind.Utc));

        var response = await CreateQuery(dbContext).GetVoyagesAsync(
            new GetVoyagesRequest { From = WindowFrom, To = WindowTo, Search = "VOY-9" },
            CancellationToken.None);

        Assert.Equal(1, response.TotalCount);
        Assert.Equal("VOY-901", Assert.Single(response.Items).VoyageNumber);
    }

    [Fact]
    public async Task Blank_search_is_treated_as_no_filter()
    {
        await using var dbContext = CreateDbContext();
        await SeedAsync(dbContext, "VOY-911", new DateTime(2026, 9, 7, 8, 0, 0, DateTimeKind.Utc));
        await SeedAsync(dbContext, "ATL-912", new DateTime(2026, 9, 8, 8, 0, 0, DateTimeKind.Utc));

        var response = await CreateQuery(dbContext).GetVoyagesAsync(
            new GetVoyagesRequest { From = WindowFrom, To = WindowTo, Search = "   " },
            CancellationToken.None);

        Assert.Equal(2, response.TotalCount);
    }

    [Fact]
    public async Task Results_are_ordered_by_planned_departure_then_voyage_id()
    {
        await using var dbContext = CreateDbContext();
        var sharedDeparture = new DateTime(2026, 9, 10, 8, 0, 0, DateTimeKind.Utc);
        var later = await SeedAsync(
            dbContext,
            "VOY-533",
            new DateTime(2026, 9, 12, 8, 0, 0, DateTimeKind.Utc));
        var first = await SeedAsync(dbContext, "VOY-531", sharedDeparture);
        var second = await SeedAsync(dbContext, "VOY-532", sharedDeparture);

        var response = await CreateQuery(dbContext).GetVoyagesAsync(
            new GetVoyagesRequest { From = WindowFrom, To = WindowTo },
            CancellationToken.None);

        var expected = new[] { first.Id, second.Id }
            .OrderBy(voyageId => voyageId)
            .Append(later.Id)
            .ToArray();

        Assert.Equal(expected, response.Items.Select(item => item.VoyageId));
    }

    [Fact]
    public async Task Paging_returns_requested_slice_and_total_count()
    {
        await using var dbContext = CreateDbContext();
        await SeedAsync(dbContext, "VOY-541", new DateTime(2026, 9, 1, 8, 0, 0, DateTimeKind.Utc));
        await SeedAsync(dbContext, "VOY-542", new DateTime(2026, 9, 2, 8, 0, 0, DateTimeKind.Utc));
        await SeedAsync(dbContext, "VOY-543", new DateTime(2026, 9, 3, 8, 0, 0, DateTimeKind.Utc));

        var response = await CreateQuery(dbContext).GetVoyagesAsync(
            new GetVoyagesRequest
            {
                From = WindowFrom,
                To = WindowTo,
                PageNumber = 2,
                PageSize = 2,
            },
            CancellationToken.None);

        Assert.Equal(3, response.TotalCount);
        Assert.Equal(2, response.PageNumber);
        Assert.Equal(2, response.PageSize);
        Assert.Equal("VOY-543", Assert.Single(response.Items).VoyageNumber);
    }

    [Fact]
    public async Task Paging_defaults_to_first_page_of_twenty_five()
    {
        await using var dbContext = CreateDbContext();
        await SeedAsync(dbContext, "VOY-551", new DateTime(2026, 9, 1, 8, 0, 0, DateTimeKind.Utc));

        var response = await CreateQuery(dbContext).GetVoyagesAsync(
            new GetVoyagesRequest { From = WindowFrom, To = WindowTo },
            CancellationToken.None);

        Assert.Equal(GetVoyagesValidator.DefaultPageNumber, response.PageNumber);
        Assert.Equal(GetVoyagesValidator.DefaultPageSize, response.PageSize);
    }

    [Fact]
    public async Task Actual_dates_are_null_until_they_are_recorded()
    {
        await using var dbContext = CreateDbContext();
        await SeedAsync(dbContext, "VOY-561", new DateTime(2026, 9, 1, 8, 0, 0, DateTimeKind.Utc));
        var completed = await SeedAsync(
            dbContext,
            "VOY-562",
            new DateTime(2026, 9, 2, 8, 0, 0, DateTimeKind.Utc));

        var actualDeparture = new DateTime(2026, 9, 2, 9, 0, 0, DateTimeKind.Utc);
        var actualArrival = new DateTime(2026, 9, 5, 9, 0, 0, DateTimeKind.Utc);
        completed.Depart(actualDeparture);
        completed.Arrive(actualArrival);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var response = await CreateQuery(dbContext).GetVoyagesAsync(
            new GetVoyagesRequest { From = WindowFrom, To = WindowTo },
            CancellationToken.None);

        var planned = response.Items.Single(item => item.VoyageNumber == "VOY-561");
        var finished = response.Items.Single(item => item.VoyageNumber == "VOY-562");

        Assert.Null(planned.ActualDeparture);
        Assert.Null(planned.ActualArrival);
        Assert.Equal(actualDeparture, finished.ActualDeparture);
        Assert.Equal(actualArrival, finished.ActualArrival);
    }

    [Fact]
    public async Task Row_exposes_scalar_vessel_id_and_route_codes()
    {
        await using var dbContext = CreateDbContext();
        var voyage = await SeedAsync(
            dbContext,
            "VOY-571",
            new DateTime(2026, 9, 1, 8, 0, 0, DateTimeKind.Utc));

        var response = await CreateQuery(dbContext).GetVoyagesAsync(
            new GetVoyagesRequest { From = WindowFrom, To = WindowTo },
            CancellationToken.None);

        var item = Assert.Single(response.Items);

        Assert.Equal(voyage.Id, item.VoyageId);
        Assert.Equal(voyage.VesselId, item.VesselId);
        Assert.Equal("MERIJ", item.DeparturePortCode);
        Assert.Equal("ITGOA", item.ArrivalPortCode);
        Assert.Equal(Voyage.PlannedStatus, item.Status);
        Assert.Equal(voyage.PlannedArrival, item.PlannedArrival);
    }

    [Fact]
    public async Task Row_exposes_first_and_last_itinerary_stop_port_names_when_planned()
    {
        await using var dbContext = CreateDbContext();
        var voyage = await SeedAsync(
            dbContext,
            "VOY-581",
            new DateTime(2026, 9, 1, 8, 0, 0, DateTimeKind.Utc));
        var bar = Port.Create("Port of Bar", "Montenegro");
        var kotor = Port.Create("Port of Kotor", "Montenegro");
        var dubrovnik = Port.Create("Port of Dubrovnik", "Croatia");
        await dbContext.Set<Port>().AddRangeAsync([bar, kotor, dubrovnik]);
        await dbContext.Set<VoyageStop>().AddRangeAsync(
            [
                VoyageStop.Create(voyage.Id, bar.Id, 1, voyage.PlannedDeparture, voyage.PlannedDeparture.AddHours(4)),
                VoyageStop.Create(voyage.Id, kotor.Id, 2, voyage.PlannedDeparture.AddHours(10), voyage.PlannedDeparture.AddHours(14)),
                VoyageStop.Create(voyage.Id, dubrovnik.Id, 3, voyage.PlannedDeparture.AddHours(20), voyage.PlannedDeparture.AddHours(24)),
            ]);
        await dbContext.SaveChangesAsync();

        var response = await CreateQuery(dbContext).GetVoyagesAsync(
            new GetVoyagesRequest { From = WindowFrom, To = WindowTo },
            CancellationToken.None);

        var item = Assert.Single(response.Items);

        Assert.Equal("Port of Bar", item.FirstStopPortName);
        Assert.Equal("Port of Dubrovnik", item.LastStopPortName);
    }

    [Fact]
    public async Task Row_leaves_stop_port_names_null_when_no_itinerary_is_planned()
    {
        await using var dbContext = CreateDbContext();
        await SeedAsync(dbContext, "VOY-582", new DateTime(2026, 9, 1, 8, 0, 0, DateTimeKind.Utc));

        var response = await CreateQuery(dbContext).GetVoyagesAsync(
            new GetVoyagesRequest { From = WindowFrom, To = WindowTo },
            CancellationToken.None);

        var item = Assert.Single(response.Items);

        Assert.Null(item.FirstStopPortName);
        Assert.Null(item.LastStopPortName);
        Assert.Empty(item.Stops);
    }

    [Fact]
    public async Task Row_exposes_itinerary_stops_in_order_with_port_coordinates()
    {
        await using var dbContext = CreateDbContext();
        var voyage = await SeedAsync(
            dbContext,
            "VOY-583",
            new DateTime(2026, 9, 1, 8, 0, 0, DateTimeKind.Utc));
        var bar = Port.Create("Port of Bar", "Montenegro", latitude: 42.0936m, longitude: 19.0936m);
        var dubrovnik = Port.Create("Port of Dubrovnik", "Croatia");
        await dbContext.Set<Port>().AddRangeAsync([bar, dubrovnik]);
        await dbContext.Set<VoyageStop>().AddRangeAsync(
            [
                VoyageStop.Create(voyage.Id, dubrovnik.Id, 2, voyage.PlannedDeparture.AddHours(20), voyage.PlannedDeparture.AddHours(24)),
                VoyageStop.Create(voyage.Id, bar.Id, 1, voyage.PlannedDeparture, voyage.PlannedDeparture.AddHours(4)),
            ]);
        await dbContext.SaveChangesAsync();

        var response = await CreateQuery(dbContext).GetVoyagesAsync(
            new GetVoyagesRequest { From = WindowFrom, To = WindowTo },
            CancellationToken.None);

        var item = Assert.Single(response.Items);

        Assert.Equal(
            [
                new VoyageListStopItem(1, "Port of Bar", VoyageStop.PlannedStatus, 42.0936m, 19.0936m),
                new VoyageListStopItem(2, "Port of Dubrovnik", VoyageStop.PlannedStatus, null, null),
            ],
            item.Stops);
    }

    [Fact]
    public async Task Query_rejects_to_date_before_from_date()
    {
        await using var dbContext = CreateDbContext();

        var exception = await Assert.ThrowsAsync<GetVoyagesValidationException>(
            () => CreateQuery(dbContext).GetVoyagesAsync(
                new GetVoyagesRequest { From = WindowTo, To = WindowFrom },
                CancellationToken.None));

        Assert.Contains(GetVoyagesValidator.ToField, exception.Errors);
    }

    [Fact]
    public async Task Query_rejects_range_longer_than_the_maximum()
    {
        await using var dbContext = CreateDbContext();

        var exception = await Assert.ThrowsAsync<GetVoyagesValidationException>(
            () => CreateQuery(dbContext).GetVoyagesAsync(
                new GetVoyagesRequest
                {
                    From = WindowFrom,
                    To = WindowFrom.AddDays(GetVoyagesValidator.MaxRangeDays + 1),
                },
                CancellationToken.None));

        Assert.Contains(GetVoyagesValidator.ToField, exception.Errors);
    }

    [Fact]
    public async Task Query_rejects_missing_dates()
    {
        await using var dbContext = CreateDbContext();

        var exception = await Assert.ThrowsAsync<GetVoyagesValidationException>(
            () => CreateQuery(dbContext).GetVoyagesAsync(
                new GetVoyagesRequest(),
                CancellationToken.None));

        Assert.Contains(GetVoyagesValidator.FromField, exception.Errors);
        Assert.Contains(GetVoyagesValidator.ToField, exception.Errors);
    }

    [Fact]
    public async Task Query_rejects_invalid_paging()
    {
        await using var dbContext = CreateDbContext();

        var exception = await Assert.ThrowsAsync<GetVoyagesValidationException>(
            () => CreateQuery(dbContext).GetVoyagesAsync(
                new GetVoyagesRequest
                {
                    From = WindowFrom,
                    To = WindowTo,
                    PageNumber = 0,
                    PageSize = GetVoyagesValidator.MaxPageSize + 1,
                },
                CancellationToken.None));

        Assert.Contains(GetVoyagesValidator.PageNumberField, exception.Errors);
        Assert.Contains(GetVoyagesValidator.PageSizeField, exception.Errors);
    }

    [Fact]
    public async Task Delay_impact_filter_returns_only_voyages_with_an_applied_impact()
    {
        await using var dbContext = CreateDbContext();
        var untouched = await SeedAsync(
            dbContext,
            "VOY-561",
            new DateTime(2026, 9, 7, 8, 0, 0, DateTimeKind.Utc));
        var revised = await SeedAsync(
            dbContext,
            "VOY-562",
            new DateTime(2026, 9, 8, 8, 0, 0, DateTimeKind.Utc));

        await SeedDelayAsync(dbContext, untouched.Id);
        await SeedAppliedDelayImpactAsync(dbContext, revised.Id);

        var response = await CreateQuery(dbContext).GetVoyagesAsync(
            new GetVoyagesRequest
            {
                From = WindowFrom,
                To = WindowTo,
                DelayImpactApplied = true,
            },
            CancellationToken.None);

        Assert.Equal(1, response.TotalCount);
        Assert.Equal("VOY-562", Assert.Single(response.Items).VoyageNumber);
    }

    [Fact]
    public async Task Delay_impact_filter_is_opt_in()
    {
        await using var dbContext = CreateDbContext();
        await SeedAsync(dbContext, "VOY-571", new DateTime(2026, 9, 9, 8, 0, 0, DateTimeKind.Utc));
        var revised = await SeedAsync(
            dbContext,
            "VOY-572",
            new DateTime(2026, 9, 10, 8, 0, 0, DateTimeKind.Utc));

        await SeedAppliedDelayImpactAsync(dbContext, revised.Id);

        var response = await CreateQuery(dbContext).GetVoyagesAsync(
            new GetVoyagesRequest { From = WindowFrom, To = WindowTo },
            CancellationToken.None);

        Assert.Equal(2, response.TotalCount);
    }


    private static IGetVoyagesQuery CreateQuery(HarborOpsDbContext dbContext)
    {
        return new GetVoyagesQuery(dbContext);
    }

    private static async Task<Voyage> SeedAsync(
        HarborOpsDbContext dbContext,
        string voyageNumber,
        DateTime plannedDeparture)
    {
        var voyage = Voyage.Schedule(
            Guid.NewGuid(),
            voyageNumber,
            "MERIJ",
            "ITGOA",
            plannedDeparture,
            plannedDeparture.AddDays(3));

        await dbContext.Set<Voyage>().AddAsync(voyage);
        await dbContext.SaveChangesAsync();

        return voyage;
    }

    private static async Task<Delay> SeedDelayAsync(HarborOpsDbContext dbContext, Guid voyageId)
    {
        var delay = Delay.Create(
            voyageId,
            "Weather",
            120,
            new DateTime(2026, 9, 11, 6, 0, 0, DateTimeKind.Utc));

        await dbContext.Set<Delay>().AddAsync(delay);
        await dbContext.SaveChangesAsync();

        return delay;
    }

    private static async Task SeedAppliedDelayImpactAsync(HarborOpsDbContext dbContext, Guid voyageId)
    {
        var delay = await SeedDelayAsync(dbContext, voyageId);

        var batch = VoyageEstimateRevisionBatch.Record(
            voyageId,
            delay.Id,
            "Weather delay applied",
            Guid.NewGuid(),
            new DateTime(2026, 9, 11, 7, 0, 0, DateTimeKind.Utc));

        await dbContext.Set<VoyageEstimateRevisionBatch>().AddAsync(batch);
        await dbContext.SaveChangesAsync();
    }

    private static HarborOpsDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<HarborOpsDbContext>()
            .UseInMemoryDatabase($"HarborOpsUnitTests-{Guid.NewGuid()}")
            .Options;

        return new HarborOpsDbContext(options);
    }
}
