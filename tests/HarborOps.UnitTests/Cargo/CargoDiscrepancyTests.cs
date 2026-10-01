using HarborOps.Modules.Cargo.Domain;

namespace HarborOps.UnitTests.Cargo;

public sealed class CargoDiscrepancyTests
{
    private static readonly Guid VoyageId =
        Guid.Parse("7c1d2e3f-4a5b-4c6d-8e9f-0a1b2c3d4e5f");

    private static readonly Guid ReporterUserId =
        Guid.Parse("1a2b3c4d-5e6f-4071-8293-a4b5c6d7e8f9");

    private static readonly Guid InvestigatorUserId =
        Guid.Parse("4d5e6f70-8192-4a3b-8c4d-5e6f708192a3");

    private const string ReporterName = "reporter@harborops.test";

    private const string InvestigatorName = "investigator@harborops.test";

    private static readonly DateTime ObservedAtUtc =
        new(2026, 9, 29, 7, 30, 0, DateTimeKind.Utc);

    private static readonly DateTime ReportedAtUtc =
        new(2026, 9, 29, 8, 0, 0, DateTimeKind.Utc);

    private static readonly DateTime LaterUtc =
        new(2026, 9, 29, 12, 0, 0, DateTimeKind.Utc);

    private const string Summary = "Seal replaced and verified against the manifest.";

    [Fact]
    public void Register_records_a_reported_discrepancy_with_its_reporter_and_server_time()
    {
        var booking = Booking(CargoStatus.Unloaded);
        var container = ContainerOn(booking);

        var (discrepancy, reported) = CargoDiscrepancy.Register(
            booking,
            container,
            null,
            CargoDiscrepancyType.Damage,
            CargoDiscrepancySeverity.High,
            "  Dented corner post  ",
            ObservedAtUtc,
            ReporterUserId,
            ReporterName,
            ReportedAtUtc);

        Assert.Equal(booking.Id, discrepancy.CargoBookingId);
        Assert.Equal(container.Id, discrepancy.ContainerId);
        Assert.Equal(CargoDiscrepancyType.Damage, discrepancy.Type);
        Assert.Equal(CargoDiscrepancySeverity.High, discrepancy.Severity);
        Assert.Equal("Dented corner post", discrepancy.Description);
        Assert.Equal(ObservedAtUtc, discrepancy.ObservedAtUtc);
        Assert.Equal(ReporterName, discrepancy.ReportedByName);
        Assert.Equal(ReporterUserId, discrepancy.ReportedByUserId);
        Assert.Equal(ReportedAtUtc, discrepancy.ReportedAtUtc);
        Assert.Equal(CargoDiscrepancyStatus.Reported, discrepancy.Status);
        Assert.True(discrepancy.IsOpen);
        Assert.Null(discrepancy.SourceExcursionId);

        Assert.Equal(discrepancy.Id, reported.DiscrepancyId);
        Assert.Equal(CargoDiscrepancyEventType.Reported, reported.EventType);
        Assert.Null(reported.FromStatus);
        Assert.Equal(CargoDiscrepancyStatus.Reported, reported.ToStatus);
        Assert.Equal(ReporterUserId, reported.ActorUserId);
        Assert.Equal(ReporterName, reported.ActorName);
        Assert.Equal(ReportedAtUtc, reported.OccurredAtUtc);
    }

    [Fact]
    public void Register_allows_a_booking_level_discrepancy_without_a_container()
    {
        var (discrepancy, _) = Register(Booking(CargoStatus.InTransit), null, CargoDiscrepancyType.Shortage);

        Assert.Null(discrepancy.ContainerId);
    }

    [Theory]
    [InlineData(nameof(CargoStatus.Loaded))]
    [InlineData(nameof(CargoStatus.InTransit))]
    [InlineData(nameof(CargoStatus.Arrived))]
    [InlineData(nameof(CargoStatus.Unloaded))]
    public void Register_is_open_from_loading_until_unloading(string statusName)
    {
        var status = Enum.Parse<CargoStatus>(statusName);
        var (discrepancy, _) = Register(Booking(status), null, CargoDiscrepancyType.Other);

        Assert.Equal(CargoDiscrepancyStatus.Reported, discrepancy.Status);
    }

