using HarborOps.Infrastructure.Persistence;
using HarborOps.Modules.Cargo.Application;
using HarborOps.Modules.Cargo.Contracts;
using HarborOps.Modules.Cargo.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.UnitTests.Cargo;

public sealed class RecordCargoTrackingEventServiceTests
{
    private static readonly DateTimeOffset FixedNow = DateTimeOffset.UtcNow.AddMinutes(10);

    private static readonly Guid ResponsibleUserId =
        Guid.Parse("3f2a1b0c-9d8e-4f7a-8b6c-5d4e3f2a1b0c");

    private static readonly Guid VoyageId = Guid.Parse("5d4c3b2a-1e0f-4a9b-8c7d-6e5f4a3b2c1d");

    [Theory]
    [InlineData(nameof(CargoStatus.Registered), nameof(CargoStatus.Planned))]
    [InlineData(nameof(CargoStatus.Planned), nameof(CargoStatus.Loaded))]
    [InlineData(nameof(CargoStatus.Loaded), nameof(CargoStatus.InTransit))]
    [InlineData(nameof(CargoStatus.InTransit), nameof(CargoStatus.Arrived))]
    [InlineData(nameof(CargoStatus.Arrived), nameof(CargoStatus.Unloaded))]
    [InlineData(nameof(CargoStatus.Unloaded), nameof(CargoStatus.Completed))]
    public async Task RecordAsync_records_every_allowed_transition(
        string currentStatusName,
        string requestedStatusName)
    {
        var currentStatus = Enum.Parse<CargoStatus>(currentStatusName);
        var requestedStatus = Enum.Parse<CargoStatus>(requestedStatusName);

        await using var dbContext = CreateDbContext();
        var booking = await AddBookingAsync(dbContext, "BK-TRACK-0001", currentStatus);
        var service = CreateService(dbContext);

        var response = await service.RecordAsync(
            booking.Id,
            ResponsibleUserId,
            CreateValidRequest(requestedStatus),
            CancellationToken.None);

        var trackingEvent = await dbContext
            .Set<CargoTrackingEvent>()
            .SingleAsync(CancellationToken.None);
        var persistedBooking = await dbContext
            .Set<CargoBooking>()
            .SingleAsync(candidate => candidate.Id == booking.Id, CancellationToken.None);

        Assert.Equal(trackingEvent.Id, response.Id);
        Assert.Equal(requestedStatus.ToString(), response.Status);
        Assert.Equal(booking.Id, trackingEvent.CargoBookingId);
        Assert.Equal(ResponsibleUserId, trackingEvent.ResponsibleUserId);
        Assert.Equal(requestedStatus, trackingEvent.Status);
        Assert.Equal(requestedStatus, persistedBooking.Status);
    }

    [Fact]
    public async Task RecordAsync_records_the_full_cargo_lifecycle_in_order()
    {
        await using var dbContext = CreateDbContext();
        var booking = await AddBookingAsync(dbContext, "BK-TRACK-0002", CargoStatus.Registered);
        var service = CreateService(dbContext);
        CargoStatus[] lifecycle =
        [
            CargoStatus.Planned,
            CargoStatus.Loaded,
            CargoStatus.InTransit,
            CargoStatus.Arrived,
            CargoStatus.Unloaded,
            CargoStatus.Completed,
        ];

        for (var step = 0; step < lifecycle.Length; step++)
        {
            await service.RecordAsync(
                booking.Id,
                ResponsibleUserId,
                CreateValidRequest(lifecycle[step], OccurredUtc(step)),
                CancellationToken.None);
        }

        var persistedBooking = await dbContext
            .Set<CargoBooking>()
            .SingleAsync(candidate => candidate.Id == booking.Id, CancellationToken.None);

        Assert.Equal(CargoStatus.Completed, persistedBooking.Status);
        Assert.Equal(lifecycle.Length, await dbContext.Set<CargoTrackingEvent>().CountAsync());
    }

