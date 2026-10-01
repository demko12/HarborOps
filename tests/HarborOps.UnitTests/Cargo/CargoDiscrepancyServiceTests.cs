using HarborOps.Infrastructure.Persistence;
using HarborOps.Modules.Cargo.Application;
using HarborOps.Modules.Cargo.Contracts;
using HarborOps.Modules.Cargo.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.UnitTests.Cargo;

public sealed class CargoDiscrepancyServiceTests
{
    private static readonly DateTimeOffset FixedUtcNow = DateTimeOffset.UtcNow.AddMinutes(10);

    private static readonly DateTimeOffset LaterUtcNow = FixedUtcNow.AddHours(4);

    private static readonly DateTime ObservedAtUtc = FixedUtcNow.UtcDateTime.AddHours(-2);

    private static readonly Guid VoyageId =
        Guid.Parse("7c1d2e3f-4a5b-4c6d-8e9f-0a1b2c3d4e5f");

    private static readonly Guid ReporterUserId =
        Guid.Parse("1a2b3c4d-5e6f-4071-8293-a4b5c6d7e8f9");

    private static readonly Guid InvestigatorUserId =
        Guid.Parse("4d5e6f70-8192-4a3b-8c4d-5e6f708192a3");

    private const string ReporterName = "reporter@harborops.test";

    private const string InvestigatorName = "investigator@harborops.test";

    private static readonly CargoDiscrepancyActor Reporter = new(ReporterUserId, ReporterName);

    private static readonly CargoDiscrepancyActor Investigator = new(InvestigatorUserId, InvestigatorName);

    private const string Summary = "Seal replaced and verified against the manifest.";

    [Fact]
    public async Task RegisterAsync_stores_the_discrepancy_its_history_and_touches_the_booking()
    {
        await using var dbContext = CreateDbContext();
        var booking = await SeedBookingAsync(dbContext, CargoStatus.Arrived);
        var container = await SeedContainerAsync(dbContext, booking);
        var token = booking.ConcurrencyToken;

        var details = await CreateRegisterService(dbContext).RegisterAsync(
            booking.Id,
            Reporter,
            ValidRequest(CargoDiscrepancyType.Damage, container.Id),
            CancellationToken.None);

        Assert.Equal(nameof(CargoDiscrepancyStatus.Reported), details.Discrepancy.Status);
        Assert.Equal(container.ContainerNumber, details.Discrepancy.ContainerNumber);
        Assert.Equal(ReporterUserId, details.Discrepancy.ReportedByUserId);
        Assert.Equal(FixedUtcNow.UtcDateTime, details.Discrepancy.ReportedAtUtc);
        Assert.Equal(ObservedAtUtc, details.Discrepancy.ObservedAtUtc);
        Assert.Equal("Dented corner post", details.Discrepancy.Description);
        Assert.Equal(booking.BookingNumber, details.BookingNumber);
        Assert.Equal(CargoDiscrepancyMapper.StartInvestigationFirstBlocker, details.ResolutionBlocker);

        var entry = Assert.Single(details.History);
        Assert.Equal(nameof(CargoDiscrepancyEventType.Reported), entry.EventType);
        Assert.Equal(ReporterUserId, entry.ActorUserId);

        dbContext.ChangeTracker.Clear();
        var persisted = await dbContext.Set<CargoBooking>().SingleAsync(candidate => candidate.Id == booking.Id);
        Assert.NotEqual(token, persisted.ConcurrencyToken);
    }

    [Fact]
    public async Task RegisterAsync_accepts_a_booking_level_shortage_without_a_container()
    {
        await using var dbContext = CreateDbContext();
        var booking = await SeedBookingAsync(dbContext, CargoStatus.Unloaded);

        var details = await CreateRegisterService(dbContext).RegisterAsync(
            booking.Id,
            Reporter,
            ValidRequest(CargoDiscrepancyType.Shortage, containerId: null),
            CancellationToken.None);

        Assert.Null(details.Discrepancy.ContainerId);
        Assert.Equal(nameof(CargoDiscrepancyType.Shortage), details.Discrepancy.Type);
    }

    [Fact]
    public async Task RegisterAsync_rejects_a_completed_booking()
    {
        await using var dbContext = CreateDbContext();
        var booking = await SeedBookingAsync(dbContext, CargoStatus.Completed);

        await Assert.ThrowsAsync<CargoBookingCompletedException>(
            () => CreateRegisterService(dbContext).RegisterAsync(
                booking.Id,
                Reporter,
                ValidRequest(CargoDiscrepancyType.Shortage, null),
                CancellationToken.None));

        Assert.Empty(await dbContext.Set<CargoDiscrepancy>().ToListAsync());
    }

    [Fact]
    public async Task RegisterAsync_rejects_a_booking_that_has_not_been_loaded()
    {
        await using var dbContext = CreateDbContext();
        var booking = await SeedBookingAsync(dbContext, CargoStatus.Planned);

        await Assert.ThrowsAsync<CargoDiscrepancyRegistrationNotAllowedException>(
            () => CreateRegisterService(dbContext).RegisterAsync(
                booking.Id,
                Reporter,
                ValidRequest(CargoDiscrepancyType.Shortage, null),
                CancellationToken.None));
    }

    [Fact]
    public async Task RegisterAsync_rejects_a_container_of_another_booking()
    {
        await using var dbContext = CreateDbContext();
        var booking = await SeedBookingAsync(dbContext, CargoStatus.Arrived);
        var other = await SeedBookingAsync(dbContext, CargoStatus.Arrived);
        var foreign = await SeedContainerAsync(dbContext, other);

        await Assert.ThrowsAsync<CargoDiscrepancyContainerMismatchException>(
            () => CreateRegisterService(dbContext).RegisterAsync(
                booking.Id,
                Reporter,
                ValidRequest(CargoDiscrepancyType.Damage, foreign.Id),
                CancellationToken.None));

        Assert.Empty(await dbContext.Set<CargoDiscrepancy>().ToListAsync());
    }

