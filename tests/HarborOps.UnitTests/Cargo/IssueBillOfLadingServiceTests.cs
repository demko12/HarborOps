using HarborOps.Infrastructure.Persistence;
using HarborOps.Modules.Cargo.Application;
using HarborOps.Modules.Cargo.Contracts;
using HarborOps.Modules.Cargo.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace HarborOps.UnitTests.Cargo;

public sealed class IssueBillOfLadingServiceTests
{
    private static readonly DateTimeOffset FixedUtcNow =
        new(2026, 9, 9, 10, 30, 0, TimeSpan.Zero);

    private static readonly Guid ActorUserId =
        Guid.Parse("5e4d3c2b-1a09-4f8e-8d7c-6b5a49382716");

    [Fact]
    public async Task IssueAsync_records_the_issuer_the_timestamp_and_the_status()
    {
        await using var dbContext = CreateDbContext();
        var context = await SeedIssuableAsync(dbContext);

        var details = await CreateService(dbContext).IssueAsync(
            context.BillOfLadingId,
            ActorUserId,
            CancellationToken.None);

        Assert.Equal(nameof(BillOfLadingStatus.Issued), details.Status);
        Assert.False(details.IsEditable);
        Assert.Equal(ActorUserId, details.IssuedByUserId);
        Assert.Equal(FixedUtcNow.UtcDateTime, details.IssuedAtUtc);
    }

    [Fact]
    public async Task IssueAsync_snapshots_the_booking_fields()
    {
        await using var dbContext = CreateDbContext();
        var context = await SeedIssuableAsync(dbContext);

        var details = await CreateService(dbContext).IssueAsync(
            context.BillOfLadingId,
            ActorUserId,
            CancellationToken.None);

        Assert.Equal("BK-2026-0001", details.SnapshotBookingNumber);
        Assert.Equal(context.VoyageId, details.SnapshotVoyageId);
        Assert.Equal("General cargo", details.SnapshotCargoType);
        Assert.Equal("Machine parts", details.SnapshotBookingDescription);
        Assert.Equal(480, details.SnapshotBookingPackageCount);
        Assert.Equal(12400m, details.SnapshotBookingWeightKg);
        Assert.Equal(nameof(CargoStatus.Loaded), details.SnapshotBookingStatus);
    }

    [Fact]
    public async Task IssueAsync_snapshots_the_selected_container_numbers_and_types()
    {
        await using var dbContext = CreateDbContext();
        var context = await SeedIssuableAsync(dbContext);

        var details = await CreateService(dbContext).IssueAsync(
            context.BillOfLadingId,
            ActorUserId,
            CancellationToken.None);

        var container = Assert.Single(details.Containers);

        Assert.Equal("MSKU1111111", container.ContainerNumber);
        Assert.Equal("40HC", container.ContainerType);
        Assert.True(container.IsSnapshotted);

        var stored = await dbContext.Set<BillOfLadingContainer>()
            .SingleAsync(selection => selection.BillOfLadingId == context.BillOfLadingId);

        Assert.Equal("MSKU1111111", stored.SnapshotContainerNumber);
        Assert.Equal("40HC", stored.SnapshotContainerType);
    }

    [Fact]
    public async Task An_issued_bill_keeps_its_snapshot_when_the_booking_moves_on()
    {
        await using var dbContext = CreateDbContext();
        var context = await SeedIssuableAsync(dbContext);

        await CreateService(dbContext).IssueAsync(
            context.BillOfLadingId,
            ActorUserId,
            CancellationToken.None);

        var booking = await dbContext.Set<CargoBooking>().SingleAsync();
        booking.AdvanceStatusTo(CargoStatus.InTransit);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var bill = await dbContext.Set<BillOfLading>()
            .SingleAsync(candidate => candidate.Id == context.BillOfLadingId);

        Assert.Equal(CargoStatus.InTransit, booking.Status);
        Assert.Equal(CargoStatus.Loaded, bill.SnapshotBookingStatus);
        Assert.Equal("BK-2026-0001", bill.SnapshotBookingNumber);
        Assert.Equal(480, bill.SnapshotBookingPackageCount);
    }

    [Fact]
    public async Task An_issued_bill_reads_its_containers_from_the_snapshot()
    {
        await using var dbContext = CreateDbContext();
        var context = await SeedIssuableAsync(dbContext);

        await CreateService(dbContext).IssueAsync(
            context.BillOfLadingId,
            ActorUserId,
            CancellationToken.None);

        var bill = await dbContext.Set<BillOfLading>()
            .SingleAsync(candidate => candidate.Id == context.BillOfLadingId);
        var selections = await dbContext.Set<BillOfLadingContainer>()
            .Where(selection => selection.BillOfLadingId == context.BillOfLadingId)
            .ToListAsync();

        var details = BillOfLadingMapper.ToDetails(bill, CargoStatus.Loaded, selections, []);

        Assert.Equal("MSKU1111111", Assert.Single(details.Containers).ContainerNumber);
    }