    [Theory]
    [InlineData(nameof(CargoStatus.Registered))]
    [InlineData(nameof(CargoStatus.Planned))]
    [InlineData(nameof(CargoStatus.Completed))]
    public void Register_is_rejected_outside_the_cargo_movement(string statusName)
    {
        var status = Enum.Parse<CargoStatus>(statusName);
        var booking = Booking(status);

        var exception = Assert.Throws<CargoDiscrepancyRegistrationNotAllowedException>(
            () => Register(booking, null, CargoDiscrepancyType.Other));

        Assert.Equal(status, exception.BookingStatus);
    }

    [Theory]
    [InlineData(nameof(CargoDiscrepancyType.SealMismatch))]
    [InlineData(nameof(CargoDiscrepancyType.TemperatureRelated))]
    public void Register_requires_a_container_for_seal_and_temperature_issues(string typeName)
    {
        var type = Enum.Parse<CargoDiscrepancyType>(typeName);
        var exception = Assert.Throws<CargoDiscrepancyContainerRequiredException>(
            () => Register(Booking(CargoStatus.Arrived), null, type));

        Assert.Equal(type, exception.Type);
    }

    [Fact]
    public void Register_rejects_a_container_of_another_booking()
    {
        var booking = Booking(CargoStatus.Arrived);
        var foreign = ContainerOn(Booking(CargoStatus.Arrived));

        var exception = Assert.Throws<CargoDiscrepancyContainerMismatchException>(
            () => Register(booking, foreign, CargoDiscrepancyType.Damage));

        Assert.Equal(foreign.Id, exception.ContainerId);
        Assert.Equal(booking.Id, exception.CargoBookingId);
    }

    [Fact]
    public void Register_links_an_excursion_of_the_same_container_and_keeps_a_snapshot()
    {
        var booking = Booking(CargoStatus.InTransit);
        var reefer = ContainerOn(booking, "42R1");
        var excursion = ExcursionOn(reefer, 7.5m);

        var (discrepancy, _) = CargoDiscrepancy.Register(
            booking,
            reefer,
            excursion,
            CargoDiscrepancyType.TemperatureRelated,
            CargoDiscrepancySeverity.Critical,
            "Cargo warmed above the agreed range",
            ObservedAtUtc,
            ReporterUserId,
            ReporterName,
            ReportedAtUtc);

        Assert.Equal(excursion.Id, discrepancy.SourceExcursionId);
        Assert.Equal(ReeferExcursionDirection.Above, discrepancy.SourceDirection);
        Assert.Equal(7.5m, discrepancy.SourceTemperatureCelsius);
        Assert.Equal(-2m, discrepancy.SourceThresholdMinimumCelsius);
        Assert.Equal(4m, discrepancy.SourceThresholdMaximumCelsius);
        Assert.Equal(excursion.MeasuredAtUtc, discrepancy.SourceMeasuredAtUtc);
        Assert.Equal(excursion.DetectedAtUtc, discrepancy.SourceDetectedAtUtc);
        Assert.Equal(ReeferExcursionStatus.Open, excursion.Status);
    }

    [Fact]
    public void Register_rejects_an_excursion_of_another_container()
    {
        var booking = Booking(CargoStatus.InTransit);
        var reefer = ContainerOn(booking, "42R1");
        var otherReefer = ContainerOn(booking, "42R1");
        var excursion = ExcursionOn(otherReefer, 7.5m);

        Assert.Throws<CargoDiscrepancySourceMismatchException>(
            () => CargoDiscrepancy.Register(
                booking,
                reefer,
                excursion,
                CargoDiscrepancyType.TemperatureRelated,
                CargoDiscrepancySeverity.High,
                "Warm cargo",
                ObservedAtUtc,
                ReporterUserId,
                ReporterName,
                ReportedAtUtc));
    }

    [Fact]
    public void Register_rejects_a_superseded_excursion()
    {
        var booking = Booking(CargoStatus.InTransit);
        var reefer = ContainerOn(booking, "42R1");
        var excursion = ExcursionOn(reefer, 7.5m);
        excursion.Supersede(null, ReportedAtUtc);

        var exception = Assert.Throws<CargoDiscrepancySourceSupersededException>(
            () => CargoDiscrepancy.Register(
                booking,
                reefer,
                excursion,
                CargoDiscrepancyType.TemperatureRelated,
                CargoDiscrepancySeverity.High,
                "Warm cargo",
                ObservedAtUtc,
                ReporterUserId,
                ReporterName,
                ReportedAtUtc));

        Assert.Equal(excursion.Id, exception.ExcursionId);
    }

