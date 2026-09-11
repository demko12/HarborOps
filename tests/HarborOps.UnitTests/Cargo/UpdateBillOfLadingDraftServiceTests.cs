using HarborOps.Infrastructure.Persistence;
using HarborOps.Modules.Cargo.Application;
using HarborOps.Modules.Cargo.Contracts;
using HarborOps.Modules.Cargo.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.UnitTests.Cargo;

public sealed class UpdateBillOfLadingDraftServiceTests
{
    private static readonly DateTimeOffset FixedUtcNow =
        new(2026, 9, 8, 8, 0, 0, TimeSpan.Zero);

    private static readonly Guid ActorUserId =
        Guid.Parse("5e4d3c2b-1a09-4f8e-8d7c-6b5a49382716");

    [Fact]
    public async Task UpdateAsync_replaces_the_draft_values()
    {
        await using var dbContext = CreateDbContext();
        var booking = await SeedBookingAsync(dbContext);
        var created = await CreateDraftAsync(dbContext, booking.Id);

        var details = await CreateService(dbContext).UpdateAsync(
            created.BillOfLadingId,
            ActorUserId,
            Request() with { ConsigneeName = "Baltic Freight" },
            CancellationToken.None);

        Assert.Equal("Baltic Freight", details.ConsigneeName);
        Assert.Equal(nameof(BillOfLadingStatus.Draft), details.Status);
    }

    [Fact]
    public async Task UpdateAsync_adds_and_removes_selected_containers()
    {
        await using var dbContext = CreateDbContext();
        var booking = await SeedBookingAsync(dbContext);
        var first = await SeedContainerAsync(dbContext, booking.Id, "MSKU1111111");
        var second = await SeedContainerAsync(dbContext, booking.Id, "TCLU2222222");
        var created = await CreateDraftAsync(dbContext, booking.Id, [first.Id]);

        var details = await CreateService(dbContext).UpdateAsync(
            created.BillOfLadingId,
            ActorUserId,
            Request(containerIds: [second.Id]),
            CancellationToken.None);

        Assert.Equal("TCLU2222222", Assert.Single(details.Containers).ContainerNumber);

        var stored = await dbContext.Set<BillOfLadingContainer>()
            .Where(selection => selection.BillOfLadingId == created.BillOfLadingId)
            .ToListAsync();

        Assert.Equal(second.Id, Assert.Single(stored).ContainerId);
    }

    [Fact]
    public async Task UpdateAsync_appends_a_history_entry()
    {
        await using var dbContext = CreateDbContext();
        var booking = await SeedBookingAsync(dbContext);
        var created = await CreateDraftAsync(dbContext, booking.Id);

        await CreateService(dbContext).UpdateAsync(
            created.BillOfLadingId,
            ActorUserId,
            Request(),
            CancellationToken.None);

        var entries = await dbContext.Set<BillOfLadingEvent>()
            .Where(entry => entry.BillOfLadingId == created.BillOfLadingId)
            .ToListAsync();

        Assert.Equal(2, entries.Count);
        Assert.Contains(entries, entry => entry.Action == BillOfLadingAction.Updated);
    }

    [Fact]
    public async Task UpdateAsync_keeps_the_bill_number_available_to_itself()
    {
        await using var dbContext = CreateDbContext();
        var booking = await SeedBookingAsync(dbContext);
        var created = await CreateDraftAsync(dbContext, booking.Id);

        var details = await CreateService(dbContext).UpdateAsync(
            created.BillOfLadingId,
            ActorUserId,
            Request(billNumber: "BL-2026-0001"),
            CancellationToken.None);

        Assert.Equal("BL-2026-0001", details.BillNumber);
    }

    [Fact]
    public async Task UpdateAsync_rejects_a_bill_number_used_by_another_bill()
    {
        await using var dbContext = CreateDbContext();
        var booking = await SeedBookingAsync(dbContext);
        var first = await CreateDraftAsync(dbContext, booking.Id, billNumber: "BL-2026-0001");
        await CreateDraftAsync(dbContext, booking.Id, billNumber: "BL-2026-0002");

        await Assert.ThrowsAsync<DuplicateBillNumberException>(
            () => CreateService(dbContext).UpdateAsync(
                first.BillOfLadingId,
                ActorUserId,
                Request(billNumber: "BL-2026-0002"),
                CancellationToken.None));
    }

    [Fact]
    public async Task UpdateAsync_rejects_a_container_from_another_booking()
    {
        await using var dbContext = CreateDbContext();
        var booking = await SeedBookingAsync(dbContext);
        var other = await SeedBookingAsync(dbContext, "BK-2026-0002");
        var foreign = await SeedContainerAsync(dbContext, other.Id, "MSKU9999999");
        var created = await CreateDraftAsync(dbContext, booking.Id);

        await Assert.ThrowsAsync<ContainerNotInBookingException>(
            () => CreateService(dbContext).UpdateAsync(
                created.BillOfLadingId,
                ActorUserId,
                Request(containerIds: [foreign.Id]),
                CancellationToken.None));
    }

