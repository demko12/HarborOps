using HarborOps.Infrastructure.Persistence;
using HarborOps.Modules.Cargo.Application;
using HarborOps.Modules.Cargo.Contracts;
using HarborOps.Modules.Cargo.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.UnitTests.Cargo;

public sealed class SearchCargoBookingsQueryTests
{
    private static readonly Guid VoyageId = Guid.Parse("4a3b2c1d-0e9f-4a8b-9c7d-6e5f4a3b2c1d");

    [Fact]
    public async Task SearchAsync_returns_every_booking_field()
    {
        await using var dbContext = CreateDbContext();
        var booking = await AddBookingAsync(
            dbContext,
            "BK-SEARCH-0001",
            description: "Ammonium nitrate",
            cargoType: "Dangerous Goods",
            isHazardous: true);
        var query = new SearchCargoBookingsQuery(dbContext);

        var response = await query.SearchAsync(
            new SearchCargoBookingsRequest(),
            CancellationToken.None);

        var item = Assert.Single(response.Items);

        Assert.Equal(booking.Id, item.CargoBookingId);
        Assert.Equal("BK-SEARCH-0001", item.BookingNumber);
        Assert.Equal(VoyageId, item.VoyageId);
        Assert.Equal("Ammonium nitrate", item.Description);
        Assert.Equal("Dangerous Goods", item.CargoType);
        Assert.Equal(5000.000m, item.WeightKg);
        Assert.Equal(10, item.PackageCount);
        Assert.True(item.IsHazardous);
        Assert.Equal(nameof(CargoStatus.Registered), item.Status);
        Assert.Equal(booking.CreatedUtc, item.CreatedUtc);
    }

    [Fact]
    public async Task SearchAsync_matches_the_booking_number()
    {
        await using var dbContext = CreateDbContext();
        await AddBookingAsync(dbContext, "BK-ALPHA-0001", description: "Steel coils");
        await AddBookingAsync(dbContext, "BK-BRAVO-0002", description: "Timber");
        var query = new SearchCargoBookingsQuery(dbContext);

        var response = await query.SearchAsync(
            new SearchCargoBookingsRequest { Search = "ALPHA" },
            CancellationToken.None);

        Assert.Equal(1, response.TotalCount);
        Assert.Equal("BK-ALPHA-0001", Assert.Single(response.Items).BookingNumber);
    }

    [Fact]
    public async Task SearchAsync_matches_the_description()
    {
        await using var dbContext = CreateDbContext();
        await AddBookingAsync(dbContext, "BK-ALPHA-0001", description: "Steel coils");
        await AddBookingAsync(dbContext, "BK-BRAVO-0002", description: "Timber");
        var query = new SearchCargoBookingsQuery(dbContext);

        var response = await query.SearchAsync(
            new SearchCargoBookingsRequest { Search = " Timber " },
            CancellationToken.None);

        Assert.Equal("BK-BRAVO-0002", Assert.Single(response.Items).BookingNumber);
    }

    [Fact]
    public async Task SearchAsync_filters_by_status()
    {
        await using var dbContext = CreateDbContext();
        await AddBookingAsync(dbContext, "BK-STATUS-0001");
        await AddBookingAsync(dbContext, "BK-STATUS-0002", status: CargoStatus.Loaded);
        var query = new SearchCargoBookingsQuery(dbContext);

        var response = await query.SearchAsync(
            new SearchCargoBookingsRequest { Status = "loaded" },
            CancellationToken.None);

        var item = Assert.Single(response.Items);

        Assert.Equal("BK-STATUS-0002", item.BookingNumber);
        Assert.Equal(nameof(CargoStatus.Loaded), item.Status);
    }

    [Fact]
    public async Task SearchAsync_prioritizes_active_cargo_statuses_before_completed_bookings()
    {
        await using var dbContext = CreateDbContext();
        await AddBookingAsync(dbContext, "BK-COMPLETED", status: CargoStatus.Completed);
        await AddBookingAsync(dbContext, "BK-REGISTERED", status: CargoStatus.Registered);
        await AddBookingAsync(dbContext, "BK-LOADED", status: CargoStatus.Loaded);
        await AddBookingAsync(dbContext, "BK-IN-TRANSIT", status: CargoStatus.InTransit);

        var response = await new SearchCargoBookingsQuery(dbContext).SearchAsync(
            new SearchCargoBookingsRequest(),
            CancellationToken.None);

        Assert.Equal(
            ["BK-IN-TRANSIT", "BK-LOADED", "BK-REGISTERED", "BK-COMPLETED"],
            response.Items.Select(item => item.BookingNumber));
    }

