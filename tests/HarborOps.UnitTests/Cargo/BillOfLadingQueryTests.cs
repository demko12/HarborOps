using HarborOps.Infrastructure.Persistence;
using HarborOps.Modules.Cargo.Application;
using HarborOps.Modules.Cargo.Contracts;
using HarborOps.Modules.Cargo.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.UnitTests.Cargo;

public sealed class BillOfLadingQueryTests
{
    private static readonly DateTimeOffset FixedUtcNow =
        new(2026, 9, 9, 10, 30, 0, TimeSpan.Zero);

    private static readonly Guid ActorUserId =
        Guid.Parse("5e4d3c2b-1a09-4f8e-8d7c-6b5a49382716");

    [Fact]
    public async Task The_list_reports_the_booking_and_its_bills()
    {
        await using var dbContext = CreateDbContext();
        var seeded = await SeedAsync(dbContext);
        await DraftAsync(dbContext, seeded, "BL-2026-0001", [seeded.FirstContainerId]);

        var response = await new GetBillsOfLadingQuery(dbContext)
            .GetAsync(seeded.CargoBookingId, CancellationToken.None);

        Assert.Equal(seeded.CargoBookingId, response.CargoBookingId);
        Assert.Equal("BK-2026-0001", response.BookingNumber);
        Assert.Equal(nameof(CargoStatus.Loaded), response.BookingStatus);

        var item = Assert.Single(response.BillsOfLading);

        Assert.Equal("BL-2026-0001", item.BillNumber);
        Assert.Equal(nameof(BillOfLadingStatus.Draft), item.Status);
        Assert.Equal("Adriatic Trade", item.ShipperName);
        Assert.Equal("Nordic Imports", item.ConsigneeName);
        Assert.Equal(1, item.ContainerCount);
        Assert.Null(item.IssuedAtUtc);
    }

    [Fact]
    public async Task The_list_orders_bills_deterministically()
    {
        await using var dbContext = CreateDbContext();
        var seeded = await SeedAsync(dbContext);
        await DraftAsync(dbContext, seeded, "BL-2026-0003", null);
        await DraftAsync(dbContext, seeded, "BL-2026-0001", null);
        await DraftAsync(dbContext, seeded, "BL-2026-0002", null);

        var response = await new GetBillsOfLadingQuery(dbContext)
            .GetAsync(seeded.CargoBookingId, CancellationToken.None);

        Assert.Equal(
            ["BL-2026-0001", "BL-2026-0002", "BL-2026-0003"],
            response.BillsOfLading.Select(bill => bill.BillNumber));
    }

    [Fact]
    public async Task The_list_ignores_bills_of_another_booking()
    {
        await using var dbContext = CreateDbContext();
        var seeded = await SeedAsync(dbContext);
        var other = await SeedAsync(dbContext, "BK-2026-0002", "TCLU2222222");
        await DraftAsync(dbContext, seeded, "BL-2026-0001", null);
        await DraftAsync(dbContext, other, "BL-2026-0002", null);

        var response = await new GetBillsOfLadingQuery(dbContext)
            .GetAsync(seeded.CargoBookingId, CancellationToken.None);

        Assert.Equal("BL-2026-0001", Assert.Single(response.BillsOfLading).BillNumber);
    }

    [Fact]
    public async Task The_list_rejects_an_unknown_booking()
    {
        await using var dbContext = CreateDbContext();

        await Assert.ThrowsAsync<CargoBookingNotFoundException>(
            () => new GetBillsOfLadingQuery(dbContext)
                .GetAsync(Guid.NewGuid(), CancellationToken.None));
    }

    [Fact]
    public async Task The_detail_reports_a_draft_with_its_selected_containers()
    {
        await using var dbContext = CreateDbContext();
        var seeded = await SeedAsync(dbContext);
        var draft = await DraftAsync(
            dbContext,
            seeded,
            "BL-2026-0001",
            [seeded.SecondContainerId, seeded.FirstContainerId]);

        var response = await new GetBillOfLadingDetailsQuery(dbContext)
            .GetAsync(draft.BillOfLadingId, CancellationToken.None);

        Assert.Equal(nameof(BillOfLadingStatus.Draft), response.BillOfLading.Status);
        Assert.True(response.BillOfLading.IsEditable);
        Assert.True(response.BillOfLading.CanIssue);
        Assert.Null(response.BillOfLading.IssueBlockedReason);
        Assert.Equal(
            ["MSKU1111111", "TCLU2222222"],
            response.BillOfLading.Containers.Select(container => container.ContainerNumber));
        Assert.All(
            response.BillOfLading.Containers,
            container => Assert.False(container.IsSnapshotted));
    }