    [Fact]
    public async Task RecordAsync_rejects_a_skipped_status()
    {
        await using var dbContext = CreateDbContext();
        var booking = await AddBookingAsync(dbContext, "BK-TRACK-0003", CargoStatus.Registered);
        var service = CreateService(dbContext);

        var exception = await Assert.ThrowsAsync<CargoTrackingEventTransitionException>(
            () => service.RecordAsync(
                booking.Id,
                ResponsibleUserId,
                CreateValidRequest(CargoStatus.Loaded),
                CancellationToken.None));

        Assert.Equal(booking.Id, exception.CargoBookingId);
        Assert.Equal(CargoStatus.Registered, exception.CurrentStatus);
        Assert.Equal(CargoStatus.Loaded, exception.RequestedStatus);
        await AssertNothingChangedAsync(dbContext, booking.Id, CargoStatus.Registered);
    }

    [Fact]
    public async Task RecordAsync_rejects_a_repeated_status()
    {
        await using var dbContext = CreateDbContext();
        var booking = await AddBookingAsync(dbContext, "BK-TRACK-0004", CargoStatus.Planned);
        var service = CreateService(dbContext);

        var exception = await Assert.ThrowsAsync<CargoTrackingEventTransitionException>(
            () => service.RecordAsync(
                booking.Id,
                ResponsibleUserId,
                CreateValidRequest(CargoStatus.Planned),
                CancellationToken.None));

        Assert.Equal(CargoStatus.Planned, exception.CurrentStatus);
        Assert.Equal(CargoStatus.Planned, exception.RequestedStatus);
        await AssertNothingChangedAsync(dbContext, booking.Id, CargoStatus.Planned);
    }

    [Fact]
    public async Task RecordAsync_rejects_a_regressive_status()
    {
        await using var dbContext = CreateDbContext();
        var booking = await AddBookingAsync(dbContext, "BK-TRACK-0005", CargoStatus.InTransit);
        var service = CreateService(dbContext);

        var exception = await Assert.ThrowsAsync<CargoTrackingEventTransitionException>(
            () => service.RecordAsync(
                booking.Id,
                ResponsibleUserId,
                CreateValidRequest(CargoStatus.Loaded),
                CancellationToken.None));

        Assert.Equal(CargoStatus.InTransit, exception.CurrentStatus);
        Assert.Equal(CargoStatus.Loaded, exception.RequestedStatus);
        await AssertNothingChangedAsync(dbContext, booking.Id, CargoStatus.InTransit);
    }

    [Fact]
    public async Task RecordAsync_rejects_any_transition_from_the_terminal_status()
    {
        await using var dbContext = CreateDbContext();
        var booking = await AddBookingAsync(dbContext, "BK-TRACK-0006", CargoStatus.Completed);
        var service = CreateService(dbContext);

        var exception = await Assert.ThrowsAsync<CargoTrackingEventTransitionException>(
            () => service.RecordAsync(
                booking.Id,
                ResponsibleUserId,
                CreateValidRequest(CargoStatus.Completed),
                CancellationToken.None));

        Assert.Equal(CargoStatus.Completed, exception.CurrentStatus);
        await AssertNothingChangedAsync(dbContext, booking.Id, CargoStatus.Completed);
    }

    [Fact]
    public async Task RecordAsync_rejects_an_unknown_status()
    {
        await using var dbContext = CreateDbContext();
        var booking = await AddBookingAsync(dbContext, "BK-TRACK-0007", CargoStatus.Registered);
        var service = CreateService(dbContext);
        var request = CreateValidRequest(CargoStatus.Planned) with { Status = "Departed" };

        var exception = await Assert.ThrowsAsync<RecordCargoTrackingEventValidationException>(
            () => service.RecordAsync(
                booking.Id,
                ResponsibleUserId,
                request,
                CancellationToken.None));

        Assert.Contains(RecordCargoTrackingEventValidator.StatusField, exception.Errors);
        await AssertNothingChangedAsync(dbContext, booking.Id, CargoStatus.Registered);
    }

    [Fact]
    public async Task RecordAsync_rejects_a_missing_status_and_location()
    {
        await using var dbContext = CreateDbContext();
        var booking = await AddBookingAsync(dbContext, "BK-TRACK-0008", CargoStatus.Registered);
        var service = CreateService(dbContext);
        var request = CreateValidRequest(CargoStatus.Planned) with
        {
            Status = "   ",
            Location = null,
        };

        var exception = await Assert.ThrowsAsync<RecordCargoTrackingEventValidationException>(
            () => service.RecordAsync(
                booking.Id,
                ResponsibleUserId,
                request,
                CancellationToken.None));

        Assert.Equal(2, exception.Errors.Count);
        Assert.Contains(RecordCargoTrackingEventValidator.StatusField, exception.Errors);
        Assert.Contains(RecordCargoTrackingEventValidator.LocationField, exception.Errors);
    }