    [Fact]
    public async Task SearchAsync_filters_by_hazardous_cargo()
    {
        await using var dbContext = CreateDbContext();
        await AddBookingAsync(dbContext, "BK-HAZ-0001", isHazardous: true);
        await AddBookingAsync(dbContext, "BK-HAZ-0002", isHazardous: false);
        var query = new SearchCargoBookingsQuery(dbContext);

        var hazardous = await query.SearchAsync(
            new SearchCargoBookingsRequest { IsHazardous = true },
            CancellationToken.None);
        var regular = await query.SearchAsync(
            new SearchCargoBookingsRequest { IsHazardous = false },
            CancellationToken.None);

        Assert.Equal("BK-HAZ-0001", Assert.Single(hazardous.Items).BookingNumber);
        Assert.Equal("BK-HAZ-0002", Assert.Single(regular.Items).BookingNumber);
    }

    [Fact]
    public async Task SearchAsync_combines_filters()
    {
        await using var dbContext = CreateDbContext();
        await AddBookingAsync(
            dbContext,
            "BK-COMBO-0001",
            description: "Ammonium nitrate",
            isHazardous: true,
            status: CargoStatus.Loaded);
        await AddBookingAsync(
            dbContext,
            "BK-COMBO-0002",
            description: "Ammonium nitrate",
            isHazardous: false,
            status: CargoStatus.Loaded);
        await AddBookingAsync(
            dbContext,
            "BK-COMBO-0003",
            description: "Ammonium nitrate",
            isHazardous: true);
        var query = new SearchCargoBookingsQuery(dbContext);

        var response = await query.SearchAsync(
            new SearchCargoBookingsRequest
            {
                Search = "Ammonium",
                Status = nameof(CargoStatus.Loaded),
                IsHazardous = true,
            },
            CancellationToken.None);

        Assert.Equal("BK-COMBO-0001", Assert.Single(response.Items).BookingNumber);
    }

    [Fact]
    public async Task SearchAsync_orders_by_creation_time_descending()
    {
        await using var dbContext = CreateDbContext();
        var oldest = await AddBookingAsync(dbContext, "BK-ORDER-0001", createdUtc: new DateTime(2026, 8, 1, 8, 0, 0, DateTimeKind.Utc));
        var newest = await AddBookingAsync(dbContext, "BK-ORDER-0002", createdUtc: new DateTime(2026, 8, 3, 8, 0, 0, DateTimeKind.Utc));
        var middle = await AddBookingAsync(dbContext, "BK-ORDER-0003", createdUtc: new DateTime(2026, 8, 2, 8, 0, 0, DateTimeKind.Utc));
        var query = new SearchCargoBookingsQuery(dbContext);

        var response = await query.SearchAsync(
            new SearchCargoBookingsRequest(),
            CancellationToken.None);

        Assert.Equal(
            [newest.Id, middle.Id, oldest.Id],
            response.Items.Select(item => item.CargoBookingId));
    }

    [Fact]
    public async Task SearchAsync_breaks_creation_time_ties_by_identifier()
    {
        await using var dbContext = CreateDbContext();
        var createdUtc = new DateTime(2026, 8, 1, 8, 0, 0, DateTimeKind.Utc);
        await AddBookingAsync(dbContext, "BK-TIE-0001", createdUtc: createdUtc);
        await AddBookingAsync(dbContext, "BK-TIE-0002", createdUtc: createdUtc);
        await AddBookingAsync(dbContext, "BK-TIE-0003", createdUtc: createdUtc);
        var query = new SearchCargoBookingsQuery(dbContext);

        var response = await query.SearchAsync(
            new SearchCargoBookingsRequest(),
            CancellationToken.None);

        Assert.Equal(
            response.Items.Select(item => item.CargoBookingId).Order(),
            response.Items.Select(item => item.CargoBookingId));
    }

    [Fact]
    public async Task SearchAsync_pages_the_result()
    {
        await using var dbContext = CreateDbContext();
        for (var index = 0; index < 5; index++)
        {
            await AddBookingAsync(
                dbContext,
                $"BK-PAGE-{index:D4}",
                createdUtc: new DateTime(2026, 8, 1, 8, 0, 0, DateTimeKind.Utc).AddMinutes(index));
        }

        var query = new SearchCargoBookingsQuery(dbContext);

        var firstPage = await query.SearchAsync(
            new SearchCargoBookingsRequest { PageNumber = 1, PageSize = 2 },
            CancellationToken.None);
        var secondPage = await query.SearchAsync(
            new SearchCargoBookingsRequest { PageNumber = 2, PageSize = 2 },
            CancellationToken.None);
        var lastPage = await query.SearchAsync(
            new SearchCargoBookingsRequest { PageNumber = 3, PageSize = 2 },
            CancellationToken.None);

        Assert.Equal(5, firstPage.TotalCount);
        Assert.Equal(2, firstPage.Items.Count);
        Assert.Equal(2, secondPage.Items.Count);
        Assert.Single(lastPage.Items);
        Assert.Equal("BK-PAGE-0004", firstPage.Items[0].BookingNumber);
        Assert.Equal("BK-PAGE-0000", lastPage.Items[0].BookingNumber);
        Assert.Equal(2, secondPage.PageNumber);
        Assert.Equal(2, secondPage.PageSize);
    }