    [Fact]
    public async Task The_detail_reports_an_issued_bill_from_its_snapshot()
    {
        await using var dbContext = CreateDbContext();
        var seeded = await SeedAsync(dbContext);
        var draft = await DraftAsync(
            dbContext,
            seeded,
            "BL-2026-0001",
            [seeded.FirstContainerId]);

        await IssueAsync(dbContext, draft.BillOfLadingId);

        var response = await new GetBillOfLadingDetailsQuery(dbContext)
            .GetAsync(draft.BillOfLadingId, CancellationToken.None);

        Assert.Equal(nameof(BillOfLadingStatus.Issued), response.BillOfLading.Status);
        Assert.False(response.BillOfLading.IsEditable);
        Assert.Equal("BK-2026-0001", response.BillOfLading.SnapshotBookingNumber);
        Assert.True(Assert.Single(response.BillOfLading.Containers).IsSnapshotted);
    }

    [Fact]
    public async Task An_issued_detail_does_not_follow_the_booking_after_issue()
    {
        await using var dbContext = CreateDbContext();
        var seeded = await SeedAsync(dbContext);
        var draft = await DraftAsync(
            dbContext,
            seeded,
            "BL-2026-0001",
            [seeded.FirstContainerId]);

        await IssueAsync(dbContext, draft.BillOfLadingId);

        var booking = await dbContext.Set<CargoBooking>()
            .SingleAsync(candidate => candidate.Id == seeded.CargoBookingId);
        booking.AdvanceStatusTo(CargoStatus.InTransit);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var response = await new GetBillOfLadingDetailsQuery(dbContext)
            .GetAsync(draft.BillOfLadingId, CancellationToken.None);

        Assert.Equal(nameof(CargoStatus.Loaded), response.BillOfLading.SnapshotBookingStatus);
        Assert.Equal("MSKU1111111", Assert.Single(response.BillOfLading.Containers).ContainerNumber);
    }

    [Fact]
    public async Task The_detail_returns_the_history_in_order()
    {
        await using var dbContext = CreateDbContext();
        var seeded = await SeedAsync(dbContext);
        var draft = await DraftAsync(
            dbContext,
            seeded,
            "BL-2026-0001",
            [seeded.FirstContainerId]);

        await IssueAsync(dbContext, draft.BillOfLadingId);
        await VoidAsync(dbContext, draft.BillOfLadingId);

        var response = await new GetBillOfLadingDetailsQuery(dbContext)
            .GetAsync(draft.BillOfLadingId, CancellationToken.None);

        Assert.Equal(
            [
                nameof(BillOfLadingAction.Drafted),
                nameof(BillOfLadingAction.Issued),
                nameof(BillOfLadingAction.Voided),
            ],
            response.History.Select(entry => entry.Action));
        Assert.Equal("Wrong consignee", response.History[2].Notes);
    }

    [Fact]
    public async Task The_detail_ignores_the_history_of_another_bill()
    {
        await using var dbContext = CreateDbContext();
        var seeded = await SeedAsync(dbContext);
        var first = await DraftAsync(dbContext, seeded, "BL-2026-0001", null);
        await DraftAsync(dbContext, seeded, "BL-2026-0002", null);

        var response = await new GetBillOfLadingDetailsQuery(dbContext)
            .GetAsync(first.BillOfLadingId, CancellationToken.None);

        Assert.Single(response.History);
    }

    [Fact]
    public async Task The_detail_rejects_an_unknown_bill()
    {
        await using var dbContext = CreateDbContext();

        await Assert.ThrowsAsync<BillOfLadingNotFoundException>(
            () => new GetBillOfLadingDetailsQuery(dbContext)
                .GetAsync(Guid.NewGuid(), CancellationToken.None));
    }

    [Fact]
    public async Task The_print_model_marks_a_draft_with_a_watermark()
    {
        await using var dbContext = CreateDbContext();
        var seeded = await SeedAsync(dbContext);
        var draft = await DraftAsync(
            dbContext,
            seeded,
            "BL-2026-0001",
            [seeded.FirstContainerId]);

        var model = await new GetBillOfLadingPrintQuery(dbContext)
            .GetAsync(draft.BillOfLadingId, CancellationToken.None);

        Assert.Equal(nameof(BillOfLadingStatus.Draft), model.Watermark);
        Assert.Equal("BL-2026-0001", model.BillNumber);
        Assert.Equal("Adriatic Trade", model.ShipperName);
        Assert.Null(model.BookingNumber);
    }

