using HarborOps.Infrastructure.Persistence;
using HarborOps.Modules.Cargo.Application;
using HarborOps.Modules.Cargo.Contracts;
using HarborOps.Modules.Cargo.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace HarborOps.UnitTests.Cargo;

public sealed class CreateBillOfLadingDraftServiceTests
{
    private static readonly DateTimeOffset FixedUtcNow =
        new(2026, 9, 8, 8, 0, 0, TimeSpan.Zero);

    private static readonly Guid ActorUserId =
        Guid.Parse("5e4d3c2b-1a09-4f8e-8d7c-6b5a49382716");

    [Fact]
    public async Task CreateAsync_stores_a_draft_with_the_normalized_bill_number()
    {
        await using var dbContext = CreateDbContext();
        var booking = await SeedBookingAsync(dbContext);

        var details = await CreateService(dbContext).CreateAsync(
            booking.Id,
            ActorUserId,
            Request(billNumber: "  bl 2026 0001 "),
            CancellationToken.None);

        Assert.Equal("BL20260001", details.BillNumber);
        Assert.Equal(nameof(BillOfLadingStatus.Draft), details.Status);
        Assert.True(details.IsEditable);
        Assert.Equal(booking.Id, details.CargoBookingId);
        Assert.Null(details.IssuedAtUtc);
    }

    [Fact]
    public async Task CreateAsync_selects_the_requested_containers()
    {
        await using var dbContext = CreateDbContext();
        var booking = await SeedBookingAsync(dbContext);
        var first = await SeedContainerAsync(dbContext, booking.Id, "MSKU1111111");
        var second = await SeedContainerAsync(dbContext, booking.Id, "TCLU2222222");

        var details = await CreateService(dbContext).CreateAsync(
            booking.Id,
            ActorUserId,
            Request(containerIds: [second.Id, first.Id]),
            CancellationToken.None);

        Assert.Equal(2, details.Containers.Count);
        Assert.Equal("MSKU1111111", details.Containers[0].ContainerNumber);
        Assert.Equal("TCLU2222222", details.Containers[1].ContainerNumber);
        Assert.All(details.Containers, container => Assert.False(container.IsSnapshotted));
    }

    [Fact]
    public async Task CreateAsync_saves_the_draft_the_selection_and_the_history_together()
    {
        await using var dbContext = CreateDbContext();
        var booking = await SeedBookingAsync(dbContext);
        var container = await SeedContainerAsync(dbContext, booking.Id);

        var details = await CreateService(dbContext).CreateAsync(
            booking.Id,
            ActorUserId,
            Request(containerIds: [container.Id]),
            CancellationToken.None);

        var selection = await dbContext.Set<BillOfLadingContainer>()
            .SingleAsync(candidate => candidate.BillOfLadingId == details.BillOfLadingId);
        var entry = await dbContext.Set<BillOfLadingEvent>()
            .SingleAsync(candidate => candidate.BillOfLadingId == details.BillOfLadingId);

        Assert.Equal(container.Id, selection.ContainerId);
        Assert.True(selection.IsActiveSelection);
        Assert.Equal(BillOfLadingAction.Drafted, entry.Action);
        Assert.Equal(ActorUserId, entry.ActorUserId);
        Assert.Equal(FixedUtcNow.UtcDateTime, entry.OccurredUtc);
    }

    [Fact]
    public async Task CreateAsync_rejects_a_bill_number_that_is_already_used()
    {
        await using var dbContext = CreateDbContext();
        var booking = await SeedBookingAsync(dbContext);
        var service = CreateService(dbContext);

        await service.CreateAsync(
            booking.Id,
            ActorUserId,
            Request(billNumber: "BL-2026-0001"),
            CancellationToken.None);

        var exception = await Assert.ThrowsAsync<DuplicateBillNumberException>(
            () => service.CreateAsync(
                booking.Id,
                ActorUserId,
                Request(billNumber: "  bl-2026-0001  "),
                CancellationToken.None));

        Assert.Equal("BL-2026-0001", exception.BillNumber);
    }

    [Fact]
    public async Task CreateAsync_rejects_a_container_from_another_booking()
    {
        await using var dbContext = CreateDbContext();
        var booking = await SeedBookingAsync(dbContext);
        var other = await SeedBookingAsync(dbContext, "BK-2026-0002");
        var foreign = await SeedContainerAsync(dbContext, other.Id, "MSKU9999999");

        var exception = await Assert.ThrowsAsync<ContainerNotInBookingException>(
            () => CreateService(dbContext).CreateAsync(
                booking.Id,
                ActorUserId,
                Request(containerIds: [foreign.Id]),
                CancellationToken.None));

        Assert.Equal(booking.Id, exception.CargoBookingId);
        Assert.Equal(foreign.Id, Assert.Single(exception.ContainerIds));
    }