    [Fact]
    public async Task RegisterAsync_rejects_an_unknown_container()
    {
        await using var dbContext = CreateDbContext();
        var booking = await SeedBookingAsync(dbContext, CargoStatus.Arrived);

        var exception = await Assert.ThrowsAsync<CargoDiscrepancyValidationException>(
            () => CreateRegisterService(dbContext).RegisterAsync(
                booking.Id,
                Reporter,
                ValidRequest(CargoDiscrepancyType.Damage, Guid.NewGuid()),
                CancellationToken.None));

        Assert.True(exception.Errors.ContainsKey(CargoDiscrepancyValidator.ContainerIdField));
    }

    [Fact]
    public async Task RegisterAsync_validates_every_field()
    {
        await using var dbContext = CreateDbContext();
        var booking = await SeedBookingAsync(dbContext, CargoStatus.Arrived);

        var exception = await Assert.ThrowsAsync<CargoDiscrepancyValidationException>(
            () => CreateRegisterService(dbContext).RegisterAsync(
                booking.Id,
                Reporter,
                new RegisterCargoDiscrepancyRequest
                {
                    Type = "SealMismatch",
                    Severity = "Urgent",
                    ObservedAtUtc = FixedUtcNow.UtcDateTime.AddMinutes(1),
                    Description = new string('x', CargoDiscrepancy.DescriptionMaxLength + 1),
                },
                CancellationToken.None));

        Assert.True(exception.Errors.ContainsKey(CargoDiscrepancyValidator.ContainerIdField));
        Assert.True(exception.Errors.ContainsKey(CargoDiscrepancyValidator.SeverityField));
        Assert.True(exception.Errors.ContainsKey(CargoDiscrepancyValidator.ObservedAtUtcField));
        Assert.True(exception.Errors.ContainsKey(CargoDiscrepancyValidator.DescriptionField));
    }

    [Theory]
    [InlineData(null, "Low", "Broken lashing")]
    [InlineData("Theft", "Low", "Broken lashing")]
    [InlineData("3", "Low", "Broken lashing")]
    [InlineData("Damage", null, "Broken lashing")]
    [InlineData("Damage", "Low", "   ")]
    public async Task RegisterAsync_rejects_missing_or_unknown_values(string? type, string? severity, string description)
    {
        await using var dbContext = CreateDbContext();
        var booking = await SeedBookingAsync(dbContext, CargoStatus.Arrived);

        await Assert.ThrowsAsync<CargoDiscrepancyValidationException>(
            () => CreateRegisterService(dbContext).RegisterAsync(
                booking.Id,
                Reporter,
                new RegisterCargoDiscrepancyRequest
                {
                    Type = type,
                    Severity = severity,
                    ObservedAtUtc = ObservedAtUtc,
                    Description = description,
                },
                CancellationToken.None));
    }

    [Fact]
    public async Task RegisterAsync_accepts_an_observation_at_exactly_the_server_time()
    {
        await using var dbContext = CreateDbContext();
        var booking = await SeedBookingAsync(dbContext, CargoStatus.Arrived);

        var details = await CreateRegisterService(dbContext).RegisterAsync(
            booking.Id,
            Reporter,
            ValidRequest(CargoDiscrepancyType.Other, null) with { ObservedAtUtc = FixedUtcNow.UtcDateTime },
            CancellationToken.None);

        Assert.Equal(FixedUtcNow.UtcDateTime, details.Discrepancy.ObservedAtUtc);
    }

    [Fact]
    public async Task An_operator_can_investigate_and_close_a_discrepancy_with_an_attributable_disposition()
    {
        await using var dbContext = CreateDbContext();
        var (booking, discrepancyId) = await SeedReportedAsync(dbContext);
        var workflow = CreateWorkflowService(dbContext, LaterUtcNow);

        await workflow.StartInvestigationAsync(booking.Id, discrepancyId, Investigator, CancellationToken.None);
        var noted = await workflow.AddNoteAsync(
            booking.Id,
            discrepancyId,
            Investigator,
            new AddCargoDiscrepancyNoteRequest { Note = "Seal number differs from the booking." },
            CancellationToken.None);

        Assert.Equal(1, noted.InvestigationNoteCount);
        Assert.Null(noted.ResolutionBlocker);

        var resolved = await workflow.ResolveAsync(
            booking.Id,
            discrepancyId,
            Investigator,
            new ResolveCargoDiscrepancyRequest { Disposition = "corrected", Summary = Summary },
            CancellationToken.None);

        Assert.Equal(nameof(CargoDiscrepancyStatus.Resolved), resolved.Discrepancy.Status);
        Assert.Equal(nameof(CargoDiscrepancyDisposition.Corrected), resolved.Discrepancy.Disposition);
        Assert.Equal(InvestigatorUserId, resolved.Discrepancy.ResolvedByUserId);
        Assert.Equal(InvestigatorName, resolved.Discrepancy.ResolvedByName);
        Assert.Equal(ReporterName, resolved.Discrepancy.ReportedByName);
        Assert.Equal(LaterUtcNow.UtcDateTime, resolved.Discrepancy.ResolvedAtUtc);
        Assert.Equal(
            [
                nameof(CargoDiscrepancyEventType.Reported),
                nameof(CargoDiscrepancyEventType.InvestigationStarted),
                nameof(CargoDiscrepancyEventType.NoteAdded),
                nameof(CargoDiscrepancyEventType.Resolved),
            ],
            resolved.History.Select(entry => entry.EventType));
        Assert.Equal(Summary, resolved.History[^1].Text);
        Assert.All(resolved.History.Skip(1), entry => Assert.Equal(InvestigatorUserId, entry.ActorUserId));
        Assert.All(resolved.History.Skip(1), entry => Assert.Equal(InvestigatorName, entry.ActorName));
    }

