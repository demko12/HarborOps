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

    private static HarborOpsDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<HarborOpsDbContext>()
            .UseInMemoryDatabase($"HarborOpsUnitTests-{Guid.NewGuid()}")
            .Options;

        return new HarborOpsDbContext(options);
    }
}
