using HarborOps.Infrastructure.Persistence;
using HarborOps.Modules.Cargo.Application;
using HarborOps.Modules.Cargo.Contracts;
using HarborOps.Modules.Cargo.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace HarborOps.UnitTests.Cargo;

public sealed class VoidBillOfLadingServiceTests
{
    private static readonly DateTimeOffset FixedUtcNow =
        new(2026, 9, 9, 14, 45, 0, TimeSpan.Zero);

    private static readonly Guid ActorUserId =
        Guid.Parse("5e4d3c2b-1a09-4f8e-8d7c-6b5a49382716");

    private static readonly Guid SupervisorUserId =
        Guid.Parse("4d3c2b1a-0f9e-4d8c-8b7a-695847362514");

    [Fact]
    public async Task VoidAsync_records_the_reason_the_actor_and_the_timestamp()
    {
        await using var dbContext = CreateDbContext();
        var context = await SeedIssuedAsync(dbContext);

        var details = await CreateService(dbContext).VoidAsync(
            context.BillOfLadingId,
            SupervisorUserId,
            new VoidBillOfLadingRequest { Reason = "  Wrong consignee  " },
            CancellationToken.None);

        Assert.Equal(nameof(BillOfLadingStatus.Voided), details.Status);
        Assert.Equal("Wrong consignee", details.VoidReason);
        Assert.Equal(SupervisorUserId, details.VoidedByUserId);
        Assert.Equal(FixedUtcNow.UtcDateTime, details.VoidedAtUtc);
        Assert.False(details.IsEditable);
    }

    [Fact]
    public async Task VoidAsync_keeps_the_issued_snapshot()
    {
        await using var dbContext = CreateDbContext();
        var context = await SeedIssuedAsync(dbContext);

        var details = await CreateService(dbContext).VoidAsync(
            context.BillOfLadingId,
            SupervisorUserId,
            Reason(),
            CancellationToken.None);

        Assert.Equal("BK-2026-0001", details.SnapshotBookingNumber);
        Assert.Equal(480, details.SnapshotBookingPackageCount);
        Assert.Equal(nameof(CargoStatus.Loaded), details.SnapshotBookingStatus);
        Assert.Equal(ActorUserId, details.IssuedByUserId);
        Assert.NotNull(details.IssuedAtUtc);
        Assert.Equal("MSKU1111111", Assert.Single(details.Containers).ContainerNumber);
    }

    [Fact]
    public async Task VoidAsync_releases_the_selected_containers()
    {
        await using var dbContext = CreateDbContext();
        var context = await SeedIssuedAsync(dbContext);

        await CreateService(dbContext).VoidAsync(
            context.BillOfLadingId,
            SupervisorUserId,
            Reason(),
            CancellationToken.None);

        var selection = await dbContext.Set<BillOfLadingContainer>()
            .SingleAsync(candidate => candidate.BillOfLadingId == context.BillOfLadingId);

        Assert.False(selection.IsActiveSelection);
        Assert.Equal("MSKU1111111", selection.SnapshotContainerNumber);
    }

    [Fact]
    public async Task A_replacement_bill_can_take_the_containers_of_a_voided_bill()
    {
        await using var dbContext = CreateDbContext();
        var context = await SeedIssuedAsync(dbContext);

        await CreateService(dbContext).VoidAsync(
            context.BillOfLadingId,
            SupervisorUserId,
            Reason(),
            CancellationToken.None);

        var replacement = await new CreateBillOfLadingDraftService(
            dbContext,
            new FixedTimeProvider(FixedUtcNow)).CreateAsync(
            context.CargoBookingId,
            ActorUserId,
            Request([context.ContainerId], "BL-2026-0002"),
            CancellationToken.None);

        Assert.Equal("MSKU1111111", Assert.Single(replacement.Containers).ContainerNumber);
        Assert.Equal("BL-2026-0002", replacement.BillNumber);
    }

    [Fact]
    public async Task VoidAsync_appends_a_history_entry_that_carries_the_reason()
    {
        await using var dbContext = CreateDbContext();
        var context = await SeedIssuedAsync(dbContext);

        await CreateService(dbContext).VoidAsync(
            context.BillOfLadingId,
            SupervisorUserId,
            Reason(),
            CancellationToken.None);

        var entries = await dbContext.Set<BillOfLadingEvent>()
            .Where(entry => entry.BillOfLadingId == context.BillOfLadingId)
            .ToListAsync();

        var voided = Assert.Single(entries, entry => entry.Action == BillOfLadingAction.Voided);

        Assert.Equal(BillOfLadingStatus.Issued, voided.FromStatus);
        Assert.Equal(BillOfLadingStatus.Voided, voided.ToStatus);
        Assert.Equal("Wrong consignee", voided.Notes);
        Assert.Equal(SupervisorUserId, voided.ActorUserId);
    }

    [Fact]
    public async Task The_full_history_survives_the_void()
    {
        await using var dbContext = CreateDbContext();
        var context = await SeedIssuedAsync(dbContext);

        await CreateService(dbContext).VoidAsync(
            context.BillOfLadingId,
            SupervisorUserId,
            Reason(),
            CancellationToken.None);

        var actions = await dbContext.Set<BillOfLadingEvent>()
            .Where(entry => entry.BillOfLadingId == context.BillOfLadingId)
            .Select(entry => entry.Action)
            .ToListAsync();

        Assert.Contains(BillOfLadingAction.Drafted, actions);
        Assert.Contains(BillOfLadingAction.Issued, actions);
        Assert.Contains(BillOfLadingAction.Voided, actions);
    }

