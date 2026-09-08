using HarborOps.Infrastructure.Persistence;
using HarborOps.Modules.Cargo.Application;
using HarborOps.Modules.Cargo.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace HarborOps.UnitTests.Cargo;

public sealed class SubmitCargoSafetyInfoServiceTests
{
    private static readonly DateTimeOffset FixedNow =
        new(2026, 9, 1, 10, 0, 0, TimeSpan.Zero);

    private static readonly Guid OfficerUserId =
        Guid.Parse("1a2b3c4d-5e6f-4a7b-8c9d-0e1f2a3b4c5d");

    private static readonly Guid ReviewerUserId =
        Guid.Parse("9f8e7d6c-5b4a-4938-8271-6a5b4c3d2e1f");

    private static readonly Guid VoyageId = Guid.Parse("5d4c3b2a-1e0f-4a9b-8c7d-6e5f4a3b2c1d");

    [Fact]
    public async Task SubmitAsync_moves_a_draft_into_review_and_records_the_actor_and_time()
    {
        await using var dbContext = CreateDbContext();
        var booking = await AddHazardousBookingAsync(dbContext, "BK-SUBMIT-0001");
        await AddDeclarationAsync(dbContext, booking.Id);
        var service = CreateService(dbContext);

        var response = await service.SubmitAsync(booking.Id, OfficerUserId, CancellationToken.None);

        var declaration = await dbContext.Set<CargoSafetyInfo>().SingleAsync(CancellationToken.None);

        Assert.Equal(nameof(CargoSafetyReviewStatus.Submitted), response.ReviewStatus);
        Assert.Equal(OfficerUserId, response.SubmittedByUserId);
        Assert.Equal(FixedNow.UtcDateTime, response.SubmittedAtUtc);
        Assert.Equal(CargoSafetyReviewStatus.Submitted, declaration.ReviewStatus);
        Assert.Equal(OfficerUserId, declaration.SubmittedByUserId);
        Assert.Equal(FixedNow.UtcDateTime, declaration.SubmittedAtUtc);
    }

    [Fact]
    public async Task SubmitAsync_writes_a_single_history_entry_for_the_transition()
    {
        await using var dbContext = CreateDbContext();
        var booking = await AddHazardousBookingAsync(dbContext, "BK-SUBMIT-0002");
        var declaration = await AddDeclarationAsync(dbContext, booking.Id);
        var service = CreateService(dbContext);

        await service.SubmitAsync(booking.Id, OfficerUserId, CancellationToken.None);

        var entry = await dbContext
            .Set<CargoSafetyReviewEntry>()
            .SingleAsync(CancellationToken.None);

        Assert.Equal(declaration.Id, entry.CargoSafetyInfoId);
        Assert.Equal(CargoSafetyReviewStatus.Draft, entry.FromStatus);
        Assert.Equal(CargoSafetyReviewStatus.Submitted, entry.ToStatus);
        Assert.Equal(OfficerUserId, entry.ActorUserId);
        Assert.Equal(FixedNow.UtcDateTime, entry.OccurredUtc);
        Assert.Null(entry.Notes);
    }

    [Fact]
    public async Task SubmitAsync_sends_a_rejected_declaration_back_into_review()
    {
        await using var dbContext = CreateDbContext();
        var booking = await AddHazardousBookingAsync(dbContext, "BK-SUBMIT-0003");
        var declaration = await AddDeclarationAsync(dbContext, booking.Id);
        declaration.Submit(OfficerUserId, FixedNow.UtcDateTime.AddHours(-2));
        declaration.Reject(ReviewerUserId, FixedNow.UtcDateTime.AddHours(-1), "Storage conditions are unclear.");
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var service = CreateService(dbContext);

        var response = await service.SubmitAsync(booking.Id, OfficerUserId, CancellationToken.None);

        Assert.Equal(nameof(CargoSafetyReviewStatus.Submitted), response.ReviewStatus);
        Assert.Null(response.ReviewedByUserId);
        Assert.Null(response.ReviewedAtUtc);
        Assert.Null(response.ReviewNotes);
    }

    [Theory]
    [InlineData(nameof(CargoSafetyReviewStatus.Submitted))]
    [InlineData(nameof(CargoSafetyReviewStatus.Approved))]
    public async Task SubmitAsync_refuses_a_declaration_that_is_no_longer_editable(
        string reviewStatusName)
    {
        var reviewStatus = Enum.Parse<CargoSafetyReviewStatus>(reviewStatusName);

        await using var dbContext = CreateDbContext();
        var booking = await AddHazardousBookingAsync(dbContext, "BK-SUBMIT-0004");
        var declaration = await AddDeclarationAsync(dbContext, booking.Id, reviewStatus);
        var historyCount = await dbContext.Set<CargoSafetyReviewEntry>().CountAsync(CancellationToken.None);
        var service = CreateService(dbContext);

        var exception = await Assert.ThrowsAsync<CargoSafetyInfoTransitionException>(
            () => service.SubmitAsync(booking.Id, OfficerUserId, CancellationToken.None));

        Assert.Equal(declaration.Id, exception.CargoSafetyInfoId);
        Assert.Equal(reviewStatus, exception.CurrentStatus);
        Assert.Equal(
            historyCount,
            await dbContext.Set<CargoSafetyReviewEntry>().CountAsync(CancellationToken.None));
    }