    [Fact]
    public async Task UpdateAsync_rejects_a_container_held_by_another_bill()
    {
        await using var dbContext = CreateDbContext();
        var booking = await SeedBookingAsync(dbContext);
        var container = await SeedContainerAsync(dbContext, booking.Id, "MSKU1111111");
        await CreateDraftAsync(dbContext, booking.Id, [container.Id], "BL-2026-0001");
        var second = await CreateDraftAsync(dbContext, booking.Id, billNumber: "BL-2026-0002");

        var exception = await Assert.ThrowsAsync<ContainerAlreadyOnBillException>(
            () => CreateService(dbContext).UpdateAsync(
                second.BillOfLadingId,
                ActorUserId,
                Request(billNumber: "BL-2026-0002", containerIds: [container.Id]),
                CancellationToken.None));

        Assert.Equal("MSKU1111111", Assert.Single(exception.ContainerNumbers));
    }

    [Fact]
    public async Task UpdateAsync_keeps_a_container_the_same_bill_already_holds()
    {
        await using var dbContext = CreateDbContext();
        var booking = await SeedBookingAsync(dbContext);
        var container = await SeedContainerAsync(dbContext, booking.Id, "MSKU1111111");
        var created = await CreateDraftAsync(dbContext, booking.Id, [container.Id]);

        var details = await CreateService(dbContext).UpdateAsync(
            created.BillOfLadingId,
            ActorUserId,
            Request(containerIds: [container.Id]),
            CancellationToken.None);

        Assert.Equal("MSKU1111111", Assert.Single(details.Containers).ContainerNumber);
    }

    [Fact]
    public async Task UpdateAsync_rejects_totals_above_the_booking()
    {
        await using var dbContext = CreateDbContext();
        var booking = await SeedBookingAsync(dbContext);
        var created = await CreateDraftAsync(dbContext, booking.Id);

        var exception = await Assert.ThrowsAsync<BillOfLadingValidationException>(
            () => CreateService(dbContext).UpdateAsync(
                created.BillOfLadingId,
                ActorUserId,
                Request() with { PackageCount = 999 },
                CancellationToken.None));

        Assert.True(exception.Errors.ContainsKey(BillOfLadingValidator.PackageCountField));
    }

    [Fact]
    public async Task UpdateAsync_rejects_an_unknown_bill()
    {
        await using var dbContext = CreateDbContext();

        await Assert.ThrowsAsync<BillOfLadingNotFoundException>(
            () => CreateService(dbContext).UpdateAsync(
                Guid.NewGuid(),
                ActorUserId,
                Request(),
                CancellationToken.None));
    }

    [Fact]
    public async Task UpdateAsync_rejects_invalid_values_before_touching_the_bill()
    {
        await using var dbContext = CreateDbContext();
        var booking = await SeedBookingAsync(dbContext);
        var created = await CreateDraftAsync(dbContext, booking.Id);

        var exception = await Assert.ThrowsAsync<BillOfLadingValidationException>(
            () => CreateService(dbContext).UpdateAsync(
                created.BillOfLadingId,
                ActorUserId,
                Request() with { ShipperName = "   " },
                CancellationToken.None));

        Assert.True(exception.Errors.ContainsKey(BillOfLadingValidator.ShipperNameField));
    }

    private static UpdateBillOfLadingDraftService CreateService(HarborOpsDbContext dbContext)
    {
        return new UpdateBillOfLadingDraftService(
            dbContext,
            new FixedTimeProvider(FixedUtcNow));
    }

    private static async Task<BillOfLadingDetails> CreateDraftAsync(
        HarborOpsDbContext dbContext,
        Guid cargoBookingId,
        IReadOnlyList<Guid>? containerIds = null,
        string billNumber = "BL-2026-0001")
    {
        var service = new CreateBillOfLadingDraftService(
            dbContext,
            new FixedTimeProvider(FixedUtcNow));

        return await service.CreateAsync(
            cargoBookingId,
            ActorUserId,
            Request(billNumber, containerIds),
            CancellationToken.None);
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

    private static HarborOpsDbContext CreateDbContext()
    {
        var builder = new DbContextOptionsBuilder<HarborOpsDbContext>()
            .UseInMemoryDatabase($"HarborOpsUnitTests-{Guid.NewGuid()}");

        return new HarborOpsDbContext(builder.Options);
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow()
        {
            return utcNow;
        }
    }
}