    [Fact]
    public async Task ResolveAsync_requires_a_note_recorded_during_the_investigation()
    {
        await using var dbContext = CreateDbContext();
        var (booking, discrepancyId) = await SeedReportedAsync(dbContext);
        var workflow = CreateWorkflowService(dbContext);

        await workflow.AddNoteAsync(
            booking.Id,
            discrepancyId,
            Investigator,
            new AddCargoDiscrepancyNoteRequest { Note = "Reported by the tally clerk." },
            CancellationToken.None);
        var investigating = await workflow.StartInvestigationAsync(
            booking.Id,
            discrepancyId,
            Investigator,
            CancellationToken.None);

        Assert.Equal(0, investigating.InvestigationNoteCount);
        Assert.Equal(CargoDiscrepancyMapper.InvestigationNoteRequiredBlocker, investigating.ResolutionBlocker);

        var exception = await Assert.ThrowsAsync<CargoDiscrepancyTransitionNotAllowedException>(
            () => workflow.ResolveAsync(
                booking.Id,
                discrepancyId,
                Investigator,
                new ResolveCargoDiscrepancyRequest { Disposition = "Corrected", Summary = Summary },
                CancellationToken.None));

        Assert.Equal(CargoDiscrepancyTransitionNotAllowedException.NoInvestigationNoteReason, exception.Reason);
    }

    [Theory]
    [InlineData(null, Summary)]
    [InlineData("Fixed", Summary)]
    [InlineData("Corrected", null)]
    [InlineData("Corrected", "Too short summary.")]
    public async Task ResolveAsync_validates_the_disposition_and_summary(string? disposition, string? summary)
    {
        await using var dbContext = CreateDbContext();
        var (booking, discrepancyId) = await SeedInvestigatingWithNoteAsync(dbContext);

        await Assert.ThrowsAsync<CargoDiscrepancyValidationException>(
            () => CreateWorkflowService(dbContext).ResolveAsync(
                booking.Id,
                discrepancyId,
                Investigator,
                new ResolveCargoDiscrepancyRequest { Disposition = disposition, Summary = summary },
                CancellationToken.None));
    }

    [Fact]
    public async Task VoidAsync_requires_a_reason_and_is_only_allowed_before_resolution()
    {
        await using var dbContext = CreateDbContext();
        var (booking, discrepancyId) = await SeedInvestigatingWithNoteAsync(dbContext);
        var workflow = CreateWorkflowService(dbContext);

        await Assert.ThrowsAsync<CargoDiscrepancyValidationException>(
            () => workflow.VoidAsync(
                booking.Id,
                discrepancyId,
                Investigator,
                new VoidCargoDiscrepancyRequest { Reason = " " },
                CancellationToken.None));

        await workflow.ResolveAsync(
            booking.Id,
            discrepancyId,
            Investigator,
            new ResolveCargoDiscrepancyRequest { Disposition = "NoDiscrepancyFound", Summary = Summary },
            CancellationToken.None);

        var exception = await Assert.ThrowsAsync<CargoDiscrepancyTransitionNotAllowedException>(
            () => workflow.VoidAsync(
                booking.Id,
                discrepancyId,
                Investigator,
                new VoidCargoDiscrepancyRequest { Reason = "Registered twice" },
                CancellationToken.None));

        Assert.Equal(CargoDiscrepancyTransitionNotAllowedException.AlreadyResolvedReason, exception.Reason);
    }

    [Fact]
    public async Task VoidAsync_records_the_reason_in_the_history()
    {
        await using var dbContext = CreateDbContext();
        var (booking, discrepancyId) = await SeedReportedAsync(dbContext);

        var voided = await CreateWorkflowService(dbContext).VoidAsync(
            booking.Id,
            discrepancyId,
            Investigator,
            new VoidCargoDiscrepancyRequest { Reason = "Registered twice" },
            CancellationToken.None);

        Assert.Equal(nameof(CargoDiscrepancyStatus.Voided), voided.Discrepancy.Status);
        Assert.Equal(InvestigatorUserId, voided.Discrepancy.VoidedByUserId);
        Assert.Equal("Registered twice", voided.History[^1].Text);
        Assert.Null(voided.ResolutionBlocker);
    }

    [Fact]
    public async Task History_is_append_only()
    {
        await using var dbContext = CreateDbContext();
        var (booking, discrepancyId) = await SeedInvestigatingWithNoteAsync(dbContext);
        var before = await dbContext.Set<CargoDiscrepancyEvent>()
            .AsNoTracking()
            .OrderBy(entry => entry.OccurredAtUtc)
            .ToListAsync();

        await CreateWorkflowService(dbContext, LaterUtcNow).AddNoteAsync(
            booking.Id,
            discrepancyId,
            Investigator,
            new AddCargoDiscrepancyNoteRequest { Note = "Second look" },
            CancellationToken.None);

        var after = await dbContext.Set<CargoDiscrepancyEvent>().AsNoTracking().ToListAsync();

        Assert.Equal(before.Count + 1, after.Count);
        Assert.All(before, original =>
        {
            var current = Assert.Single(after, entry => entry.Id == original.Id);
            Assert.Equal(original.EventType, current.EventType);
            Assert.Equal(original.Text, current.Text);
            Assert.Equal(original.ActorUserId, current.ActorUserId);
            Assert.Equal(original.OccurredAtUtc, current.OccurredAtUtc);
        });
    }