    [Fact]
    public void A_blank_actor_name_is_not_stored_and_a_long_one_is_bounded()
    {
        var booking = Booking(CargoStatus.Arrived);

        var (discrepancy, reported) = CargoDiscrepancy.Register(
            booking,
            null,
            null,
            CargoDiscrepancyType.Shortage,
            CargoDiscrepancySeverity.Low,
            "Two cartons short",
            ObservedAtUtc,
            ReporterUserId,
            "   ",
            ReportedAtUtc);
        var entry = discrepancy.StartInvestigation(InvestigatorUserId, new string('a', 300), LaterUtc);

        Assert.Null(discrepancy.ReportedByName);
        Assert.Null(reported.ActorName);
        Assert.Equal(CargoDiscrepancy.ActorNameMaxLength, entry.ActorName!.Length);
    }

    [Fact]
    public void Register_rejects_an_excursion_on_a_discrepancy_that_is_not_temperature_related()
    {
        var booking = Booking(CargoStatus.InTransit);
        var reefer = ContainerOn(booking, "42R1");

        Assert.Throws<CargoDiscrepancySourceMismatchException>(
            () => CargoDiscrepancy.Register(
                booking,
                reefer,
                ExcursionOn(reefer, 7.5m),
                CargoDiscrepancyType.Damage,
                CargoDiscrepancySeverity.High,
                "Dented door",
                ObservedAtUtc,
                ReporterUserId,
                ReporterName,
                ReportedAtUtc));
    }

    [Fact]
    public void StartInvestigation_moves_a_reported_discrepancy_under_investigation()
    {
        var discrepancy = Reported();
        var token = discrepancy.ConcurrencyToken;

        var entry = discrepancy.StartInvestigation(InvestigatorUserId, InvestigatorName, LaterUtc);

        Assert.Equal(CargoDiscrepancyStatus.Investigating, discrepancy.Status);
        Assert.True(discrepancy.IsOpen);
        Assert.NotEqual(token, discrepancy.ConcurrencyToken);
        Assert.Equal(CargoDiscrepancyEventType.InvestigationStarted, entry.EventType);
        Assert.Equal(CargoDiscrepancyStatus.Reported, entry.FromStatus);
        Assert.Equal(CargoDiscrepancyStatus.Investigating, entry.ToStatus);
        Assert.Equal(InvestigatorUserId, entry.ActorUserId);
        Assert.Equal(LaterUtc, entry.OccurredAtUtc);
    }

    [Fact]
    public void StartInvestigation_is_rejected_twice()
    {
        var discrepancy = Investigating();

        var exception = Assert.Throws<CargoDiscrepancyTransitionNotAllowedException>(
            () => discrepancy.StartInvestigation(InvestigatorUserId, InvestigatorName, LaterUtc));

        Assert.Equal(CargoDiscrepancyTransitionNotAllowedException.AlreadyInvestigatingReason, exception.Reason);
    }