    [Fact]
    public async Task VoidAsync_is_rejected_while_the_bill_is_still_a_draft()
    {
        await using var dbContext = CreateDbContext();
        var context = await SeedIssuedAsync(dbContext, issue: false);

        var exception = await Assert.ThrowsAsync<BillOfLadingTransitionException>(
            () => CreateService(dbContext).VoidAsync(
                context.BillOfLadingId,
                SupervisorUserId,
                Reason(),
                CancellationToken.None));

        Assert.Equal(
            BillOfLadingTransitionNotAllowedException.StillDraftReason,
            exception.Reason);
    }

    [Fact]
    public async Task VoidAsync_is_rejected_when_the_bill_is_already_voided()
    {
        await using var dbContext = CreateDbContext();
        var context = await SeedIssuedAsync(dbContext);
        var service = CreateService(dbContext);

        await service.VoidAsync(
            context.BillOfLadingId,
            SupervisorUserId,
            Reason(),
            CancellationToken.None);

        var exception = await Assert.ThrowsAsync<BillOfLadingTransitionException>(
            () => service.VoidAsync(
                context.BillOfLadingId,
                SupervisorUserId,
                Reason(),
                CancellationToken.None));

        Assert.Equal(
            BillOfLadingTransitionNotAllowedException.AlreadyVoidedReason,
            exception.Reason);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task VoidAsync_rejects_a_missing_reason(string? reason)
    {
        await using var dbContext = CreateDbContext();
        var context = await SeedIssuedAsync(dbContext);

        var exception = await Assert.ThrowsAsync<BillOfLadingValidationException>(
            () => CreateService(dbContext).VoidAsync(
                context.BillOfLadingId,
                SupervisorUserId,
                new VoidBillOfLadingRequest { Reason = reason },
                CancellationToken.None));

        Assert.True(exception.Errors.ContainsKey(BillOfLadingValidator.ReasonField));
    }

    [Fact]
    public async Task VoidAsync_rejects_a_reason_that_is_too_long()
    {
        await using var dbContext = CreateDbContext();
        var context = await SeedIssuedAsync(dbContext);

        var exception = await Assert.ThrowsAsync<BillOfLadingValidationException>(
            () => CreateService(dbContext).VoidAsync(
                context.BillOfLadingId,
                SupervisorUserId,
                new VoidBillOfLadingRequest
                {
                    Reason = new string('x', BillOfLading.VoidReasonMaxLength + 1),
                },
                CancellationToken.None));

        Assert.True(exception.Errors.ContainsKey(BillOfLadingValidator.ReasonField));
    }

    [Fact]
    public async Task VoidAsync_rejects_an_unknown_bill()
    {
        await using var dbContext = CreateDbContext();

        await Assert.ThrowsAsync<BillOfLadingNotFoundException>(
            () => CreateService(dbContext).VoidAsync(
                Guid.NewGuid(),
                SupervisorUserId,
                Reason(),
                CancellationToken.None));
    }

    [Fact]
    public async Task VoidAsync_writes_nothing_when_the_save_fails()
    {
        var databaseName = $"HarborOpsUnitTests-{Guid.NewGuid()}";
        SeededBill context;

        await using (var seedContext = CreateDbContext(databaseName))
        {
            context = await SeedIssuedAsync(seedContext);
        }

        await using (var failingContext = CreateDbContext(databaseName, failOnSave: true))
        {
            await Assert.ThrowsAsync<DbUpdateException>(
                () => CreateService(failingContext).VoidAsync(
                    context.BillOfLadingId,
                    SupervisorUserId,
                    Reason(),
                    CancellationToken.None));
        }

        await using var verifyContext = CreateDbContext(databaseName);
        var bill = await verifyContext.Set<BillOfLading>()
            .SingleAsync(candidate => candidate.Id == context.BillOfLadingId);
        var selection = await verifyContext.Set<BillOfLadingContainer>()
            .SingleAsync(candidate => candidate.BillOfLadingId == context.BillOfLadingId);

        Assert.Equal(BillOfLadingStatus.Issued, bill.Status);
        Assert.Null(bill.VoidReason);
        Assert.True(selection.IsActiveSelection);
    }

    private static VoidBillOfLadingService CreateService(HarborOpsDbContext dbContext)
    {
        return new VoidBillOfLadingService(dbContext, new FixedTimeProvider(FixedUtcNow));
    }

    private static VoidBillOfLadingRequest Reason()
    {
        return new VoidBillOfLadingRequest { Reason = "Wrong consignee" };
    }

    private static async Task<SeededBill> SeedIssuedAsync(
        HarborOpsDbContext dbContext,
        bool issue = true)
    {
        var booking = CargoBooking.Register(
            "BK-2026-0001",
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
            Request([container.Id]),
            CancellationToken.None);

        if (issue)
        {
            await new IssueBillOfLadingService(
                dbContext,
                new FixedTimeProvider(FixedUtcNow)).IssueAsync(
                created.BillOfLadingId,
                ActorUserId,
                CancellationToken.None);
        }

        return new SeededBill(created.BillOfLadingId, booking.Id, container.Id);
    }

    private static SaveBillOfLadingDraftRequest Request(
        IReadOnlyList<Guid> containerIds,
        string billNumber = "BL-2026-0001")
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
        Guid ContainerId);

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