    [Fact]
    public async Task The_print_model_of_an_issued_bill_carries_no_watermark()
    {
        await using var dbContext = CreateDbContext();
        var seeded = await SeedAsync(dbContext);
        var draft = await DraftAsync(
            dbContext,
            seeded,
            "BL-2026-0001",
            [seeded.FirstContainerId]);

        await IssueAsync(dbContext, draft.BillOfLadingId);

        var model = await new GetBillOfLadingPrintQuery(dbContext)
            .GetAsync(draft.BillOfLadingId, CancellationToken.None);

        Assert.Null(model.Watermark);
        Assert.Equal("BK-2026-0001", model.BookingNumber);
        Assert.Equal(ActorUserId, model.IssuedByUserId);
        Assert.Equal("MSKU1111111", Assert.Single(model.Containers).ContainerNumber);
    }

    [Fact]
    public async Task The_print_model_marks_a_voided_bill_with_a_watermark()
    {
        await using var dbContext = CreateDbContext();
        var seeded = await SeedAsync(dbContext);
        var draft = await DraftAsync(
            dbContext,
            seeded,
            "BL-2026-0001",
            [seeded.FirstContainerId]);

        await IssueAsync(dbContext, draft.BillOfLadingId);
        await VoidAsync(dbContext, draft.BillOfLadingId);

        var model = await new GetBillOfLadingPrintQuery(dbContext)
            .GetAsync(draft.BillOfLadingId, CancellationToken.None);

        Assert.Equal(nameof(BillOfLadingStatus.Voided), model.Watermark);
        Assert.Equal("Wrong consignee", model.VoidReason);
        Assert.Equal("BK-2026-0001", model.BookingNumber);
    }

    [Fact]
    public async Task The_print_model_rejects_an_unknown_bill()
    {
        await using var dbContext = CreateDbContext();

        await Assert.ThrowsAsync<BillOfLadingNotFoundException>(
            () => new GetBillOfLadingPrintQuery(dbContext)
                .GetAsync(Guid.NewGuid(), CancellationToken.None));
    }

    private static async Task<BillOfLadingDetails> DraftAsync(
        HarborOpsDbContext dbContext,
        SeededBooking seeded,
        string billNumber,
        IReadOnlyList<Guid>? containerIds)
    {
        return await new CreateBillOfLadingDraftService(
            dbContext,
            new FixedTimeProvider(FixedUtcNow)).CreateAsync(
            seeded.CargoBookingId,
            ActorUserId,
            Request(billNumber, containerIds),
            CancellationToken.None);
    }

    private static async Task IssueAsync(HarborOpsDbContext dbContext, Guid billOfLadingId)
    {
        await new IssueBillOfLadingService(
            dbContext,
            new FixedTimeProvider(FixedUtcNow.AddHours(1))).IssueAsync(
            billOfLadingId,
            ActorUserId,
            CancellationToken.None);
    }

    private static async Task VoidAsync(HarborOpsDbContext dbContext, Guid billOfLadingId)
    {
        await new VoidBillOfLadingService(
            dbContext,
            new FixedTimeProvider(FixedUtcNow.AddHours(2))).VoidAsync(
            billOfLadingId,
            ActorUserId,
            new VoidBillOfLadingRequest { Reason = "Wrong consignee" },
            CancellationToken.None);
    }

    private static async Task<SeededBooking> SeedAsync(
        HarborOpsDbContext dbContext,
        string bookingNumber = "BK-2026-0001",
        string firstContainerNumber = "MSKU1111111")
    {
        var booking = CargoBooking.Register(
            bookingNumber,
            Guid.NewGuid(),
            "Machine parts",
            "General cargo",
            12400m,
            480,
            false);

        while (booking.Status != CargoStatus.Loaded)
        {
            booking.AdvanceStatusTo(booking.NextStatus!.Value);
        }

        await dbContext.Set<CargoBooking>().AddAsync(booking, CancellationToken.None);

        var first = Container.Register(
            firstContainerNumber,
            "40HC",
            CargoStatus.Registered,
            cargoBookingId: booking.Id);
        var second = Container.Register(
            "TCLU2222222",
            "20GP",
            CargoStatus.Registered,
            cargoBookingId: booking.Id);

        await dbContext.Set<Container>().AddRangeAsync([first, second], CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        return new SeededBooking(booking.Id, first.Id, second.Id);
    }

    private static SaveBillOfLadingDraftRequest Request(
        string billNumber,
        IReadOnlyList<Guid>? containerIds)
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
            RequestedIssueDate = new DateOnly(2026, 9, 9),
            ContainerIds = containerIds,
        };
    }

    private static HarborOpsDbContext CreateDbContext()
    {
        var builder = new DbContextOptionsBuilder<HarborOpsDbContext>()
            .UseInMemoryDatabase($"HarborOpsUnitTests-{Guid.NewGuid()}");

        return new HarborOpsDbContext(builder.Options);
    }

    private sealed record SeededBooking(
        Guid CargoBookingId,
        Guid FirstContainerId,
        Guid SecondContainerId);

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow()
        {
            return utcNow;
        }
    }
}