    [Fact]
    public async Task SubmitAsync_rejects_a_booking_without_a_declaration()
    {
        await using var dbContext = CreateDbContext();
        var booking = await AddHazardousBookingAsync(dbContext, "BK-SUBMIT-0005");
        var service = CreateService(dbContext);

        var exception = await Assert.ThrowsAsync<CargoSafetyInfoNotFoundException>(
            () => service.SubmitAsync(booking.Id, OfficerUserId, CancellationToken.None));

        Assert.Equal(booking.Id, exception.CargoBookingId);
    }

    [Fact]
    public async Task SubmitAsync_rejects_an_unknown_booking()
    {
        await using var dbContext = CreateDbContext();
        var unknownBookingId = Guid.NewGuid();
        var service = CreateService(dbContext);

        var exception = await Assert.ThrowsAsync<CargoBookingNotFoundException>(
            () => service.SubmitAsync(unknownBookingId, OfficerUserId, CancellationToken.None));

        Assert.Equal(unknownBookingId, exception.CargoBookingId);
    }

    [Fact]
    public async Task SubmitAsync_rejects_a_booking_that_is_not_hazardous()
    {
        await using var dbContext = CreateDbContext();
        var booking = await AddBookingAsync(dbContext, "BK-SUBMIT-0006", isHazardous: false);
        var service = CreateService(dbContext);

        await Assert.ThrowsAsync<CargoBookingNotHazardousException>(
            () => service.SubmitAsync(booking.Id, OfficerUserId, CancellationToken.None));
    }

    [Fact]
    public async Task SubmitAsync_leaves_the_status_and_the_history_untouched_when_the_save_fails()
    {
        var databaseName = $"HarborOpsUnitTests-{Guid.NewGuid()}";
        Guid bookingId;

        await using (var seedContext = CreateDbContext(databaseName))
        {
            var booking = await AddHazardousBookingAsync(seedContext, "BK-SUBMIT-0007");
            await AddDeclarationAsync(seedContext, booking.Id);
            bookingId = booking.Id;
        }

        await using (var failingContext = CreateDbContext(databaseName, failOnSave: true))
        {
            var service = CreateService(failingContext);

            await Assert.ThrowsAsync<DbUpdateException>(
                () => service.SubmitAsync(bookingId, OfficerUserId, CancellationToken.None));
        }

        await using var assertContext = CreateDbContext(databaseName);
        var declaration = await assertContext
            .Set<CargoSafetyInfo>()
            .SingleAsync(CancellationToken.None);

        Assert.Equal(CargoSafetyReviewStatus.Draft, declaration.ReviewStatus);
        Assert.Null(declaration.SubmittedByUserId);
        Assert.Empty(await assertContext.Set<CargoSafetyReviewEntry>().ToListAsync(CancellationToken.None));
    }

    private static SubmitCargoSafetyInfoService CreateService(HarborOpsDbContext dbContext)
    {
        return new SubmitCargoSafetyInfoService(dbContext, new FixedTimeProvider(FixedNow));
    }

    private static async Task<CargoSafetyInfo> AddDeclarationAsync(
        HarborOpsDbContext dbContext,
        Guid cargoBookingId,
        CargoSafetyReviewStatus reviewStatus = CargoSafetyReviewStatus.Draft)
    {
        var declaration = CargoSafetyInfo.Declare(
            cargoBookingId,
            "3",
            "UN1203",
            "Keep below 25C",
            handlingNotes: null);

        await dbContext.Set<CargoSafetyInfo>().AddAsync(declaration, CancellationToken.None);

        if (reviewStatus is not CargoSafetyReviewStatus.Draft)
        {
            var submitted = declaration.Submit(OfficerUserId, FixedNow.UtcDateTime.AddHours(-2));
            await dbContext.Set<CargoSafetyReviewEntry>().AddAsync(submitted, CancellationToken.None);
        }

        if (reviewStatus is CargoSafetyReviewStatus.Approved)
        {
            var approved = declaration.Approve(
                ReviewerUserId,
                FixedNow.UtcDateTime.AddHours(-1),
                notes: null);
            await dbContext.Set<CargoSafetyReviewEntry>().AddAsync(approved, CancellationToken.None);
        }

        await dbContext.SaveChangesAsync(CancellationToken.None);

        return declaration;
    }

    private static Task<CargoBooking> AddHazardousBookingAsync(
        HarborOpsDbContext dbContext,
        string bookingNumber)
    {
        return AddBookingAsync(dbContext, bookingNumber, isHazardous: true);
    }

    private static async Task<CargoBooking> AddBookingAsync(
        HarborOpsDbContext dbContext,
        string bookingNumber,
        bool isHazardous)
    {
        var booking = CargoBooking.Register(
            bookingNumber,
            VoyageId,
            "Solvent drums",
            "General Cargo",
            5000.000m,
            10,
            isHazardous);

        await dbContext.Set<CargoBooking>().AddAsync(booking, CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        return booking;
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