    [Fact]
    public async Task SearchAsync_applies_the_default_page_when_none_is_requested()
    {
        await using var dbContext = CreateDbContext();
        await AddBookingAsync(dbContext, "BK-DEFAULT-0001");
        var query = new SearchCargoBookingsQuery(dbContext);

        var response = await query.SearchAsync(
            new SearchCargoBookingsRequest(),
            CancellationToken.None);

        Assert.Equal(SearchCargoBookingsValidator.DefaultPageNumber, response.PageNumber);
        Assert.Equal(SearchCargoBookingsValidator.DefaultPageSize, response.PageSize);
    }

    [Fact]
    public async Task SearchAsync_returns_an_empty_page_without_data()
    {
        await using var dbContext = CreateDbContext();
        var query = new SearchCargoBookingsQuery(dbContext);

        var response = await query.SearchAsync(
            new SearchCargoBookingsRequest { Search = "nothing matches" },
            CancellationToken.None);

        Assert.Empty(response.Items);
        Assert.Equal(0, response.TotalCount);
    }

    [Fact]
    public async Task SearchAsync_rejects_an_unknown_status()
    {
        await using var dbContext = CreateDbContext();
        var query = new SearchCargoBookingsQuery(dbContext);

        var exception = await Assert.ThrowsAsync<SearchCargoBookingsValidationException>(
            () => query.SearchAsync(
                new SearchCargoBookingsRequest { Status = "Departed" },
                CancellationToken.None));

        Assert.Contains(SearchCargoBookingsValidator.StatusField, exception.Errors);
    }

    [Theory]
    [InlineData(0, null)]
    [InlineData(null, 0)]
    [InlineData(null, SearchCargoBookingsValidator.MaxPageSize + 1)]
    public async Task SearchAsync_rejects_invalid_paging(int? pageNumber, int? pageSize)
    {
        await using var dbContext = CreateDbContext();
        var query = new SearchCargoBookingsQuery(dbContext);

        var exception = await Assert.ThrowsAsync<SearchCargoBookingsValidationException>(
            () => query.SearchAsync(
                new SearchCargoBookingsRequest { PageNumber = pageNumber, PageSize = pageSize },
                CancellationToken.None));

        Assert.NotEmpty(exception.Errors);
    }

    [Fact]
    public async Task SearchAsync_accepts_the_maximum_page_size()
    {
        await using var dbContext = CreateDbContext();
        var query = new SearchCargoBookingsQuery(dbContext);

        var response = await query.SearchAsync(
            new SearchCargoBookingsRequest { PageSize = SearchCargoBookingsValidator.MaxPageSize },
            CancellationToken.None);

        Assert.Equal(SearchCargoBookingsValidator.MaxPageSize, response.PageSize);
    }

    [Fact]
    public async Task SearchAsync_counts_every_hazardous_booking_beyond_the_current_page()
    {
        await using var dbContext = CreateDbContext();
        for (var index = 0; index < 5; index++)
        {
            await AddBookingAsync(
                dbContext,
                $"BK-HAZCOUNT-{index:D4}",
                isHazardous: index % 2 == 0,
                createdUtc: new DateTime(2026, 8, 1, 8, 0, 0, DateTimeKind.Utc).AddMinutes(index));
        }

        var query = new SearchCargoBookingsQuery(dbContext);

        var response = await query.SearchAsync(
            new SearchCargoBookingsRequest { PageNumber = 2, PageSize = 2 },
            CancellationToken.None);

        Assert.Equal(5, response.TotalCount);
        Assert.Equal(3, response.HazardousCount);
        Assert.Equal(2, response.Items.Count);
    }

    [Fact]
    public async Task SearchAsync_counts_hazardous_bookings_within_the_active_filters()
    {
        await using var dbContext = CreateDbContext();
        await AddBookingAsync(dbContext, "BK-HAZFILTER-0001", isHazardous: true);
        await AddBookingAsync(dbContext, "BK-HAZFILTER-0002", isHazardous: true, status: CargoStatus.Loaded);
        await AddBookingAsync(dbContext, "BK-HAZFILTER-0003", isHazardous: false);

        var response = await new SearchCargoBookingsQuery(dbContext).SearchAsync(
            new SearchCargoBookingsRequest { Status = nameof(CargoStatus.Registered) },
            CancellationToken.None);

        Assert.Equal(2, response.TotalCount);
        Assert.Equal(1, response.HazardousCount);
    }