    [Fact]
    public async Task IssueAsync_appends_a_history_entry()
    {
        await using var dbContext = CreateDbContext();
        var context = await SeedIssuableAsync(dbContext);

        await CreateService(dbContext).IssueAsync(
            context.BillOfLadingId,
            ActorUserId,
            CancellationToken.None);

        var entries = await dbContext.Set<BillOfLadingEvent>()
            .Where(entry => entry.BillOfLadingId == context.BillOfLadingId)
            .ToListAsync();

        var issued = Assert.Single(entries, entry => entry.Action == BillOfLadingAction.Issued);

        Assert.Equal(BillOfLadingStatus.Draft, issued.FromStatus);
        Assert.Equal(BillOfLadingStatus.Issued, issued.ToStatus);
        Assert.Equal(ActorUserId, issued.ActorUserId);
        Assert.Equal(FixedUtcNow.UtcDateTime, issued.OccurredUtc);
    }

    [Fact]
    public async Task IssueAsync_is_rejected_before_the_booking_is_loaded()
    {
        await using var dbContext = CreateDbContext();
        var context = await SeedIssuableAsync(dbContext, bookingStatus: CargoStatus.Planned);

        var exception = await Assert.ThrowsAsync<BillOfLadingTransitionException>(
            () => CreateService(dbContext).IssueAsync(
                context.BillOfLadingId,
                ActorUserId,
                CancellationToken.None));

        Assert.Equal(
            BillOfLadingTransitionNotAllowedException.BookingNotLoadedReason,
            exception.Reason);
    }

    [Fact]
    public async Task IssueAsync_is_rejected_without_a_selected_container()
    {
        await using var dbContext = CreateDbContext();
        var context = await SeedIssuableAsync(dbContext, selectContainer: false);

        var exception = await Assert.ThrowsAsync<BillOfLadingTransitionException>(
            () => CreateService(dbContext).IssueAsync(
                context.BillOfLadingId,
                ActorUserId,
                CancellationToken.None));

        Assert.Equal(
            BillOfLadingTransitionNotAllowedException.NoContainersReason,
            exception.Reason);
    }

    [Fact]
    public async Task IssueAsync_is_rejected_when_the_bill_is_already_issued()
    {
        await using var dbContext = CreateDbContext();
        var context = await SeedIssuableAsync(dbContext);
        var service = CreateService(dbContext);

        await service.IssueAsync(context.BillOfLadingId, ActorUserId, CancellationToken.None);

        var exception = await Assert.ThrowsAsync<BillOfLadingTransitionException>(
            () => service.IssueAsync(
                context.BillOfLadingId,
                ActorUserId,
                CancellationToken.None));

        Assert.Equal(
            BillOfLadingTransitionNotAllowedException.AlreadyIssuedReason,
            exception.Reason);
    }