    [Fact]
    public async Task CreateAsync_rejects_a_container_that_is_already_on_another_bill()
    {
        await using var dbContext = CreateDbContext();
        var booking = await SeedBookingAsync(dbContext);
        var container = await SeedContainerAsync(dbContext, booking.Id, "MSKU1111111");
        var service = CreateService(dbContext);

        await service.CreateAsync(
            booking.Id,
            ActorUserId,
            Request(billNumber: "BL-2026-0001", containerIds: [container.Id]),
            CancellationToken.None);

        var exception = await Assert.ThrowsAsync<ContainerAlreadyOnBillException>(
            () => service.CreateAsync(
                booking.Id,
                ActorUserId,
                Request(billNumber: "BL-2026-0002", containerIds: [container.Id]),
                CancellationToken.None));

        Assert.Equal("MSKU1111111", Assert.Single(exception.ContainerNumbers));
    }

    [Fact]
    public async Task CreateAsync_rejects_an_unknown_booking()
    {
        await using var dbContext = CreateDbContext();

        await Assert.ThrowsAsync<CargoBookingNotFoundException>(
            () => CreateService(dbContext).CreateAsync(
                Guid.NewGuid(),
                ActorUserId,
                Request(),
                CancellationToken.None));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task CreateAsync_rejects_a_missing_bill_number(string billNumber)
    {
        await using var dbContext = CreateDbContext();
        var booking = await SeedBookingAsync(dbContext);

        var exception = await Assert.ThrowsAsync<BillOfLadingValidationException>(
            () => CreateService(dbContext).CreateAsync(
                booking.Id,
                ActorUserId,
                Request(billNumber: billNumber),
                CancellationToken.None));

        Assert.True(exception.Errors.ContainsKey(BillOfLadingValidator.BillNumberField));
    }

    [Fact]
    public async Task CreateAsync_rejects_unknown_freight_terms()
    {
        await using var dbContext = CreateDbContext();
        var booking = await SeedBookingAsync(dbContext);

        var exception = await Assert.ThrowsAsync<BillOfLadingValidationException>(
            () => CreateService(dbContext).CreateAsync(
                booking.Id,
                ActorUserId,
                Request() with { FreightTerms = "Barter" },
                CancellationToken.None));

        Assert.True(exception.Errors.ContainsKey(BillOfLadingValidator.FreightTermsField));
    }

    [Fact]
    public async Task CreateAsync_rejects_the_same_container_twice()
    {
        await using var dbContext = CreateDbContext();
        var booking = await SeedBookingAsync(dbContext);
        var container = await SeedContainerAsync(dbContext, booking.Id);

        var exception = await Assert.ThrowsAsync<BillOfLadingValidationException>(
            () => CreateService(dbContext).CreateAsync(
                booking.Id,
                ActorUserId,
                Request(containerIds: [container.Id, container.Id]),
                CancellationToken.None));

        Assert.True(exception.Errors.ContainsKey(BillOfLadingValidator.ContainerIdsField));
    }

    [Fact]
    public async Task CreateAsync_rejects_a_package_count_above_the_booking_total()
    {
        await using var dbContext = CreateDbContext();
        var booking = await SeedBookingAsync(dbContext);

        var exception = await Assert.ThrowsAsync<BillOfLadingValidationException>(
            () => CreateService(dbContext).CreateAsync(
                booking.Id,
                ActorUserId,
                Request() with { PackageCount = 481 },
                CancellationToken.None));

        Assert.True(exception.Errors.ContainsKey(BillOfLadingValidator.PackageCountField));
    }

    [Fact]
    public async Task CreateAsync_rejects_a_weight_above_the_booking_total()
    {
        await using var dbContext = CreateDbContext();
        var booking = await SeedBookingAsync(dbContext);

        var exception = await Assert.ThrowsAsync<BillOfLadingValidationException>(
            () => CreateService(dbContext).CreateAsync(
                booking.Id,
                ActorUserId,
                Request() with { WeightKg = 12400.001m },
                CancellationToken.None));

        Assert.True(exception.Errors.ContainsKey(BillOfLadingValidator.WeightKgField));
    }

    [Fact]
    public async Task CreateAsync_accepts_a_partial_share_of_the_booking()
    {
        await using var dbContext = CreateDbContext();
        var booking = await SeedBookingAsync(dbContext);

        var details = await CreateService(dbContext).CreateAsync(
            booking.Id,
            ActorUserId,
            Request() with { PackageCount = 200, WeightKg = 5000m },
            CancellationToken.None);

        Assert.Equal(200, details.PackageCount);
        Assert.Equal(5000m, details.WeightKg);
    }

    [Fact]
    public async Task CreateAsync_accepts_totals_equal_to_the_booking()
    {
        await using var dbContext = CreateDbContext();
        var booking = await SeedBookingAsync(dbContext);

        var details = await CreateService(dbContext).CreateAsync(
            booking.Id,
            ActorUserId,
            Request(),
            CancellationToken.None);

        Assert.Equal(480, details.PackageCount);
    }

    [Fact]
    public async Task CreateAsync_rejects_a_weight_that_is_not_positive()
    {
        await using var dbContext = CreateDbContext();
        var booking = await SeedBookingAsync(dbContext);

        var exception = await Assert.ThrowsAsync<BillOfLadingValidationException>(
            () => CreateService(dbContext).CreateAsync(
                booking.Id,
                ActorUserId,
                Request() with { WeightKg = 0m },
                CancellationToken.None));

        Assert.True(exception.Errors.ContainsKey(BillOfLadingValidator.WeightKgField));
    }

    [Fact]
    public async Task CreateAsync_reports_that_the_booking_is_not_loaded_yet()
    {
        await using var dbContext = CreateDbContext();
        var booking = await SeedBookingAsync(dbContext);
        var container = await SeedContainerAsync(dbContext, booking.Id);

        var details = await CreateService(dbContext).CreateAsync(
            booking.Id,
            ActorUserId,
            Request(containerIds: [container.Id]),
            CancellationToken.None);

        Assert.False(details.CanIssue);
        Assert.Equal(
            BillOfLadingTransitionNotAllowedException.BookingNotLoadedReason,
            details.IssueBlockedReason);
    }

    [Fact]
    public async Task CreateAsync_writes_nothing_when_the_save_fails()
    {
        var databaseName = $"HarborOpsUnitTests-{Guid.NewGuid()}";

        await using (var seedContext = CreateDbContext(databaseName))
        {
            await SeedBookingAsync(seedContext);
        }

        await using var dbContext = CreateDbContext(databaseName, failOnSave: true);
        var booking = await dbContext.Set<CargoBooking>().SingleAsync();

        await Assert.ThrowsAsync<DbUpdateException>(
            () => CreateService(dbContext).CreateAsync(
                booking.Id,
                ActorUserId,
                Request(),
                CancellationToken.None));

        await using var verifyContext = CreateDbContext(databaseName);

        Assert.Empty(await verifyContext.Set<BillOfLading>().ToListAsync());
        Assert.Empty(await verifyContext.Set<BillOfLadingEvent>().ToListAsync());
    }

    private static CreateBillOfLadingDraftService CreateService(HarborOpsDbContext dbContext)
    {
        return new CreateBillOfLadingDraftService(
            dbContext,
            new FixedTimeProvider(FixedUtcNow));
    }

    private static SaveBillOfLadingDraftRequest Request(
        string billNumber = "BL-2026-0001",
        IReadOnlyList<Guid>? containerIds = null)
    {
        return new SaveBillOfLadingDraftRequest
        {
            BillNumber = billNumber,
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
            RequestedIssueDate = new DateOnly(2026, 9, 8),
            ContainerIds = containerIds,
        };
    }

    private static async Task<CargoBooking> SeedBookingAsync(
        HarborOpsDbContext dbContext,
        string bookingNumber = "BK-2026-0001")
    {
        var booking = CargoBooking.Register(
            bookingNumber,
            Guid.NewGuid(),
            "Machine parts",
            "General cargo",
            12400m,
            480,
            false);

        await dbContext.Set<CargoBooking>().AddAsync(booking, CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        return booking;
    }

    private static async Task<Container> SeedContainerAsync(
        HarborOpsDbContext dbContext,
        Guid cargoBookingId,
        string containerNumber = "MSKU1234567")
    {
        var container = Container.Register(
            containerNumber,
            "40HC",
            CargoStatus.Registered,
            cargoBookingId: cargoBookingId);

        await dbContext.Set<Container>().AddAsync(container, CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        return container;
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