    [Fact]
    public async Task Workflow_actions_never_reach_a_discrepancy_of_another_booking()
    {
        await using var dbContext = CreateDbContext();
        var (_, discrepancyId) = await SeedReportedAsync(dbContext);
        var other = await SeedBookingAsync(dbContext, CargoStatus.Arrived);

        await Assert.ThrowsAsync<CargoDiscrepancyNotFoundException>(
            () => CreateWorkflowService(dbContext).StartInvestigationAsync(
                other.Id,
                discrepancyId,
                Investigator,
                CancellationToken.None));

        await Assert.ThrowsAsync<CargoDiscrepancyNotFoundException>(
            () => new GetCargoDiscrepanciesQuery(dbContext).GetDetailsAsync(
                other.Id,
                discrepancyId,
                CancellationToken.None));

        var list = await new GetCargoDiscrepanciesQuery(dbContext).GetAsync(
            other.Id,
            new GetCargoDiscrepanciesRequest(),
            CancellationToken.None);

        Assert.Empty(list.Items);
        Assert.Equal(0, list.Summary.OpenCount);
    }

    [Fact]
    public async Task GetAsync_filters_pages_and_summarises_the_booking()
    {
        await using var dbContext = CreateDbContext();
        var booking = await SeedBookingAsync(dbContext, CargoStatus.Arrived);
        var container = await SeedContainerAsync(dbContext, booking);
        var register = CreateRegisterService(dbContext);

        var damage = await register.RegisterAsync(booking.Id, Reporter, ValidRequest(CargoDiscrepancyType.Damage, container.Id), CancellationToken.None);
        await register.RegisterAsync(booking.Id, Reporter, ValidRequest(CargoDiscrepancyType.Shortage, null), CancellationToken.None);
        await register.RegisterAsync(booking.Id, Reporter, ValidRequest(CargoDiscrepancyType.SealMismatch, container.Id), CancellationToken.None);
        await CreateWorkflowService(dbContext).StartInvestigationAsync(booking.Id, damage.Discrepancy.Id, Investigator, CancellationToken.None);

        var query = new GetCargoDiscrepanciesQuery(dbContext);

        var all = await query.GetAsync(booking.Id, new GetCargoDiscrepanciesRequest { PageSize = 2 }, CancellationToken.None);
        var investigating = await query.GetAsync(booking.Id, new GetCargoDiscrepanciesRequest { Status = "investigating" }, CancellationToken.None);
        var onContainer = await query.GetAsync(booking.Id, new GetCargoDiscrepanciesRequest { ContainerId = container.Id, Type = "SealMismatch" }, CancellationToken.None);
        var secondPage = await query.GetAsync(booking.Id, new GetCargoDiscrepanciesRequest { PageSize = 2, PageNumber = 2 }, CancellationToken.None);

        Assert.Equal(3, all.TotalCount);
        Assert.Equal(2, all.Items.Count);
        Assert.Single(secondPage.Items);
        Assert.Empty(all.Items.Select(item => item.Id).Intersect(secondPage.Items.Select(item => item.Id)));
        Assert.Equal(new CargoDiscrepancySummary(2, 1, 0, 0, 3), all.Summary);
        Assert.Equal(damage.Discrepancy.Id, Assert.Single(investigating.Items).Id);
        Assert.Equal(nameof(CargoDiscrepancyType.SealMismatch), Assert.Single(onContainer.Items).Type);
    }

    [Theory]
    [InlineData("Closed", null, null, null)]
    [InlineData(null, "Theft", null, null)]
    [InlineData(null, null, "Urgent", null)]
    [InlineData(null, null, null, 0)]
    public async Task GetAsync_rejects_invalid_filters(string? status, string? type, string? severity, int? pageNumber)
    {
        await using var dbContext = CreateDbContext();
        var booking = await SeedBookingAsync(dbContext, CargoStatus.Arrived);

        await Assert.ThrowsAsync<CargoDiscrepancyValidationException>(
            () => new GetCargoDiscrepanciesQuery(dbContext).GetAsync(
                booking.Id,
                new GetCargoDiscrepanciesRequest { Status = status, Type = type, Severity = severity, PageNumber = pageNumber },
                CancellationToken.None));
    }

    [Fact]
    public async Task Completion_is_blocked_by_open_discrepancies_with_their_ids_and_counts()
    {
        await using var dbContext = CreateDbContext();
        var booking = await SeedBookingAsync(dbContext, CargoStatus.Unloaded);
        var register = CreateRegisterService(dbContext);
        var reported = await register.RegisterAsync(booking.Id, Reporter, ValidRequest(CargoDiscrepancyType.Shortage, null), CancellationToken.None);
        var investigating = await register.RegisterAsync(booking.Id, Reporter, ValidRequest(CargoDiscrepancyType.Other, null), CancellationToken.None);
        var voided = await register.RegisterAsync(booking.Id, Reporter, ValidRequest(CargoDiscrepancyType.Damage, null), CancellationToken.None);
        var workflow = CreateWorkflowService(dbContext);
        await workflow.StartInvestigationAsync(booking.Id, investigating.Discrepancy.Id, Investigator, CancellationToken.None);
        await workflow.VoidAsync(booking.Id, voided.Discrepancy.Id, Investigator, new VoidCargoDiscrepancyRequest { Reason = "Duplicate" }, CancellationToken.None);

        var exception = await Assert.ThrowsAsync<CargoBookingCompletionBlockedException>(
            () => CreateTrackingService(dbContext).RecordAsync(
                booking.Id,
                ReporterUserId,
                CompletionRequest(),
                CancellationToken.None));

        Assert.Equal(booking.Id, exception.Blockers.CargoBookingId);
        Assert.Equal(2, exception.Blockers.OpenCount);
        Assert.Equal(1, exception.Blockers.ReportedCount);
        Assert.Equal(1, exception.Blockers.InvestigatingCount);
        Assert.Equal(
            new[] { reported.Discrepancy.Id, investigating.Discrepancy.Id }.Order(),
            exception.Blockers.Discrepancies.Select(blocker => blocker.DiscrepancyId).Order());

        dbContext.ChangeTracker.Clear();
        var persisted = await dbContext.Set<CargoBooking>().SingleAsync(candidate => candidate.Id == booking.Id);
        Assert.Equal(CargoStatus.Unloaded, persisted.Status);
        Assert.Empty(await dbContext.Set<CargoTrackingEvent>().ToListAsync());
    }

