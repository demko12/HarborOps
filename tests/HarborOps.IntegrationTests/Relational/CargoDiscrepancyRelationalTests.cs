using HarborOps.Infrastructure.Persistence;
using HarborOps.Modules.Cargo.Application;
using HarborOps.Modules.Cargo.Contracts;
using HarborOps.Modules.Cargo.Domain;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.IntegrationTests.Relational;

public sealed class CargoDiscrepancyRelationalTests : IAsyncLifetime
{
    private const string Summary = "Seal replaced and verified against the manifest.";

    private static readonly Guid VoyageId =
        Guid.Parse("6f5e4d3c-2b1a-4098-8e7d-6c5b4a392817");

    private static readonly Guid ActorUserId =
        Guid.Parse("1a2b3c4d-5e6f-4071-8293-a4b5c6d7e8f9");

    private const string ActorName = "relational.operator@harborops.test";

    private static readonly CargoDiscrepancyActor Actor = new(ActorUserId, ActorName);

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

        SqlConnection.ClearAllPools();
        await using var dbContext = CreateDbContext();
        await dbContext.Database.EnsureDeletedAsync();
    }

    [RelationalFact]
    public async Task A_completion_that_loaded_the_booking_before_a_registration_is_rejected()
    {
        var bookingId = await SeedBookingAsync(CargoStatus.Unloaded);

        await using var completionContext = CreateDbContext();
        var booking = await completionContext.Set<CargoBooking>().SingleAsync(candidate => candidate.Id == bookingId);

        await using (var registrationContext = CreateDbContext())
        {
            await RegisterAsync(registrationContext, bookingId);
        }

        booking.AdvanceStatusTo(CargoStatus.Completed);

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => completionContext.SaveChangesAsync());
        Assert.Equal(CargoStatus.Unloaded, await StatusOfAsync(bookingId));
    }

    [RelationalFact]
    public async Task A_registration_that_loaded_the_booking_before_completion_is_rejected()
    {
        var bookingId = await SeedBookingAsync(CargoStatus.Unloaded);

        await using var registrationContext = CreateDbContext();
        var booking = await registrationContext.Set<CargoBooking>().SingleAsync(candidate => candidate.Id == bookingId);
        var (discrepancy, reported) = CargoDiscrepancy.Register(
            booking,
            null,
            null,
            CargoDiscrepancyType.Shortage,
            CargoDiscrepancySeverity.Medium,
            "Two pallets missing",
            DateTime.UtcNow.AddMinutes(-5),
            ActorUserId,
            ActorName,
            DateTime.UtcNow);
        booking.Touch();
        registrationContext.Set<CargoDiscrepancy>().Add(discrepancy);
        registrationContext.Set<CargoDiscrepancyEvent>().Add(reported);

        await using (var completionContext = CreateDbContext())
        {
            await CompleteAsync(completionContext, bookingId);
        }

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => registrationContext.SaveChangesAsync());
        Assert.Equal(CargoStatus.Completed, await StatusOfAsync(bookingId));
        Assert.Equal(0, await OpenDiscrepancyCountAsync(bookingId));
    }

    [RelationalFact]
    public async Task Simultaneous_registration_and_completion_never_complete_a_booking_with_an_open_discrepancy()
    {
        for (var round = 0; round < 12; round++)
        {
            var bookingId = await SeedBookingAsync(CargoStatus.Unloaded);
            using var start = new SemaphoreSlim(0, 2);

            var registration = Task.Run(async () =>
            {
                await start.WaitAsync();
                await using var dbContext = CreateDbContext();

                return await Attempt(() => RegisterAsync(dbContext, bookingId));
            });

            var completion = Task.Run(async () =>
            {
                await start.WaitAsync();
                await using var dbContext = CreateDbContext();

                return await Attempt(() => CompleteAsync(dbContext, bookingId));
            });

            start.Release(2);
            var outcomes = await Task.WhenAll(registration, completion);

            var status = await StatusOfAsync(bookingId);
            var openCount = await OpenDiscrepancyCountAsync(bookingId);

            Assert.Contains(true, outcomes);
            Assert.False(
                status == CargoStatus.Completed && openCount > 0,
                $"Round {round}: the booking completed with {openCount} open discrepancies.");
            Assert.Equal(outcomes[0], openCount == 1);
            Assert.Equal(outcomes[1], status == CargoStatus.Completed);
        }
    }

    [RelationalFact]
    public async Task Converting_the_same_excursion_concurrently_yields_one_discrepancy_for_every_caller()
    {
        var bookingId = await SeedBookingAsync(CargoStatus.InTransit);
        var (reeferId, excursionId) = await SeedExcursionAsync(bookingId);
        using var start = new SemaphoreSlim(0, 6);

        var callers = Enumerable.Range(0, 6).Select(_ => Task.Run(async () =>
        {
            await start.WaitAsync();
            await using var dbContext = CreateDbContext();

            return await new OpenExcursionDiscrepancyService(dbContext, TimeProvider.System).OpenAsync(
                reeferId,
                excursionId,
                Actor,
                new OpenExcursionDiscrepancyRequest { Severity = "High", Description = "Warm cargo" },
                CancellationToken.None);
        })).ToArray();

        start.Release(6);
        var responses = await Task.WhenAll(callers);

        Assert.Single(responses.Select(response => response.Details.Discrepancy.Id).Distinct());
        Assert.Single(responses, response => response.Created);

        await using var verify = CreateDbContext();
        Assert.Equal(1, await verify.Set<CargoDiscrepancy>().CountAsync(discrepancy => discrepancy.SourceExcursionId == excursionId));
        Assert.Equal(1, await verify.Set<CargoDiscrepancyEvent>().CountAsync(entry => entry.EventType == CargoDiscrepancyEventType.Reported));
    }

    [RelationalFact]
    public async Task The_database_rejects_a_second_discrepancy_for_the_same_excursion()
    {
        var bookingId = await SeedBookingAsync(CargoStatus.InTransit);
        var (reeferId, excursionId) = await SeedExcursionAsync(bookingId);

        await using (var first = CreateDbContext())
        {
            await new OpenExcursionDiscrepancyService(first, TimeProvider.System).OpenAsync(
                reeferId,
                excursionId,
                Actor,
                new OpenExcursionDiscrepancyRequest { Severity = "High", Description = "Warm cargo" },
                CancellationToken.None);
        }

        await using var dbContext = CreateDbContext();
        var booking = await dbContext.Set<CargoBooking>().SingleAsync(candidate => candidate.Id == bookingId);
        var reefer = await dbContext.Set<Container>().AsNoTracking().SingleAsync(container => container.Id == reeferId);
        var excursion = await dbContext.Set<ReeferExcursion>().AsNoTracking().SingleAsync(candidate => candidate.Id == excursionId);
        var (duplicate, reported) = CargoDiscrepancy.Register(
            booking,
            reefer,
            excursion,
            CargoDiscrepancyType.TemperatureRelated,
            CargoDiscrepancySeverity.Low,
            "Second attempt",
            DateTime.UtcNow.AddMinutes(-5),
            ActorUserId,
            ActorName,
            DateTime.UtcNow);
        dbContext.Set<CargoDiscrepancy>().Add(duplicate);
        dbContext.Set<CargoDiscrepancyEvent>().Add(reported);

        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => dbContext.SaveChangesAsync());

        Assert.IsType<SqlException>(exception.InnerException);
        Assert.Contains("IX_CargoDiscrepancies_SourceExcursionId", exception.InnerException!.Message, StringComparison.Ordinal);
    }

    [RelationalFact]
    public async Task Competing_resolutions_have_exactly_one_winner()
    {
        for (var round = 0; round < 8; round++)
        {
            var bookingId = await SeedBookingAsync(CargoStatus.Arrived);
            Guid discrepancyId;

            await using (var setup = CreateDbContext())
            {
                discrepancyId = (await RegisterAsync(setup, bookingId)).Discrepancy.Id;
                var workflow = new CargoDiscrepancyWorkflowService(setup, TimeProvider.System);
                await workflow.StartInvestigationAsync(bookingId, discrepancyId, Actor, CancellationToken.None);
                await workflow.AddNoteAsync(
                    bookingId,
                    discrepancyId,
                    Actor,
                    new AddCargoDiscrepancyNoteRequest { Note = "Counted twice at the gate." },
                    CancellationToken.None);
            }

            using var start = new SemaphoreSlim(0, 2);
            var resolvers = new[] { "Corrected", "AcceptedDeviation" }.Select(disposition => Task.Run(async () =>
            {
                await start.WaitAsync();
                await using var dbContext = CreateDbContext();

                return await Attempt(() => new CargoDiscrepancyWorkflowService(dbContext, TimeProvider.System).ResolveAsync(
                    bookingId,
                    discrepancyId,
                    Actor,
                    new ResolveCargoDiscrepancyRequest { Disposition = disposition, Summary = Summary },
                    CancellationToken.None));
            })).ToArray();

            start.Release(2);
            var outcomes = await Task.WhenAll(resolvers);

            await using var verify = CreateDbContext();
            Assert.Single(outcomes, succeeded => succeeded);
            Assert.Equal(1, await verify.Set<CargoDiscrepancyEvent>().CountAsync(entry =>
                entry.DiscrepancyId == discrepancyId && entry.EventType == CargoDiscrepancyEventType.Resolved));
            Assert.Equal(
                CargoDiscrepancyStatus.Resolved,
                (await verify.Set<CargoDiscrepancy>().SingleAsync(discrepancy => discrepancy.Id == discrepancyId)).Status);
        }
    }

    [RelationalFact]
    public async Task The_booking_search_counts_and_filters_open_discrepancies_on_sql_server()
    {
        var blockedId = await SeedBookingAsync(CargoStatus.Unloaded);
        var clearId = await SeedBookingAsync(CargoStatus.Unloaded);

        await using (var setup = CreateDbContext())
        {
            await RegisterAsync(setup, blockedId);
            await RegisterAsync(setup, blockedId);
        }

        await using var dbContext = CreateDbContext();
        var query = new SearchCargoBookingsQuery(dbContext);
        var all = await query.SearchAsync(new SearchCargoBookingsRequest { PageSize = 100 }, CancellationToken.None);
        var open = await query.SearchAsync(
            new SearchCargoBookingsRequest { HasOpenDiscrepancies = true, PageSize = 100 },
            CancellationToken.None);

        Assert.Equal(2, all.Items.Single(item => item.CargoBookingId == blockedId).OpenDiscrepancyCount);
        Assert.Equal(0, all.Items.Single(item => item.CargoBookingId == clearId).OpenDiscrepancyCount);
        Assert.Equal(1, all.OpenDiscrepancyBookingCount);
        Assert.Equal(blockedId, Assert.Single(open.Items).CargoBookingId);
    }

    private static async Task<bool> Attempt(Func<Task> action)
    {
        try
        {
            await action();

            return true;
        }
        catch (DbUpdateException)
        {
            return false;
        }
        catch (CargoBookingCompletionBlockedException)
        {
            return false;
        }
        catch (CargoBookingCompletedException)
        {
            return false;
        }
        catch (CargoTrackingEventTransitionException)
        {
            return false;
        }
        catch (CargoDiscrepancyTransitionNotAllowedException)
        {
            return false;
        }
        catch (InvalidOperationException exception) when (exception.InnerException is SqlException { Number: 1205 })
        {
            return false;
        }
        catch (SqlException exception) when (exception.Number == 1205)
        {
            return false;
        }
    }

    private static Task<CargoDiscrepancyDetails> RegisterAsync(HarborOpsDbContext dbContext, Guid bookingId)
    {
        return new RegisterCargoDiscrepancyService(dbContext, TimeProvider.System).RegisterAsync(
            bookingId,
            Actor,
            new RegisterCargoDiscrepancyRequest
            {
                Type = "Shortage",
                Severity = "Medium",
                ObservedAtUtc = DateTime.UtcNow.AddMinutes(-5),
                Description = "Two pallets missing",
            },
            CancellationToken.None);
    }

    private static Task<RecordCargoTrackingEventResponse> CompleteAsync(HarborOpsDbContext dbContext, Guid bookingId)
    {
        return new RecordCargoTrackingEventService(dbContext, TimeProvider.System).RecordAsync(
            bookingId,
            ActorUserId,
            new RecordCargoTrackingEventRequest
            {
                Status = nameof(CargoStatus.Completed),
                OccurredUtc = DateTime.UtcNow,
                Location = "Gate 2",
            },
            CancellationToken.None);
    }

    private async Task<CargoStatus> StatusOfAsync(Guid bookingId)
    {
        await using var dbContext = CreateDbContext();

        return await dbContext.Set<CargoBooking>()
            .Where(booking => booking.Id == bookingId)
            .Select(booking => booking.Status)
            .SingleAsync();
    }

    private async Task<int> OpenDiscrepancyCountAsync(Guid bookingId)
    {
        await using var dbContext = CreateDbContext();

        return await dbContext.Set<CargoDiscrepancy>().CountAsync(discrepancy =>
            discrepancy.CargoBookingId == bookingId
            && (discrepancy.Status == CargoDiscrepancyStatus.Reported
                || discrepancy.Status == CargoDiscrepancyStatus.Investigating));
    }

    private async Task<Guid> SeedBookingAsync(CargoStatus status)
    {
        await using var dbContext = CreateDbContext();
        var booking = CargoBooking.Register(
            $"BK-REL-DSC-{Guid.NewGuid():N}"[..24].ToUpperInvariant(),
            VoyageId,
            "Frozen fish",
            "Reefer",
            12000m,
            20,
            isHazardous: false);

        while (booking.Status != status)
        {
            booking.AdvanceStatusTo(booking.Status + 1);
        }

        dbContext.Set<CargoBooking>().Add(booking);
        await dbContext.SaveChangesAsync();

        return booking.Id;
    }

    private async Task<(Guid ReeferId, Guid ExcursionId)> SeedExcursionAsync(Guid bookingId)
    {
        await using var dbContext = CreateDbContext();
        var observedAtUtc = DateTime.UtcNow.AddHours(-1);
        var reefer = Container.Register(
            $"RELU{Random.Shared.Next(1_000_000, 9_999_999)}",
            "42R1",
            CargoStatus.Loaded,
            cargoBookingId: bookingId);
        var profile = ReeferMonitoringProfile.Configure(reefer.Id, isReefer: true, -2m, 4m, 1m, ActorUserId, observedAtUtc.AddDays(-1));
        var reading = ReeferTemperatureReading.Record(profile, 7.5m, observedAtUtc, null, ActorUserId, observedAtUtc.AddMinutes(5));
        var excursion = ReeferExcursion.Open(reading);

        dbContext.Set<Container>().Add(reefer);
        dbContext.Set<ReeferMonitoringProfile>().Add(profile);
        dbContext.Set<ReeferTemperatureReading>().Add(reading);
        dbContext.Set<ReeferExcursion>().Add(excursion);
        await dbContext.SaveChangesAsync();

        return (reefer.Id, excursion.Id);
    }

    private HarborOpsDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<HarborOpsDbContext>()
            .UseSqlServer(SqlServerAvailability.ConnectionStringFor(databaseName))
            .Options;

        return new HarborOpsDbContext(options);
    }
}