    [Fact]
    public async Task SearchAsync_reports_no_hazardous_bookings_without_data()
    {
        await using var dbContext = CreateDbContext();

        var response = await new SearchCargoBookingsQuery(dbContext).SearchAsync(
            new SearchCargoBookingsRequest(),
            CancellationToken.None);

        Assert.Equal(0, response.HazardousCount);
    }

    [Fact]
    public async Task SearchAsync_counts_only_reported_and_investigating_discrepancies_per_booking()
    {
        await using var dbContext = CreateDbContext();
        var blocked = await AddBookingAsync(dbContext, "BK-DISC-0001", status: CargoStatus.Unloaded);
        var clear = await AddBookingAsync(dbContext, "BK-DISC-0002", status: CargoStatus.Unloaded);
        await AddDiscrepancyAsync(dbContext, blocked);
        await AddDiscrepancyAsync(dbContext, blocked, investigate: true);
        await AddDiscrepancyAsync(dbContext, blocked, voided: true);
        await AddDiscrepancyAsync(dbContext, clear, voided: true);

        var response = await new SearchCargoBookingsQuery(dbContext).SearchAsync(
            new SearchCargoBookingsRequest(),
            CancellationToken.None);

        Assert.Equal(2, response.Items.Single(item => item.CargoBookingId == blocked.Id).OpenDiscrepancyCount);
        Assert.Equal(0, response.Items.Single(item => item.CargoBookingId == clear.Id).OpenDiscrepancyCount);
        Assert.Equal(1, response.OpenDiscrepancyBookingCount);
    }

    [Theory]
    [InlineData(true, "BK-DISC-0003")]
    [InlineData(false, "BK-DISC-0004")]
    public async Task SearchAsync_filters_by_open_discrepancies(bool hasOpenDiscrepancies, string expectedBookingNumber)
    {
        await using var dbContext = CreateDbContext();
        var blocked = await AddBookingAsync(dbContext, "BK-DISC-0003", status: CargoStatus.Arrived);
        await AddBookingAsync(dbContext, "BK-DISC-0004", status: CargoStatus.Arrived);
        await AddDiscrepancyAsync(dbContext, blocked);

        var response = await new SearchCargoBookingsQuery(dbContext).SearchAsync(
            new SearchCargoBookingsRequest { HasOpenDiscrepancies = hasOpenDiscrepancies },
            CancellationToken.None);

        Assert.Equal(expectedBookingNumber, Assert.Single(response.Items).BookingNumber);
        Assert.Equal(1, response.TotalCount);
    }

    private static async Task AddDiscrepancyAsync(
        HarborOpsDbContext dbContext,
        CargoBooking booking,
        bool investigate = false,
        bool voided = false)
    {
        var utcNow = DateTime.UtcNow;
        var (discrepancy, reported) = CargoDiscrepancy.Register(
            booking,
            null,
            null,
            CargoDiscrepancyType.Shortage,
            CargoDiscrepancySeverity.Medium,
            "Cartons missing",
            utcNow.AddHours(-1),
            Guid.NewGuid(),
            null,
            utcNow);

        if (investigate)
        {
            discrepancy.StartInvestigation(Guid.NewGuid(), null, utcNow);
        }

        if (voided)
        {
            discrepancy.Void("Registered twice", Guid.NewGuid(), null, utcNow);
        }

        await dbContext.Set<CargoDiscrepancy>().AddAsync(discrepancy, CancellationToken.None);
        await dbContext.Set<CargoDiscrepancyEvent>().AddAsync(reported, CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);
    }

    private static async Task<CargoBooking> AddBookingAsync(
        HarborOpsDbContext dbContext,
        string bookingNumber,
        string description = "Steel coils",
        string cargoType = "General Cargo",
        bool isHazardous = false,
        CargoStatus status = CargoStatus.Registered,
        DateTime? createdUtc = null)
    {
        var booking = CargoBooking.Register(
            bookingNumber,
            VoyageId,
            description,
            cargoType,
            5000.000m,
            10,
            isHazardous);

        while (booking.Status != status)
        {
            booking.AdvanceStatusTo(booking.Status + 1);
        }

        await dbContext.Set<CargoBooking>().AddAsync(booking, CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        if (createdUtc is { } requestedCreatedUtc)
        {
            dbContext.Entry(booking).Property(nameof(CargoBooking.CreatedUtc)).CurrentValue =
                requestedCreatedUtc;
            await dbContext.SaveChangesAsync(CancellationToken.None);
        }

        return booking;
    }

    private static HarborOpsDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<HarborOpsDbContext>()
            .UseInMemoryDatabase($"HarborOpsUnitTests-{Guid.NewGuid()}")
            .Options;

        return new HarborOpsDbContext(options);
    }
}