    [Fact]
    public async Task RecordAsync_rejects_a_location_exceeding_the_maximum_length()
    {
        await using var dbContext = CreateDbContext();
        var booking = await AddBookingAsync(dbContext, "BK-TRACK-0009", CargoStatus.Registered);
        var service = CreateService(dbContext);
        var request = CreateValidRequest(CargoStatus.Planned) with
        {
            Location = new string('A', CargoTrackingEvent.LocationMaxLength + 1),
        };

        var exception = await Assert.ThrowsAsync<RecordCargoTrackingEventValidationException>(
            () => service.RecordAsync(
                booking.Id,
                ResponsibleUserId,
                request,
                CancellationToken.None));

        Assert.Contains(RecordCargoTrackingEventValidator.LocationField, exception.Errors);
    }

    [Fact]
    public async Task RecordAsync_trims_the_location()
    {
        await using var dbContext = CreateDbContext();
        var booking = await AddBookingAsync(dbContext, "BK-TRACK-0010", CargoStatus.Registered);
        var service = CreateService(dbContext);
        var request = CreateValidRequest(CargoStatus.Planned) with
        {
            Location = "   Terminal 4, Berth 12   ",
        };

        await service.RecordAsync(
            booking.Id,
            ResponsibleUserId,
            request,
            CancellationToken.None);

        var trackingEvent = await dbContext
            .Set<CargoTrackingEvent>()
            .SingleAsync(CancellationToken.None);

        Assert.Equal("Terminal 4, Berth 12", trackingEvent.Location);
    }

    [Fact]
    public async Task RecordAsync_accepts_a_location_at_the_maximum_length()
    {
        await using var dbContext = CreateDbContext();
        var booking = await AddBookingAsync(dbContext, "BK-TRACK-0011", CargoStatus.Registered);
        var service = CreateService(dbContext);
        var request = CreateValidRequest(CargoStatus.Planned) with
        {
            Location = new string('A', CargoTrackingEvent.LocationMaxLength),
        };

        var response = await service.RecordAsync(
            booking.Id,
            ResponsibleUserId,
            request,
            CancellationToken.None);

        Assert.NotEqual(Guid.Empty, response.Id);
    }

    [Fact]
    public async Task RecordAsync_rejects_a_missing_occurrence_time()
    {
        await using var dbContext = CreateDbContext();
        var booking = await AddBookingAsync(dbContext, "BK-TRACK-0012", CargoStatus.Registered);
        var service = CreateService(dbContext);
        var request = CreateValidRequest(CargoStatus.Planned) with { OccurredUtc = null };

        var exception = await Assert.ThrowsAsync<RecordCargoTrackingEventValidationException>(
            () => service.RecordAsync(
                booking.Id,
                ResponsibleUserId,
                request,
                CancellationToken.None));

        Assert.Contains(RecordCargoTrackingEventValidator.OccurredUtcField, exception.Errors);
    }

    [Fact]
    public async Task RecordAsync_rejects_an_occurrence_time_that_is_not_utc()
    {
        await using var dbContext = CreateDbContext();
        var booking = await AddBookingAsync(dbContext, "BK-TRACK-0013", CargoStatus.Registered);
        var service = CreateService(dbContext);
        var request = CreateValidRequest(CargoStatus.Planned) with
        {
            OccurredUtc = DateTime.SpecifyKind(OccurredUtc(0), DateTimeKind.Local),
        };

        var exception = await Assert.ThrowsAsync<RecordCargoTrackingEventValidationException>(
            () => service.RecordAsync(
                booking.Id,
                ResponsibleUserId,
                request,
                CancellationToken.None));

        Assert.Contains(RecordCargoTrackingEventValidator.OccurredUtcField, exception.Errors);
    }