    [Fact]
    public async Task IssueAsync_rejects_a_container_that_moved_to_another_booking()
    {
        await using var dbContext = CreateDbContext();
        var context = await SeedIssuableAsync(dbContext);

        var selection = await dbContext.Set<BillOfLadingContainer>()
            .SingleAsync(candidate => candidate.BillOfLadingId == context.BillOfLadingId);
        var stray = Container.Register(
            "HLXU9999999",
            "20GP",
            CargoStatus.Registered,
            cargoBookingId: Guid.NewGuid());

        await dbContext.Set<Container>().AddAsync(stray, CancellationToken.None);
        dbContext.Set<BillOfLadingContainer>().Remove(selection);
        await dbContext.Set<BillOfLadingContainer>().AddAsync(
            BillOfLadingContainer.Select(context.BillOfLadingId, stray.Id),
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var exception = await Assert.ThrowsAsync<ContainerNotInBookingException>(
            () => CreateService(dbContext).IssueAsync(
                context.BillOfLadingId,
                ActorUserId,
                CancellationToken.None));

        Assert.Equal(stray.Id, Assert.Single(exception.ContainerIds));
    }

    [Fact]
    public async Task IssueAsync_rejects_an_unknown_bill()
    {
        await using var dbContext = CreateDbContext();

        await Assert.ThrowsAsync<BillOfLadingNotFoundException>(
            () => CreateService(dbContext).IssueAsync(
                Guid.NewGuid(),
                ActorUserId,
                CancellationToken.None));
    }

    [Fact]
    public async Task IssueAsync_writes_nothing_when_the_save_fails()
    {
        var databaseName = $"HarborOpsUnitTests-{Guid.NewGuid()}";
        SeededBill context;

        await using (var seedContext = CreateDbContext(databaseName))
        {
            context = await SeedIssuableAsync(seedContext);
        }

        await using (var failingContext = CreateDbContext(databaseName, failOnSave: true))
        {
            await Assert.ThrowsAsync<DbUpdateException>(
                () => CreateService(failingContext).IssueAsync(
                    context.BillOfLadingId,
                    ActorUserId,
                    CancellationToken.None));
        }

        await using var verifyContext = CreateDbContext(databaseName);
        var bill = await verifyContext.Set<BillOfLading>()
            .SingleAsync(candidate => candidate.Id == context.BillOfLadingId);
        var selection = await verifyContext.Set<BillOfLadingContainer>()
            .SingleAsync(candidate => candidate.BillOfLadingId == context.BillOfLadingId);

        Assert.Equal(BillOfLadingStatus.Draft, bill.Status);
        Assert.Null(bill.IssuedAtUtc);
        Assert.Null(selection.SnapshotContainerNumber);
        Assert.DoesNotContain(
            await verifyContext.Set<BillOfLadingEvent>().ToListAsync(),
            entry => entry.Action == BillOfLadingAction.Issued);
    }

    private static IssueBillOfLadingService CreateService(HarborOpsDbContext dbContext)
    {
        return new IssueBillOfLadingService(dbContext, new FixedTimeProvider(FixedUtcNow));
    }

    private static async Task<SeededBill> SeedIssuableAsync(
        HarborOpsDbContext dbContext,
        CargoStatus bookingStatus = CargoStatus.Loaded,
        bool selectContainer = true)
    {
        var voyageId = Guid.NewGuid();
        var booking = CargoBooking.Register(
            "BK-2026-0001",
            voyageId,
            "Machine parts",
            "General cargo",
            12400m,
            480,
            false);

        while (booking.Status != bookingStatus)
        {
            booking.AdvanceStatusTo(booking.NextStatus!.Value);
        }

        await dbContext.Set<CargoBooking>().AddAsync(booking, CancellationToken.None);

        var container = Container.Register(
            "MSKU1111111",
            "40HC",
            CargoStatus.Registered,
            cargoBookingId: booking.Id);

        await dbContext.Set<Container>().AddAsync(container, CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var created = await new CreateBillOfLadingDraftService(
            dbContext,
            new FixedTimeProvider(FixedUtcNow)).CreateAsync(
            booking.Id,
            ActorUserId,
            Request(selectContainer ? [container.Id] : null),
            CancellationToken.None);

        return new SeededBill(created.BillOfLadingId, booking.Id, container.Id, voyageId);
    }

    private static SaveBillOfLadingDraftRequest Request(IReadOnlyList<Guid>? containerIds)
    {
        return new SaveBillOfLadingDraftRequest
        {
            BillNumber = "BL-2026-0001",
            ShipperName = "Adriatic Trade",
            ShipperAddress = "Bulevar Revolucije 1, Bar",
            ConsigneeName = "Nordic Imports",
            ConsigneeAddress = "Hamngatan 4, Goteborg",
            NotifyPartyName = "Port Agency",
            NotifyPartyAddress = "Obala 2, Bar",
            PlaceOfReceipt = "Bar",
            PortOfLoading = "Rotterdam",
            PortOfDischarge = "Bar",
            PlaceOfDelivery = "Podgorica",
            FreightTerms = nameof(FreightTerms.Prepaid),
            CargoMarks = "ADT/2026/0001",
            CargoDescription = "Machine parts, palletised",
            PackageCount = 480,
            WeightKg = 12400m,
            IssuePlace = "Bar",
            RequestedIssueDate = new DateOnly(2026, 9, 9),
            ContainerIds = containerIds,
        };
    }

    private static HarborOpsDbContext CreateDbContext(
        string? databaseName = null,
        bool failOnSave = false)
    {
        var builder = new DbContextOptionsBuilder<HarborOpsDbContext>()
            .UseInMemoryDatabase(databaseName ?? $"HarborOpsUnitTests-{Guid.NewGuid()}");

        if (failOnSave)
        {
            builder.AddInterceptors(new FailingSaveChangesInterceptor());
        }

        return new HarborOpsDbContext(builder.Options);
    }

    private sealed record SeededBill(
        Guid BillOfLadingId,
        Guid CargoBookingId,
        Guid ContainerId,
        Guid VoyageId);

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow()
        {
            return utcNow;
        }
    }

    private sealed class FailingSaveChangesInterceptor : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            throw new DbUpdateException("The save was rejected.");
        }
    }
}
