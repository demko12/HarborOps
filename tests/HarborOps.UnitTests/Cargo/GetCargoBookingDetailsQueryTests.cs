using HarborOps.Infrastructure.Persistence;
using HarborOps.Modules.Cargo.Application;
using HarborOps.Modules.Cargo.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.UnitTests.Cargo;

public sealed class GetCargoBookingDetailsQueryTests
{
    private static readonly Guid VoyageId = Guid.Parse("7f6e5d4c-3b2a-4190-8f7e-6d5c4b3a2190");
    private static readonly Guid ResponsibleUserId = Guid.Parse("1a2b3c4d-5e6f-4a7b-8c9d-0e1f2a3b4c5d");
    private static readonly DateTime FirstEventUtc = new(2026, 3, 1, 8, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task GetDetailsAsync_returns_every_booking_field()
    {
        await using var dbContext = CreateDbContext();
        var booking = await AddBookingAsync(
            dbContext,
            "BK-DETAILS-0001",
            description: "Ammonium nitrate",
            cargoType: "Dangerous Goods",
            isHazardous: true);
        var query = new GetCargoBookingDetailsQuery(dbContext);

        var response = await query.GetDetailsAsync(booking.Id, CancellationToken.None);

        Assert.Equal(booking.Id, response.CargoBookingId);
        Assert.Equal("BK-DETAILS-0001", response.BookingNumber);
        Assert.Equal(VoyageId, response.VoyageId);
        Assert.Equal("Ammonium nitrate", response.Description);
        Assert.Equal("Dangerous Goods", response.CargoType);
        Assert.Equal(5000.000m, response.WeightKg);
        Assert.Equal(10, response.PackageCount);
        Assert.True(response.IsHazardous);
        Assert.Equal(nameof(CargoStatus.Registered), response.Status);
        Assert.Equal(booking.CreatedUtc, response.CreatedUtc);
    }

    [Fact]
    public async Task GetDetailsAsync_offers_the_single_next_status()
    {
        await using var dbContext = CreateDbContext();
        var booking = await AddBookingAsync(dbContext, "BK-DETAILS-0002", status: CargoStatus.Loaded);
        var query = new GetCargoBookingDetailsQuery(dbContext);

        var response = await query.GetDetailsAsync(booking.Id, CancellationToken.None);

        Assert.Equal(nameof(CargoStatus.Loaded), response.Status);
        Assert.Equal(nameof(CargoStatus.InTransit), response.NextStatus);
    }

    [Fact]
    public async Task GetDetailsAsync_offers_no_next_status_for_a_completed_booking()
    {
        await using var dbContext = CreateDbContext();
        var booking = await AddBookingAsync(dbContext, "BK-DETAILS-0003", status: CargoStatus.Completed);
        var query = new GetCargoBookingDetailsQuery(dbContext);

        var response = await query.GetDetailsAsync(booking.Id, CancellationToken.None);

        Assert.Equal(nameof(CargoStatus.Completed), response.Status);
        Assert.Null(response.NextStatus);
    }

    [Fact]
    public async Task GetDetailsAsync_returns_no_safety_information_when_it_was_never_declared()
    {
        await using var dbContext = CreateDbContext();
        var booking = await AddBookingAsync(dbContext, "BK-DETAILS-0004", isHazardous: true);
        var query = new GetCargoBookingDetailsQuery(dbContext);

        var response = await query.GetDetailsAsync(booking.Id, CancellationToken.None);

        Assert.Null(response.SafetyInfo);
    }

    [Fact]
    public async Task GetDetailsAsync_returns_declared_safety_information()
    {
        await using var dbContext = CreateDbContext();
        var booking = await AddBookingAsync(dbContext, "BK-DETAILS-0005", isHazardous: true);
        var declaration = CargoSafetyInfo.Declare(
            booking.Id,
            "1.4s",
            "un1234",
            "Keep below 25 C",
            "Segregate from foodstuffs");
        await dbContext.Set<CargoSafetyInfo>().AddAsync(declaration, CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var query = new GetCargoBookingDetailsQuery(dbContext);

        var response = await query.GetDetailsAsync(booking.Id, CancellationToken.None);

        Assert.NotNull(response.SafetyInfo);
        Assert.Equal(declaration.Id, response.SafetyInfo.CargoSafetyInfoId);
        Assert.Equal("1.4S", response.SafetyInfo.ImdgClass);
        Assert.Equal("UN1234", response.SafetyInfo.UnNumber);
        Assert.Equal("Keep below 25 C", response.SafetyInfo.StorageConditions);
        Assert.Equal("Segregate from foodstuffs", response.SafetyInfo.HandlingNotes);
    }

    [Fact]
    public async Task GetDetailsAsync_returns_no_tracking_events_for_a_new_booking()
    {
        await using var dbContext = CreateDbContext();
        var booking = await AddBookingAsync(dbContext, "BK-DETAILS-0006");
        var query = new GetCargoBookingDetailsQuery(dbContext);

        var response = await query.GetDetailsAsync(booking.Id, CancellationToken.None);

        Assert.Empty(response.TrackingEvents);
    }

    [Fact]
    public async Task GetDetailsAsync_orders_tracking_events_by_occurrence()
    {
        await using var dbContext = CreateDbContext();
        var booking = await AddBookingAsync(dbContext, "BK-DETAILS-0007");
        await AddTrackingEventAsync(
            dbContext,
            booking.Id,
            CargoStatus.Loaded,
            FirstEventUtc.AddHours(6),
            "Berth 12");
        await AddTrackingEventAsync(
            dbContext,
            booking.Id,
            CargoStatus.Planned,
            FirstEventUtc,
            "Yard A");
        var query = new GetCargoBookingDetailsQuery(dbContext);

        var response = await query.GetDetailsAsync(booking.Id, CancellationToken.None);

        Assert.Equal(
            [nameof(CargoStatus.Planned), nameof(CargoStatus.Loaded)],
            response.TrackingEvents.Select(trackingEvent => trackingEvent.Status));
        Assert.Equal(
            [FirstEventUtc, FirstEventUtc.AddHours(6)],
            response.TrackingEvents.Select(trackingEvent => trackingEvent.OccurredUtc));
    }

    [Fact]
    public async Task GetDetailsAsync_breaks_occurrence_ties_by_identifier()
    {
        await using var dbContext = CreateDbContext();
        var booking = await AddBookingAsync(dbContext, "BK-DETAILS-0008");
        await AddTrackingEventAsync(dbContext, booking.Id, CargoStatus.Planned, FirstEventUtc, "Yard A");
        await AddTrackingEventAsync(dbContext, booking.Id, CargoStatus.Loaded, FirstEventUtc, "Berth 12");
        var query = new GetCargoBookingDetailsQuery(dbContext);

        var response = await query.GetDetailsAsync(booking.Id, CancellationToken.None);

        Assert.Equal(
            response.TrackingEvents.Select(trackingEvent => trackingEvent.CargoTrackingEventId).Order(),
            response.TrackingEvents.Select(trackingEvent => trackingEvent.CargoTrackingEventId));
    }

    [Fact]
    public async Task GetDetailsAsync_returns_the_responsible_user_without_resolving_identity()
    {
        await using var dbContext = CreateDbContext();
        var booking = await AddBookingAsync(dbContext, "BK-DETAILS-0009");
        await AddTrackingEventAsync(
            dbContext,
            booking.Id,
            CargoStatus.Planned,
            FirstEventUtc,
            " Yard A ");
        var query = new GetCargoBookingDetailsQuery(dbContext);

        var response = await query.GetDetailsAsync(booking.Id, CancellationToken.None);

        var trackingEvent = Assert.Single(response.TrackingEvents);

        Assert.Equal(ResponsibleUserId, trackingEvent.ResponsibleUserId);
        Assert.Equal(" Yard A ", trackingEvent.Location);
    }

    [Fact]
    public async Task GetDetailsAsync_ignores_records_belonging_to_another_booking()
    {
        await using var dbContext = CreateDbContext();
        var booking = await AddBookingAsync(dbContext, "BK-DETAILS-0010");
        var otherBooking = await AddBookingAsync(dbContext, "BK-DETAILS-0011", isHazardous: true);
        await AddTrackingEventAsync(
            dbContext,
            otherBooking.Id,
            CargoStatus.Planned,
            FirstEventUtc,
            "Yard B");
        await dbContext.Set<CargoSafetyInfo>().AddAsync(
            CargoSafetyInfo.Declare(otherBooking.Id, "1.4S", "UN1234", "Cool", null),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var query = new GetCargoBookingDetailsQuery(dbContext);

        var response = await query.GetDetailsAsync(booking.Id, CancellationToken.None);

        Assert.Null(response.SafetyInfo);
        Assert.Empty(response.TrackingEvents);
    }

    [Fact]
    public async Task GetDetailsAsync_rejects_an_unknown_booking()
    {
        await using var dbContext = CreateDbContext();
        var query = new GetCargoBookingDetailsQuery(dbContext);

        await Assert.ThrowsAsync<CargoBookingNotFoundException>(
            () => query.GetDetailsAsync(Guid.NewGuid(), CancellationToken.None));
    }

    private static async Task<CargoBooking> AddBookingAsync(
        HarborOpsDbContext dbContext,
        string bookingNumber,
        string description = "Steel coils",
        string cargoType = "General Cargo",
        bool isHazardous = false,
        CargoStatus status = CargoStatus.Registered)
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

        return booking;
    }

    private static async Task AddTrackingEventAsync(
        HarborOpsDbContext dbContext,
        Guid cargoBookingId,
        CargoStatus status,
        DateTime occurredUtc,
        string location)
    {
        var trackingEvent = CargoTrackingEvent.Record(
            cargoBookingId,
            ResponsibleUserId,
            status,
            occurredUtc,
            location);

        await dbContext.Set<CargoTrackingEvent>().AddAsync(trackingEvent, CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);
    }

    private static HarborOpsDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<HarborOpsDbContext>()
            .UseInMemoryDatabase($"HarborOpsUnitTests-{Guid.NewGuid()}")
            .Options;

        return new HarborOpsDbContext(options);
    }
}