    [Fact]
    public async Task RecordAsync_rejects_an_occurrence_time_in_the_future()
    {
        await using var dbContext = CreateDbContext();
        var booking = await AddBookingAsync(dbContext, "BK-TRACK-0014", CargoStatus.Registered);
        var service = CreateService(dbContext);
        var request = CreateValidRequest(CargoStatus.Planned) with
        {
            OccurredUtc = FixedNow.UtcDateTime.AddSeconds(1),
        };

        var exception = await Assert.ThrowsAsync<RecordCargoTrackingEventValidationException>(
            () => service.RecordAsync(
                booking.Id,
                ResponsibleUserId,
                request,
                CancellationToken.None));

        Assert.Contains(RecordCargoTrackingEventValidator.OccurredUtcField, exception.Errors);
    }

    [Fact]
    public async Task RecordAsync_rejects_an_occurrence_time_before_the_booking_was_created()
    {
        await using var dbContext = CreateDbContext();
        var booking = await AddBookingAsync(dbContext, "BK-TRACK-0015", CargoStatus.Registered);
        var service = CreateService(dbContext);
        var request = CreateValidRequest(CargoStatus.Planned) with
        {
            OccurredUtc = booking.CreatedUtc.AddSeconds(-1),
        };

        var exception = await Assert.ThrowsAsync<RecordCargoTrackingEventValidationException>(
            () => service.RecordAsync(
                booking.Id,
                ResponsibleUserId,
                request,
                CancellationToken.None));

        Assert.Contains(RecordCargoTrackingEventValidator.OccurredUtcField, exception.Errors);
    }

    [Fact]
    public async Task RecordAsync_rejects_an_occurrence_time_at_or_before_the_latest_event()
    {
        await using var dbContext = CreateDbContext();
        var booking = await AddBookingAsync(dbContext, "BK-TRACK-0016", CargoStatus.Registered);
        var service = CreateService(dbContext);

        await service.RecordAsync(
            booking.Id,
            ResponsibleUserId,
            CreateValidRequest(CargoStatus.Planned, OccurredUtc(1)),
            CancellationToken.None);

        var exception = await Assert.ThrowsAsync<RecordCargoTrackingEventValidationException>(
            () => service.RecordAsync(
                booking.Id,
                ResponsibleUserId,
                CreateValidRequest(CargoStatus.Loaded, OccurredUtc(1)),
                CancellationToken.None));

        Assert.Contains(RecordCargoTrackingEventValidator.OccurredUtcField, exception.Errors);
        await AssertNothingChangedAsync(dbContext, booking.Id, CargoStatus.Planned, expectedEvents: 1);
    }

    [Fact]
    public async Task RecordAsync_rejects_an_unknown_booking()
    {
        await using var dbContext = CreateDbContext();
        var service = CreateService(dbContext);
        var unknownBookingId = Guid.Parse("6b5a4c3d-2e1f-4a0b-9c8d-7e6f5a4b3c2d");

        var exception = await Assert.ThrowsAsync<CargoBookingNotFoundException>(
            () => service.RecordAsync(
                unknownBookingId,
                ResponsibleUserId,
                CreateValidRequest(CargoStatus.Planned),
                CancellationToken.None));

        Assert.Equal(unknownBookingId, exception.CargoBookingId);
        Assert.Empty(dbContext.Set<CargoTrackingEvent>());
    }

    private static async Task AssertNothingChangedAsync(
        HarborOpsDbContext dbContext,
        Guid bookingId,
        CargoStatus expectedStatus,
        int expectedEvents = 0)
    {
        dbContext.ChangeTracker.Clear();

        var persistedBooking = await dbContext
            .Set<CargoBooking>()
            .SingleAsync(candidate => candidate.Id == bookingId, CancellationToken.None);

        Assert.Equal(expectedStatus, persistedBooking.Status);
        Assert.Equal(
            expectedEvents,
            await dbContext.Set<CargoTrackingEvent>().CountAsync(CancellationToken.None));
    }

    private static DateTime OccurredUtc(int step)
    {
        return FixedNow.UtcDateTime.AddMinutes(-9 + step);
    }

    private static RecordCargoTrackingEventRequest CreateValidRequest(
        CargoStatus status,
        DateTime? occurredUtc = null)
    {
        return new RecordCargoTrackingEventRequest
        {
            Status = status.ToString(),
            OccurredUtc = occurredUtc ?? OccurredUtc(0),
            Location = " Terminal 4, Berth 12 ",
        };
    }