    [Fact]
    public async Task Completion_proceeds_once_every_discrepancy_is_resolved_or_voided()
    {
        await using var dbContext = CreateDbContext();
        var (booking, discrepancyId) = await SeedInvestigatingWithNoteAsync(dbContext, CargoStatus.Unloaded);
        await CreateWorkflowService(dbContext).ResolveAsync(
            booking.Id,
            discrepancyId,
            Investigator,
            new ResolveCargoDiscrepancyRequest { Disposition = "AcceptedDeviation", Summary = Summary },
            CancellationToken.None);

        var blockers = await new CargoCompletionBlockersQuery(dbContext).GetAsync(booking.Id, CancellationToken.None);
        var response = await CreateTrackingService(dbContext).RecordAsync(
            booking.Id,
            ReporterUserId,
            CompletionRequest(),
            CancellationToken.None);

        Assert.Equal(0, blockers.OpenCount);
        Assert.Empty(blockers.Discrepancies);
        Assert.Equal(nameof(CargoStatus.Completed), response.Status);
    }

    [Fact]
    public async Task Discrepancies_do_not_block_the_earlier_status_steps()
    {
        await using var dbContext = CreateDbContext();
        var (booking, _) = await SeedReportedAsync(dbContext, CargoStatus.Arrived);

        var response = await CreateTrackingService(dbContext).RecordAsync(
            booking.Id,
            ReporterUserId,
            new RecordCargoTrackingEventRequest
            {
                Status = nameof(CargoStatus.Unloaded),
                OccurredUtc = FixedUtcNow.UtcDateTime.AddMinutes(-1),
                Location = "Berth 4",
            },
            CancellationToken.None);

        Assert.Equal(nameof(CargoStatus.Unloaded), response.Status);
    }