    [Theory]
    [InlineData(nameof(CargoDiscrepancyStatus.Reported))]
    [InlineData(nameof(CargoDiscrepancyStatus.Investigating))]
    public void AddNote_appends_an_attributable_note_that_keeps_the_status(string statusName)
    {
        var status = Enum.Parse<CargoDiscrepancyStatus>(statusName);
        var discrepancy = status is CargoDiscrepancyStatus.Reported ? Reported() : Investigating();

        var entry = discrepancy.AddNote("  Photos taken at berth 4  ", InvestigatorUserId, InvestigatorName, LaterUtc);

        Assert.Equal(status, discrepancy.Status);
        Assert.Equal(CargoDiscrepancyEventType.NoteAdded, entry.EventType);
        Assert.Equal(status, entry.FromStatus);
        Assert.Equal(status, entry.ToStatus);
        Assert.Equal("Photos taken at berth 4", entry.Text);
        Assert.Equal(InvestigatorUserId, entry.ActorUserId);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void AddNote_requires_text(string note)
    {
        Assert.Throws<CargoDiscrepancyTextRequiredException>(
            () => Investigating().AddNote(note, InvestigatorUserId, InvestigatorName, LaterUtc));
    }

    [Fact]
    public void AddNote_is_rejected_once_the_discrepancy_is_closed()
    {
        var discrepancy = Resolved();

        var exception = Assert.Throws<CargoDiscrepancyTransitionNotAllowedException>(
            () => discrepancy.AddNote("Late remark", InvestigatorUserId, InvestigatorName, LaterUtc));

        Assert.Equal(CargoDiscrepancyTransitionNotAllowedException.AlreadyResolvedReason, exception.Reason);
    }

    [Theory]
    [InlineData(nameof(CargoDiscrepancyDisposition.Corrected))]
    [InlineData(nameof(CargoDiscrepancyDisposition.AcceptedDeviation))]
    [InlineData(nameof(CargoDiscrepancyDisposition.NoDiscrepancyFound))]
    public void Resolve_closes_an_investigation_with_an_attributable_disposition(string dispositionName)
    {
        var disposition = Enum.Parse<CargoDiscrepancyDisposition>(dispositionName);
        var discrepancy = Investigating();

        var entry = discrepancy.Resolve(disposition, $"  {Summary}  ", 1, null, InvestigatorUserId, InvestigatorName, LaterUtc);

        Assert.Equal(CargoDiscrepancyStatus.Resolved, discrepancy.Status);
        Assert.False(discrepancy.IsOpen);
        Assert.Equal(disposition, discrepancy.Disposition);
        Assert.Equal(InvestigatorUserId, discrepancy.ResolvedByUserId);
        Assert.Equal(InvestigatorName, discrepancy.ResolvedByName);
        Assert.Equal(InvestigatorName, entry.ActorName);
        Assert.Equal(LaterUtc, discrepancy.ResolvedAtUtc);
        Assert.Equal(CargoDiscrepancyEventType.Resolved, entry.EventType);
        Assert.Equal(CargoDiscrepancyStatus.Investigating, entry.FromStatus);
        Assert.Equal(CargoDiscrepancyStatus.Resolved, entry.ToStatus);
        Assert.Equal(Summary, entry.Text);
    }

    [Fact]
    public void Resolve_requires_an_investigation_first()
    {
        var exception = Assert.Throws<CargoDiscrepancyTransitionNotAllowedException>(
            () => Reported().Resolve(CargoDiscrepancyDisposition.Corrected, Summary, 1, null, InvestigatorUserId, InvestigatorName, LaterUtc));

        Assert.Equal(CargoDiscrepancyTransitionNotAllowedException.NotInvestigatingReason, exception.Reason);
    }

    [Fact]
    public void Resolve_requires_an_investigation_note()
    {
        var discrepancy = Investigating();

        var exception = Assert.Throws<CargoDiscrepancyTransitionNotAllowedException>(
            () => discrepancy.Resolve(CargoDiscrepancyDisposition.Corrected, Summary, 0, null, InvestigatorUserId, InvestigatorName, LaterUtc));

        Assert.Equal(CargoDiscrepancyTransitionNotAllowedException.NoInvestigationNoteReason, exception.Reason);
        Assert.Equal(CargoDiscrepancyStatus.Investigating, discrepancy.Status);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Too short summary.")]
    public void Resolve_requires_a_substantive_summary(string summary)
    {
        var discrepancy = Investigating();

        Assert.Throws<CargoDiscrepancyTextRequiredException>(
            () => discrepancy.Resolve(CargoDiscrepancyDisposition.Corrected, summary, 1, null, InvestigatorUserId, InvestigatorName, LaterUtc));

        Assert.Equal(CargoDiscrepancyStatus.Investigating, discrepancy.Status);
        Assert.Null(discrepancy.Disposition);
    }

    [Fact]
    public void Resolve_accepts_a_summary_of_exactly_the_minimum_length()
    {
        var discrepancy = Investigating();

        discrepancy.Resolve(
            CargoDiscrepancyDisposition.Corrected,
            new string('x', CargoDiscrepancy.ResolutionSummaryMinLength),
            1,
            null,
            InvestigatorUserId,
            InvestigatorName,
            LaterUtc);

        Assert.Equal(CargoDiscrepancyStatus.Resolved, discrepancy.Status);
    }

    [Fact]
    public void Resolve_waits_for_an_open_linked_excursion()
    {
        var discrepancy = LinkedInvestigating(out var excursion);

        var exception = Assert.Throws<CargoDiscrepancySourceExcursionOpenException>(
            () => discrepancy.Resolve(CargoDiscrepancyDisposition.AcceptedDeviation, Summary, 1, excursion.Status, InvestigatorUserId, InvestigatorName, LaterUtc));

        Assert.Equal(excursion.Id, exception.ExcursionId);
        Assert.Equal(CargoDiscrepancyStatus.Investigating, discrepancy.Status);
    }

    [Fact]
    public void Resolve_proceeds_once_the_linked_excursion_has_a_disposition()
    {
        var discrepancy = LinkedInvestigating(out var excursion);
        excursion.Resolve(ReeferExcursionDisposition.AcceptedDeviation, "Shipper accepted", InvestigatorUserId, LaterUtc);

        discrepancy.Resolve(CargoDiscrepancyDisposition.AcceptedDeviation, Summary, 1, excursion.Status, InvestigatorUserId, InvestigatorName, LaterUtc);

        Assert.Equal(CargoDiscrepancyStatus.Resolved, discrepancy.Status);
    }

    [Fact]
    public void Resolve_proceeds_once_the_linked_excursion_is_superseded()
    {
        var discrepancy = LinkedInvestigating(out var excursion);
        excursion.Supersede(null, LaterUtc);

        discrepancy.Resolve(CargoDiscrepancyDisposition.NoDiscrepancyFound, Summary, 1, excursion.Status, InvestigatorUserId, InvestigatorName, LaterUtc);

        Assert.Equal(CargoDiscrepancyStatus.Resolved, discrepancy.Status);
    }

    [Fact]
    public void Resolve_leaves_the_linked_excursion_untouched()
    {
        var discrepancy = LinkedInvestigating(out var excursion);
        excursion.Resolve(ReeferExcursionDisposition.CorrectiveAction, "Unit reset", InvestigatorUserId, LaterUtc);
        var token = excursion.ConcurrencyToken;

        discrepancy.Resolve(CargoDiscrepancyDisposition.Corrected, Summary, 1, excursion.Status, InvestigatorUserId, InvestigatorName, LaterUtc);

        Assert.Equal(ReeferExcursionStatus.Resolved, excursion.Status);
        Assert.Equal(token, excursion.ConcurrencyToken);
    }

    [Theory]
    [InlineData(nameof(CargoDiscrepancyStatus.Reported))]
    [InlineData(nameof(CargoDiscrepancyStatus.Investigating))]
    public void Void_closes_an_open_discrepancy_with_a_reason(string statusName)
    {
        var status = Enum.Parse<CargoDiscrepancyStatus>(statusName);
        var discrepancy = status is CargoDiscrepancyStatus.Reported ? Reported() : Investigating();

        var entry = discrepancy.Void("  Registered twice  ", InvestigatorUserId, InvestigatorName, LaterUtc);

        Assert.Equal(CargoDiscrepancyStatus.Voided, discrepancy.Status);
        Assert.False(discrepancy.IsOpen);
        Assert.Equal(InvestigatorUserId, discrepancy.VoidedByUserId);
        Assert.Equal(LaterUtc, discrepancy.VoidedAtUtc);
        Assert.Null(discrepancy.Disposition);
        Assert.Equal(CargoDiscrepancyEventType.Voided, entry.EventType);
        Assert.Equal(status, entry.FromStatus);
        Assert.Equal(CargoDiscrepancyStatus.Voided, entry.ToStatus);
        Assert.Equal("Registered twice", entry.Text);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void Void_requires_a_reason(string reason)
    {
        var discrepancy = Reported();

        Assert.Throws<CargoDiscrepancyTextRequiredException>(
            () => discrepancy.Void(reason, InvestigatorUserId, InvestigatorName, LaterUtc));

        Assert.Equal(CargoDiscrepancyStatus.Reported, discrepancy.Status);
    }

    [Fact]
    public void Void_is_rejected_after_resolution()
    {
        var exception = Assert.Throws<CargoDiscrepancyTransitionNotAllowedException>(
            () => Resolved().Void("Changed my mind", InvestigatorUserId, InvestigatorName, LaterUtc));

        Assert.Equal(CargoDiscrepancyTransitionNotAllowedException.AlreadyResolvedReason, exception.Reason);
    }

    [Fact]
    public void A_voided_discrepancy_cannot_be_investigated_or_resolved()
    {
        var discrepancy = Reported();
        discrepancy.Void("Duplicate", InvestigatorUserId, InvestigatorName, LaterUtc);

        var start = Assert.Throws<CargoDiscrepancyTransitionNotAllowedException>(
            () => discrepancy.StartInvestigation(InvestigatorUserId, InvestigatorName, LaterUtc));
        var resolve = Assert.Throws<CargoDiscrepancyTransitionNotAllowedException>(
            () => discrepancy.Resolve(CargoDiscrepancyDisposition.Corrected, Summary, 1, null, InvestigatorUserId, InvestigatorName, LaterUtc));
        var again = Assert.Throws<CargoDiscrepancyTransitionNotAllowedException>(
            () => discrepancy.Void("Again", InvestigatorUserId, InvestigatorName, LaterUtc));

        Assert.All(
            [start, resolve, again],
            exception => Assert.Equal(CargoDiscrepancyTransitionNotAllowedException.AlreadyVoidedReason, exception.Reason));
    }

    [Fact]
    public void Only_reported_and_investigating_discrepancies_block_completion()
    {
        Assert.True(CargoDiscrepancy.BlocksCompletion(CargoDiscrepancyStatus.Reported));
        Assert.True(CargoDiscrepancy.BlocksCompletion(CargoDiscrepancyStatus.Investigating));
        Assert.False(CargoDiscrepancy.BlocksCompletion(CargoDiscrepancyStatus.Resolved));
        Assert.False(CargoDiscrepancy.BlocksCompletion(CargoDiscrepancyStatus.Voided));
    }

    [Fact]
    public void Advancing_or_touching_a_booking_changes_its_concurrency_token()
    {
        var booking = Booking(CargoStatus.Unloaded);
        var original = booking.ConcurrencyToken;

        booking.Touch();
        var touched = booking.ConcurrencyToken;
        booking.AdvanceStatusTo(CargoStatus.Completed);

        Assert.NotEqual(original, touched);
        Assert.NotEqual(touched, booking.ConcurrencyToken);
    }

    private static CargoDiscrepancy Reported()
    {
        var booking = Booking(CargoStatus.Unloaded);

        return Register(booking, ContainerOn(booking), CargoDiscrepancyType.Damage).Discrepancy;
    }

    private static CargoDiscrepancy Investigating()
    {
        var discrepancy = Reported();
        discrepancy.StartInvestigation(InvestigatorUserId, InvestigatorName, LaterUtc);

        return discrepancy;
    }

    private static CargoDiscrepancy Resolved()
    {
        var discrepancy = Investigating();
        discrepancy.Resolve(CargoDiscrepancyDisposition.Corrected, Summary, 1, null, InvestigatorUserId, InvestigatorName, LaterUtc);

        return discrepancy;
    }

    private static CargoDiscrepancy LinkedInvestigating(out ReeferExcursion excursion)
    {
        var booking = Booking(CargoStatus.Arrived);
        var reefer = ContainerOn(booking, "42R1");
        excursion = ExcursionOn(reefer, 7.5m);

        var (discrepancy, _) = CargoDiscrepancy.Register(
            booking,
            reefer,
            excursion,
            CargoDiscrepancyType.TemperatureRelated,
            CargoDiscrepancySeverity.High,
            "Warm cargo",
            ObservedAtUtc,
            ReporterUserId,
            ReporterName,
            ReportedAtUtc);

        discrepancy.StartInvestigation(InvestigatorUserId, InvestigatorName, LaterUtc);

        return discrepancy;
    }

    private static (CargoDiscrepancy Discrepancy, CargoDiscrepancyEvent Reported) Register(
        CargoBooking booking,
        Container? container,
        CargoDiscrepancyType type)
    {
        return CargoDiscrepancy.Register(
            booking,
            container,
            null,
            type,
            CargoDiscrepancySeverity.Medium,
            "Observed at discharge",
            ObservedAtUtc,
            ReporterUserId,
            ReporterName,
            ReportedAtUtc);
    }

    private static CargoBooking Booking(CargoStatus status)
    {
        var booking = CargoBooking.Register(
            $"BK-DSC-{Guid.NewGuid():N}"[..20],
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

        return booking;
    }

    private static Container ContainerOn(CargoBooking booking, string containerType = "40HC")
    {
        return Container.Register(
            $"MSKU{Random.Shared.Next(1_000_000, 9_999_999)}",
            containerType,
            CargoStatus.Loaded,
            cargoBookingId: booking.Id);
    }

    private static ReeferExcursion ExcursionOn(Container reefer, decimal temperatureCelsius)
    {
        var profile = ReeferMonitoringProfile.Configure(
            reefer.Id,
            isReefer: true,
            -2m,
            4m,
            1m,
            InvestigatorUserId,
            ObservedAtUtc.AddDays(-1));

        var reading = ReeferTemperatureReading.Record(
            profile,
            temperatureCelsius,
            ObservedAtUtc,
            null,
            ReporterUserId,
            ReportedAtUtc);

        return ReeferExcursion.Open(reading);
    }
}