    private static RecordCargoTrackingEventService CreateService(HarborOpsDbContext dbContext)
    {
        return new RecordCargoTrackingEventService(dbContext, new FixedTimeProvider(FixedNow));
    }


    [Fact]
    public async Task RecordAsync_rejects_loading_hazardous_cargo_before_safety_information_is_declared()
    {
        await using var dbContext = CreateDbContext();
        var booking = await AddBookingAsync(
            dbContext,
            "BK-TRACK-HAZ-1",
            CargoStatus.Planned,
            isHazardous: true);
        var service = CreateService(dbContext);

        var exception = await Assert.ThrowsAsync<CargoSafetyInfoRequiredBeforeLoadingException>(
            () => service.RecordAsync(
                booking.Id,
                ResponsibleUserId,
                CreateValidRequest(CargoStatus.Loaded),
                CancellationToken.None));

        var persistedBooking = await dbContext
            .Set<CargoBooking>()
            .SingleAsync(candidate => candidate.Id == booking.Id, CancellationToken.None);

        Assert.Equal(booking.Id, exception.CargoBookingId);
        Assert.Equal(CargoStatus.Planned, persistedBooking.Status);
        Assert.Empty(await dbContext.Set<CargoTrackingEvent>().ToListAsync(CancellationToken.None));
    }

    [Fact]
    public async Task RecordAsync_loads_hazardous_cargo_once_safety_information_is_declared()
    {
        await using var dbContext = CreateDbContext();
        var booking = await AddBookingAsync(
            dbContext,
            "BK-TRACK-HAZ-2",
            CargoStatus.Planned,
            isHazardous: true);
        await DeclareSafetyInfoAsync(dbContext, booking.Id);
        var service = CreateService(dbContext);

        var response = await service.RecordAsync(
            booking.Id,
            ResponsibleUserId,
            CreateValidRequest(CargoStatus.Loaded),
            CancellationToken.None);

        var persistedBooking = await dbContext
            .Set<CargoBooking>()
            .SingleAsync(candidate => candidate.Id == booking.Id, CancellationToken.None);

        Assert.Equal(nameof(CargoStatus.Loaded), response.Status);
        Assert.Equal(CargoStatus.Loaded, persistedBooking.Status);
    }

    [Fact]
    public async Task RecordAsync_loads_non_hazardous_cargo_without_any_safety_information()
    {
        await using var dbContext = CreateDbContext();
        var booking = await AddBookingAsync(dbContext, "BK-TRACK-PLAIN", CargoStatus.Planned);
        var service = CreateService(dbContext);

        var response = await service.RecordAsync(
            booking.Id,
            ResponsibleUserId,
            CreateValidRequest(CargoStatus.Loaded),
            CancellationToken.None);

        Assert.Equal(nameof(CargoStatus.Loaded), response.Status);
    }

    [Fact]
    public async Task RecordAsync_leaves_the_earlier_steps_open_to_undeclared_hazardous_cargo()
    {
        await using var dbContext = CreateDbContext();
        var booking = await AddBookingAsync(
            dbContext,
            "BK-TRACK-HAZ-3",
            CargoStatus.Registered,
            isHazardous: true);
        var service = CreateService(dbContext);

        // The declaration is only due before the cargo reaches the vessel, so planning it is fine.
        var response = await service.RecordAsync(
            booking.Id,
            ResponsibleUserId,
            CreateValidRequest(CargoStatus.Planned),
            CancellationToken.None);

        Assert.Equal(nameof(CargoStatus.Planned), response.Status);
    }

    private static async Task DeclareSafetyInfoAsync(HarborOpsDbContext dbContext, Guid cargoBookingId)
    {
        var safetyInfo = CargoSafetyInfo.Declare(
            cargoBookingId,
            "5.1",
            "UN1942",
            "Keep below 25C",
            handlingNotes: null);

        await dbContext.Set<CargoSafetyInfo>().AddAsync(safetyInfo, CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);
    }
    private static async Task<CargoBooking> AddBookingAsync(
        HarborOpsDbContext dbContext,
        string bookingNumber,
        CargoStatus status,
        bool isHazardous = false)
    {
        var booking = CargoBooking.Register(
            bookingNumber,
            VoyageId,
            "Steel coils",
            "General Cargo",
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