    [Fact]
    public async Task A_completion_that_raced_a_registration_fails_on_the_booking_token()
    {
        var databaseName = $"HarborOpsUnitTests-{Guid.NewGuid()}";
        await using var seedContext = CreateDbContext(databaseName);
        var booking = await SeedBookingAsync(seedContext, CargoStatus.Unloaded);

        await using var completionContext = CreateDbContext(databaseName);
        var loaded = await completionContext.Set<CargoBooking>().SingleAsync(candidate => candidate.Id == booking.Id);

        await using var registrationContext = CreateDbContext(databaseName);
        await CreateRegisterService(registrationContext).RegisterAsync(
            booking.Id,
            Reporter,
            ValidRequest(CargoDiscrepancyType.Shortage, null),
            CancellationToken.None);

        loaded.AdvanceStatusTo(CargoStatus.Completed);

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => completionContext.SaveChangesAsync());
    }

    [Fact]
    public async Task Competing_resolutions_fail_on_the_discrepancy_token()
    {
        var databaseName = $"HarborOpsUnitTests-{Guid.NewGuid()}";
        await using var seedContext = CreateDbContext(databaseName);
        var (booking, discrepancyId) = await SeedInvestigatingWithNoteAsync(seedContext);

        await using var slowContext = CreateDbContext(databaseName);
        var slow = await slowContext.Set<CargoDiscrepancy>().SingleAsync(candidate => candidate.Id == discrepancyId);

        await using var fastContext = CreateDbContext(databaseName);
        await CreateWorkflowService(fastContext).ResolveAsync(
            booking.Id,
            discrepancyId,
            Investigator,
            new ResolveCargoDiscrepancyRequest { Disposition = "Corrected", Summary = Summary },
            CancellationToken.None);

        slow.Resolve(CargoDiscrepancyDisposition.AcceptedDeviation, Summary, 1, null, ReporterUserId, ReporterName, LaterUtcNow.UtcDateTime);

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => slowContext.SaveChangesAsync());
    }

    [Fact]
    public async Task OpenAsync_creates_a_linked_discrepancy_once_and_reopens_it_afterwards()
    {
        await using var dbContext = CreateDbContext();
        var booking = await SeedBookingAsync(dbContext, CargoStatus.InTransit);
        var (reefer, excursion) = await SeedExcursionAsync(dbContext, booking);
        var service = CreateOpenService(dbContext);

        var created = await service.OpenAsync(
            reefer.Id,
            excursion.Id,
            Reporter,
            new OpenExcursionDiscrepancyRequest { Severity = "High", Description = "Cargo warmed above range" },
            CancellationToken.None);

        var reopened = await service.OpenAsync(
            reefer.Id,
            excursion.Id,
            Investigator,
            new OpenExcursionDiscrepancyRequest(),
            CancellationToken.None);

        Assert.True(created.Created);
        Assert.False(reopened.Created);
        Assert.Equal(created.Details.Discrepancy.Id, reopened.Details.Discrepancy.Id);
        Assert.Single(await dbContext.Set<CargoDiscrepancy>().ToListAsync());

        var discrepancy = created.Details.Discrepancy;
        Assert.Equal(nameof(CargoDiscrepancyType.TemperatureRelated), discrepancy.Type);
        Assert.Equal(reefer.Id, discrepancy.ContainerId);
        Assert.Equal(excursion.Id, discrepancy.SourceExcursionId);
        Assert.Equal(excursion.MeasuredAtUtc, discrepancy.ObservedAtUtc);

        var source = Assert.IsType<CargoDiscrepancySource>(created.Details.Source);
        Assert.Equal(nameof(ReeferExcursionStatus.Open), source.CurrentStatus);
        Assert.True(source.BlocksResolution);
        Assert.Equal(7.5m, source.TemperatureCelsius);
    }

    [Fact]
    public async Task OpenAsync_returns_a_voided_discrepancy_instead_of_creating_a_second_one()
    {
        await using var dbContext = CreateDbContext();
        var booking = await SeedBookingAsync(dbContext, CargoStatus.InTransit);
        var (reefer, excursion) = await SeedExcursionAsync(dbContext, booking);
        var service = CreateOpenService(dbContext);
        var created = await service.OpenAsync(reefer.Id, excursion.Id, Reporter, ValidExcursionRequest(), CancellationToken.None);
        await CreateWorkflowService(dbContext).VoidAsync(
            booking.Id,
            created.Details.Discrepancy.Id,
            Investigator,
            new VoidCargoDiscrepancyRequest { Reason = "Probe was faulty" },
            CancellationToken.None);

        var reopened = await service.OpenAsync(reefer.Id, excursion.Id, Reporter, ValidExcursionRequest(), CancellationToken.None);

        Assert.False(reopened.Created);
        Assert.Equal(nameof(CargoDiscrepancyStatus.Voided), reopened.Details.Discrepancy.Status);
    }

    [Fact]
    public async Task OpenAsync_rejects_an_excursion_of_another_container()
    {
        await using var dbContext = CreateDbContext();
        var booking = await SeedBookingAsync(dbContext, CargoStatus.InTransit);
        var (_, excursion) = await SeedExcursionAsync(dbContext, booking);
        var otherReefer = await SeedContainerAsync(dbContext, booking, "42R1");

        await Assert.ThrowsAsync<ReeferExcursionNotFoundException>(
            () => CreateOpenService(dbContext).OpenAsync(
                otherReefer.Id,
                excursion.Id,
                Reporter,
                ValidExcursionRequest(),
                CancellationToken.None));
    }

    [Fact]
    public async Task OpenAsync_rejects_a_reefer_without_a_booking()
    {
        await using var dbContext = CreateDbContext();
        var reefer = Container.Register("TRIU1234567", "42R1", CargoStatus.Loaded);
        await dbContext.Set<Container>().AddAsync(reefer);
        await dbContext.SaveChangesAsync();
        var excursion = await SeedExcursionOnAsync(dbContext, reefer);

        await Assert.ThrowsAsync<ContainerHasNoBookingException>(
            () => CreateOpenService(dbContext).OpenAsync(
                reefer.Id,
                excursion.Id,
                Reporter,
                ValidExcursionRequest(),
                CancellationToken.None));
    }

    [Fact]
    public async Task OpenAsync_rejects_a_completed_booking()
    {
        await using var dbContext = CreateDbContext();
        var booking = await SeedBookingAsync(dbContext, CargoStatus.InTransit);
        var (reefer, excursion) = await SeedExcursionAsync(dbContext, booking);
        await AdvanceAsync(dbContext, booking, CargoStatus.Completed);

        await Assert.ThrowsAsync<CargoBookingCompletedException>(
            () => CreateOpenService(dbContext).OpenAsync(
                reefer.Id,
                excursion.Id,
                Reporter,
                ValidExcursionRequest(),
                CancellationToken.None));
    }

    [Fact]
    public async Task A_linked_discrepancy_waits_for_the_excursion_and_then_resolves_without_touching_it()
    {
        await using var dbContext = CreateDbContext();
        var booking = await SeedBookingAsync(dbContext, CargoStatus.Arrived);
        var (reefer, excursion) = await SeedExcursionAsync(dbContext, booking);
        var created = await CreateOpenService(dbContext).OpenAsync(reefer.Id, excursion.Id, Reporter, ValidExcursionRequest(), CancellationToken.None);
        var discrepancyId = created.Details.Discrepancy.Id;
        var workflow = CreateWorkflowService(dbContext);
        await workflow.StartInvestigationAsync(booking.Id, discrepancyId, Investigator, CancellationToken.None);
        var noted = await workflow.AddNoteAsync(
            booking.Id,
            discrepancyId,
            Investigator,
            new AddCargoDiscrepancyNoteRequest { Note = "Checked the reefer log." },
            CancellationToken.None);

        Assert.Equal(CargoDiscrepancyMapper.SourceExcursionOpenBlocker, noted.ResolutionBlocker);

        await Assert.ThrowsAsync<CargoDiscrepancySourceExcursionOpenException>(
            () => workflow.ResolveAsync(
                booking.Id,
                discrepancyId,
                Investigator,
                new ResolveCargoDiscrepancyRequest { Disposition = "AcceptedDeviation", Summary = Summary },
                CancellationToken.None));

        await new ResolveReeferExcursionService(dbContext, new FixedTimeProvider(FixedUtcNow)).ResolveAsync(
            reefer.Id,
            excursion.Id,
            InvestigatorUserId,
            new ResolveReeferExcursionRequest { Disposition = "AcceptedDeviation", Explanation = "Shipper accepted" },
            CancellationToken.None);
        dbContext.ChangeTracker.Clear();
        var excursionToken = (await dbContext.Set<ReeferExcursion>().AsNoTracking().SingleAsync()).ConcurrencyToken;

        var resolved = await workflow.ResolveAsync(
            booking.Id,
            discrepancyId,
            Investigator,
            new ResolveCargoDiscrepancyRequest { Disposition = "AcceptedDeviation", Summary = Summary },
            CancellationToken.None);

        var persistedExcursion = await dbContext.Set<ReeferExcursion>().AsNoTracking().SingleAsync();
        Assert.Equal(nameof(CargoDiscrepancyStatus.Resolved), resolved.Discrepancy.Status);
        Assert.Equal(ReeferExcursionStatus.Resolved, persistedExcursion.Status);
        Assert.Equal(excursionToken, persistedExcursion.ConcurrencyToken);
        Assert.False(resolved.Source!.BlocksResolution);
    }

    [Fact]
    public async Task A_linked_discrepancy_resolves_once_its_excursion_is_superseded()
    {
        await using var dbContext = CreateDbContext();
        var booking = await SeedBookingAsync(dbContext, CargoStatus.Arrived);
        var (reefer, excursion) = await SeedExcursionAsync(dbContext, booking);
        var created = await CreateOpenService(dbContext).OpenAsync(reefer.Id, excursion.Id, Reporter, ValidExcursionRequest(), CancellationToken.None);
        var discrepancyId = created.Details.Discrepancy.Id;
        var workflow = CreateWorkflowService(dbContext);
        await workflow.StartInvestigationAsync(booking.Id, discrepancyId, Investigator, CancellationToken.None);
        await workflow.AddNoteAsync(booking.Id, discrepancyId, Investigator, new AddCargoDiscrepancyNoteRequest { Note = "Probe misread." }, CancellationToken.None);

        var tracked = await dbContext.Set<ReeferExcursion>().SingleAsync();
        tracked.Supersede(null, FixedUtcNow.UtcDateTime);
        await dbContext.SaveChangesAsync();

        var resolved = await workflow.ResolveAsync(
            booking.Id,
            discrepancyId,
            Investigator,
            new ResolveCargoDiscrepancyRequest { Disposition = "NoDiscrepancyFound", Summary = Summary },
            CancellationToken.None);

        Assert.Equal(nameof(CargoDiscrepancyStatus.Resolved), resolved.Discrepancy.Status);
        Assert.Equal(nameof(ReeferExcursionStatus.Superseded), resolved.Source!.CurrentStatus);
    }

    [Fact]
    public async Task Resolving_or_voiding_never_releases_a_hold_or_changes_bills_of_lading_or_declared_cargo()
    {
        await using var dbContext = CreateDbContext();
        var booking = await SeedBookingAsync(dbContext, CargoStatus.Arrived);
        var container = await SeedContainerAsync(dbContext, booking);
        var control = ContainerControl.Start(container.Id);
        var hold = control.Hold("Damaged doors", InvestigatorUserId, FixedUtcNow.UtcDateTime);
        var bill = await SeedIssuedBillAsync(dbContext, booking);
        await dbContext.Set<ContainerControl>().AddAsync(control);
        await dbContext.Set<ContainerControlEvent>().AddAsync(hold);
        await dbContext.SaveChangesAsync();
        dbContext.ChangeTracker.Clear();

        var controlToken = control.ConcurrencyToken;
        var billToken = bill.ConcurrencyToken;
        var register = CreateRegisterService(dbContext);
        var workflow = CreateWorkflowService(dbContext);

        var first = await register.RegisterAsync(booking.Id, Reporter, ValidRequest(CargoDiscrepancyType.Damage, container.Id), CancellationToken.None);
        var second = await register.RegisterAsync(booking.Id, Reporter, ValidRequest(CargoDiscrepancyType.Shortage, null), CancellationToken.None);
        await workflow.StartInvestigationAsync(booking.Id, first.Discrepancy.Id, Investigator, CancellationToken.None);
        await workflow.AddNoteAsync(booking.Id, first.Discrepancy.Id, Investigator, new AddCargoDiscrepancyNoteRequest { Note = "Doors bent." }, CancellationToken.None);
        await workflow.ResolveAsync(booking.Id, first.Discrepancy.Id, Investigator, new ResolveCargoDiscrepancyRequest { Disposition = "Corrected", Summary = Summary }, CancellationToken.None);
        await workflow.VoidAsync(booking.Id, second.Discrepancy.Id, Investigator, new VoidCargoDiscrepancyRequest { Reason = "Counted twice" }, CancellationToken.None);

        dbContext.ChangeTracker.Clear();
        var persistedControl = await dbContext.Set<ContainerControl>().SingleAsync();
        var persistedBill = await dbContext.Set<BillOfLading>().SingleAsync();
        var persistedBooking = await dbContext.Set<CargoBooking>().SingleAsync(candidate => candidate.Id == booking.Id);

        Assert.Equal(ContainerControlState.Held, persistedControl.State);
        Assert.Equal(controlToken, persistedControl.ConcurrencyToken);
        Assert.Single(await dbContext.Set<ContainerControlEvent>().ToListAsync());
        Assert.Equal(BillOfLadingStatus.Issued, persistedBill.Status);
        Assert.Equal(billToken, persistedBill.ConcurrencyToken);
        Assert.Equal(20, persistedBill.SnapshotBookingPackageCount);
        Assert.Equal(12000m, persistedBill.SnapshotBookingWeightKg);
        Assert.Equal(20, persistedBooking.PackageCount);
        Assert.Equal(12000m, persistedBooking.WeightKg);
    }

    private static RegisterCargoDiscrepancyRequest ValidRequest(CargoDiscrepancyType type, Guid? containerId)
    {
        return new RegisterCargoDiscrepancyRequest
        {
            Type = type.ToString(),
            Severity = nameof(CargoDiscrepancySeverity.High),
            ContainerId = containerId,
            ObservedAtUtc = ObservedAtUtc,
            Description = "  Dented corner post  ",
        };
    }

    private static OpenExcursionDiscrepancyRequest ValidExcursionRequest()
    {
        return new OpenExcursionDiscrepancyRequest
        {
            Severity = nameof(CargoDiscrepancySeverity.High),
            Description = "Cargo warmed above range",
        };
    }

    private static RecordCargoTrackingEventRequest CompletionRequest()
    {
        return new RecordCargoTrackingEventRequest
        {
            Status = nameof(CargoStatus.Completed),
            OccurredUtc = FixedUtcNow.UtcDateTime.AddMinutes(-1),
            Location = "Gate 2",
        };
    }

    private static async Task<(CargoBooking Booking, Guid DiscrepancyId)> SeedReportedAsync(
        HarborOpsDbContext dbContext,
        CargoStatus status = CargoStatus.Arrived)
    {
        var booking = await SeedBookingAsync(dbContext, status);
        var container = await SeedContainerAsync(dbContext, booking);

        var details = await CreateRegisterService(dbContext).RegisterAsync(
            booking.Id,
            Reporter,
            ValidRequest(CargoDiscrepancyType.SealMismatch, container.Id),
            CancellationToken.None);

        return (booking, details.Discrepancy.Id);
    }

    private static async Task<(CargoBooking Booking, Guid DiscrepancyId)> SeedInvestigatingWithNoteAsync(
        HarborOpsDbContext dbContext,
        CargoStatus status = CargoStatus.Arrived)
    {
        var (booking, discrepancyId) = await SeedReportedAsync(dbContext, status);
        var workflow = CreateWorkflowService(dbContext);

        await workflow.StartInvestigationAsync(booking.Id, discrepancyId, Investigator, CancellationToken.None);
        await workflow.AddNoteAsync(
            booking.Id,
            discrepancyId,
            Investigator,
            new AddCargoDiscrepancyNoteRequest { Note = "Seal photographed." },
            CancellationToken.None);

        return (booking, discrepancyId);
    }

    private static async Task<CargoBooking> SeedBookingAsync(HarborOpsDbContext dbContext, CargoStatus status)
    {
        var booking = CargoBooking.Register(
            $"BK-{Guid.NewGuid():N}"[..16].ToUpperInvariant(),
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

        await dbContext.Set<CargoBooking>().AddAsync(booking);
        await dbContext.SaveChangesAsync();

        return booking;
    }

    private static async Task AdvanceAsync(HarborOpsDbContext dbContext, CargoBooking booking, CargoStatus status)
    {
        var tracked = await dbContext.Set<CargoBooking>().SingleAsync(candidate => candidate.Id == booking.Id);

        while (tracked.Status != status)
        {
            tracked.AdvanceStatusTo(tracked.Status + 1);
        }

        await dbContext.SaveChangesAsync();
    }

    private static async Task<Container> SeedContainerAsync(
        HarborOpsDbContext dbContext,
        CargoBooking booking,
        string containerType = "40HC")
    {
        var container = Container.Register(
            $"MSKU{Random.Shared.Next(1_000_000, 9_999_999)}",
            containerType,
            CargoStatus.Loaded,
            cargoBookingId: booking.Id);

        await dbContext.Set<Container>().AddAsync(container);
        await dbContext.SaveChangesAsync();

        return container;
    }

    private static async Task<(Container Reefer, ReeferExcursion Excursion)> SeedExcursionAsync(
        HarborOpsDbContext dbContext,
        CargoBooking booking)
    {
        var reefer = await SeedContainerAsync(dbContext, booking, "42R1");

        return (reefer, await SeedExcursionOnAsync(dbContext, reefer));
    }

    private static async Task<ReeferExcursion> SeedExcursionOnAsync(HarborOpsDbContext dbContext, Container reefer)
    {
        var profile = ReeferMonitoringProfile.Configure(
            reefer.Id,
            isReefer: true,
            -2m,
            4m,
            1m,
            InvestigatorUserId,
            ObservedAtUtc.AddDays(-1));
        var reading = ReeferTemperatureReading.Record(profile, 7.5m, ObservedAtUtc, null, ReporterUserId, ObservedAtUtc.AddMinutes(5));
        var excursion = ReeferExcursion.Open(reading);

        await dbContext.Set<ReeferMonitoringProfile>().AddAsync(profile);
        await dbContext.Set<ReeferTemperatureReading>().AddAsync(reading);
        await dbContext.Set<ReeferExcursion>().AddAsync(excursion);
        await dbContext.SaveChangesAsync();

        return excursion;
    }

    private static async Task<BillOfLading> SeedIssuedBillAsync(HarborOpsDbContext dbContext, CargoBooking booking)
    {
        var bill = BillOfLading.Draft(
            booking.Id,
            new BillOfLadingDraftValues(
                "HOBL-DSC-1",
                "Adriatic Fish",
                "Bar, Montenegro",
                "Nordic Foods",
                "Oslo, Norway",
                null,
                null,
                "Bar",
                "MEBAR",
                "NOOSL",
                "Oslo",
                FreightTerms.Prepaid,
                "AF/1-20",
                "Frozen fish",
                20,
                12000m,
                "Bar",
                new DateOnly(2026, 9, 29)),
            FixedUtcNow.UtcDateTime);

        bill.Issue(
            new BillOfLadingBookingSnapshot(
                booking.BookingNumber,
                booking.VoyageId,
                booking.CargoType,
                booking.Description,
                booking.PackageCount,
                booking.WeightKg,
                booking.Status),
            1,
            InvestigatorUserId,
            FixedUtcNow.UtcDateTime);

        await dbContext.Set<BillOfLading>().AddAsync(bill);

        return bill;
    }

    private static RegisterCargoDiscrepancyService CreateRegisterService(DbContext dbContext, DateTimeOffset? utcNow = null)
    {
        return new RegisterCargoDiscrepancyService(dbContext, new FixedTimeProvider(utcNow ?? FixedUtcNow));
    }

    private static CargoDiscrepancyWorkflowService CreateWorkflowService(DbContext dbContext, DateTimeOffset? utcNow = null)
    {
        return new CargoDiscrepancyWorkflowService(dbContext, new FixedTimeProvider(utcNow ?? FixedUtcNow));
    }

    private static OpenExcursionDiscrepancyService CreateOpenService(DbContext dbContext)
    {
        return new OpenExcursionDiscrepancyService(dbContext, new FixedTimeProvider(FixedUtcNow));
    }

    private static RecordCargoTrackingEventService CreateTrackingService(DbContext dbContext)
    {
        return new RecordCargoTrackingEventService(dbContext, new FixedTimeProvider(FixedUtcNow));
    }

    private static HarborOpsDbContext CreateDbContext(string? databaseName = null)
    {
        var options = new DbContextOptionsBuilder<HarborOpsDbContext>()
            .UseInMemoryDatabase(databaseName ?? $"HarborOpsUnitTests-{Guid.NewGuid()}")
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
