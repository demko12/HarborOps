using HarborOps.Infrastructure.Persistence;
using HarborOps.Modules.Cargo.Application;
using HarborOps.Modules.Cargo.Contracts;
using HarborOps.Modules.Cargo.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace HarborOps.UnitTests.Cargo;

public sealed class ReviewCargoSafetyInfoServiceTests
{
    private static readonly DateTimeOffset FixedNow =
        new(2026, 9, 1, 14, 30, 0, TimeSpan.Zero);

    private static readonly Guid OfficerUserId =
        Guid.Parse("1a2b3c4d-5e6f-4a7b-8c9d-0e1f2a3b4c5d");

    private static readonly Guid ReviewerUserId =
        Guid.Parse("9f8e7d6c-5b4a-4938-8271-6a5b4c3d2e1f");

    private static readonly Guid VoyageId = Guid.Parse("5d4c3b2a-1e0f-4a9b-8c7d-6e5f4a3b2c1d");

    [Fact]
    public async Task ReviewAsync_approves_a_submitted_declaration()
    {
        await using var dbContext = CreateDbContext();
        var booking = await AddHazardousBookingAsync(dbContext, "BK-REVIEW-0001");
        await AddDeclarationAsync(dbContext, booking.Id, CargoSafetyReviewStatus.Submitted);
        var service = CreateService(dbContext);

        var response = await service.ReviewAsync(
            booking.Id,
            ReviewerUserId,
            CreateRequest(CargoSafetyReviewStatus.Approved),
            CancellationToken.None);

        var declaration = await dbContext.Set<CargoSafetyInfo>().SingleAsync(CancellationToken.None);

        Assert.Equal(nameof(CargoSafetyReviewStatus.Approved), response.ReviewStatus);
        Assert.Equal(ReviewerUserId, response.ReviewedByUserId);
        Assert.Equal(FixedNow.UtcDateTime, response.ReviewedAtUtc);
        Assert.Equal(CargoSafetyReviewStatus.Approved, declaration.ReviewStatus);
    }

    [Fact]
    public async Task ReviewAsync_rejects_a_submitted_declaration_with_the_reason()
    {
        await using var dbContext = CreateDbContext();
        var booking = await AddHazardousBookingAsync(dbContext, "BK-REVIEW-0002");
        await AddDeclarationAsync(dbContext, booking.Id, CargoSafetyReviewStatus.Submitted);
        var service = CreateService(dbContext);

        var response = await service.ReviewAsync(
            booking.Id,
            ReviewerUserId,
            CreateRequest(CargoSafetyReviewStatus.Rejected, "  Storage conditions are unclear.  "),
            CancellationToken.None);

        Assert.Equal(nameof(CargoSafetyReviewStatus.Rejected), response.ReviewStatus);
        Assert.Equal("Storage conditions are unclear.", response.ReviewNotes);
    }

    [Fact]
    public async Task ReviewAsync_writes_the_decision_into_the_history()
    {
        await using var dbContext = CreateDbContext();
        var booking = await AddHazardousBookingAsync(dbContext, "BK-REVIEW-0003");
        var declaration = await AddDeclarationAsync(
            dbContext,
            booking.Id,
            CargoSafetyReviewStatus.Submitted);
        var service = CreateService(dbContext);

        await service.ReviewAsync(
            booking.Id,
            ReviewerUserId,
            CreateRequest(CargoSafetyReviewStatus.Rejected, "Storage conditions are unclear."),
            CancellationToken.None);

        var entries = await dbContext
            .Set<CargoSafetyReviewEntry>()
            .OrderBy(entry => entry.OccurredUtc)
            .ToListAsync(CancellationToken.None);

        Assert.Equal(2, entries.Count);
        Assert.Equal(declaration.Id, entries[1].CargoSafetyInfoId);
        Assert.Equal(CargoSafetyReviewStatus.Submitted, entries[1].FromStatus);
        Assert.Equal(CargoSafetyReviewStatus.Rejected, entries[1].ToStatus);
        Assert.Equal(ReviewerUserId, entries[1].ActorUserId);
        Assert.Equal(FixedNow.UtcDateTime, entries[1].OccurredUtc);
        Assert.Equal("Storage conditions are unclear.", entries[1].Notes);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ReviewAsync_requires_notes_when_the_declaration_is_rejected(string? notes)
    {
        await using var dbContext = CreateDbContext();
        var booking = await AddHazardousBookingAsync(dbContext, "BK-REVIEW-0004");
        await AddDeclarationAsync(dbContext, booking.Id, CargoSafetyReviewStatus.Submitted);
        var service = CreateService(dbContext);

        var exception = await Assert.ThrowsAsync<CargoSafetyInfoValidationException>(
            () => service.ReviewAsync(
                booking.Id,
                ReviewerUserId,
                CreateRequest(CargoSafetyReviewStatus.Rejected, notes),
                CancellationToken.None));

        var declaration = await dbContext.Set<CargoSafetyInfo>().SingleAsync(CancellationToken.None);

        Assert.Contains(CargoSafetyInfoValidator.NotesField, exception.Errors.Keys);
        Assert.Equal(CargoSafetyReviewStatus.Submitted, declaration.ReviewStatus);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Draft")]
    [InlineData("Submitted")]
    [InlineData("Cancelled")]
    public async Task ReviewAsync_accepts_only_an_approved_or_rejected_decision(string? status)
    {
        await using var dbContext = CreateDbContext();
        var booking = await AddHazardousBookingAsync(dbContext, "BK-REVIEW-0005");
        await AddDeclarationAsync(dbContext, booking.Id, CargoSafetyReviewStatus.Submitted);
        var service = CreateService(dbContext);

        var exception = await Assert.ThrowsAsync<CargoSafetyInfoValidationException>(
            () => service.ReviewAsync(
                booking.Id,
                ReviewerUserId,
                new ReviewCargoSafetyInfoRequest { Status = status, Notes = "Looks fine." },
                CancellationToken.None));

        Assert.Contains(CargoSafetyInfoValidator.StatusField, exception.Errors.Keys);
    }

    [Fact]
    public async Task ReviewAsync_rejects_review_notes_that_are_too_long()
    {
        await using var dbContext = CreateDbContext();
        var booking = await AddHazardousBookingAsync(dbContext, "BK-REVIEW-0006");
        await AddDeclarationAsync(dbContext, booking.Id, CargoSafetyReviewStatus.Submitted);
        var service = CreateService(dbContext);

        var exception = await Assert.ThrowsAsync<CargoSafetyInfoValidationException>(
            () => service.ReviewAsync(
                booking.Id,
                ReviewerUserId,
                CreateRequest(CargoSafetyReviewStatus.Approved, new string('x', 1001)),
                CancellationToken.None));

        Assert.Contains(CargoSafetyInfoValidator.NotesField, exception.Errors.Keys);
    }

    [Theory]
    [InlineData(nameof(CargoSafetyReviewStatus.Draft))]
    [InlineData(nameof(CargoSafetyReviewStatus.Approved))]
    [InlineData(nameof(CargoSafetyReviewStatus.Rejected))]
    public async Task ReviewAsync_refuses_a_declaration_that_is_not_awaiting_review(
        string reviewStatusName)
    {
        var reviewStatus = Enum.Parse<CargoSafetyReviewStatus>(reviewStatusName);

        await using var dbContext = CreateDbContext();
        var booking = await AddHazardousBookingAsync(dbContext, "BK-REVIEW-0007");
        var declaration = await AddDeclarationAsync(dbContext, booking.Id, reviewStatus);
        var historyCount = await dbContext.Set<CargoSafetyReviewEntry>().CountAsync(CancellationToken.None);
        var service = CreateService(dbContext);

        var exception = await Assert.ThrowsAsync<CargoSafetyInfoTransitionException>(
            () => service.ReviewAsync(
                booking.Id,
                ReviewerUserId,
                CreateRequest(CargoSafetyReviewStatus.Approved),
                CancellationToken.None));

        Assert.Equal(declaration.Id, exception.CargoSafetyInfoId);
        Assert.Equal(reviewStatus, exception.CurrentStatus);
        Assert.Equal(
            historyCount,
            await dbContext.Set<CargoSafetyReviewEntry>().CountAsync(CancellationToken.None));
    }

    [Fact]
    public async Task ReviewAsync_rejects_a_booking_without_a_declaration()
    {
        await using var dbContext = CreateDbContext();
        var booking = await AddHazardousBookingAsync(dbContext, "BK-REVIEW-0008");
        var service = CreateService(dbContext);

        var exception = await Assert.ThrowsAsync<CargoSafetyInfoNotFoundException>(
            () => service.ReviewAsync(
                booking.Id,
                ReviewerUserId,
                CreateRequest(CargoSafetyReviewStatus.Approved),
                CancellationToken.None));

        Assert.Equal(booking.Id, exception.CargoBookingId);
    }

    [Fact]
    public async Task ReviewAsync_rejects_an_unknown_booking()
    {
        await using var dbContext = CreateDbContext();
        var unknownBookingId = Guid.NewGuid();
        var service = CreateService(dbContext);

        await Assert.ThrowsAsync<CargoBookingNotFoundException>(
            () => service.ReviewAsync(
                unknownBookingId,
                ReviewerUserId,
                CreateRequest(CargoSafetyReviewStatus.Approved),
                CancellationToken.None));
    }

    [Fact]
    public async Task ReviewAsync_rejects_a_booking_that_is_not_hazardous()
    {
        await using var dbContext = CreateDbContext();
        var booking = await AddBookingAsync(dbContext, "BK-REVIEW-0009", isHazardous: false);
        var service = CreateService(dbContext);

        await Assert.ThrowsAsync<CargoBookingNotHazardousException>(
            () => service.ReviewAsync(
                booking.Id,
                ReviewerUserId,
                CreateRequest(CargoSafetyReviewStatus.Approved),
                CancellationToken.None));
    }

    [Fact]
    public async Task ReviewAsync_leaves_the_decision_and_the_history_untouched_when_the_save_fails()
    {
        var databaseName = $"HarborOpsUnitTests-{Guid.NewGuid()}";
        Guid bookingId;

        await using (var seedContext = CreateDbContext(databaseName))
        {
            var booking = await AddHazardousBookingAsync(seedContext, "BK-REVIEW-0010");
            await AddDeclarationAsync(seedContext, booking.Id, CargoSafetyReviewStatus.Submitted);
            bookingId = booking.Id;
        }

        await using (var failingContext = CreateDbContext(databaseName, failOnSave: true))
        {
            var service = CreateService(failingContext);

            await Assert.ThrowsAsync<DbUpdateException>(
                () => service.ReviewAsync(
                    bookingId,
                    ReviewerUserId,
                    CreateRequest(CargoSafetyReviewStatus.Approved),
                    CancellationToken.None));
        }

        await using var assertContext = CreateDbContext(databaseName);
        var declaration = await assertContext
            .Set<CargoSafetyInfo>()
            .SingleAsync(CancellationToken.None);

        Assert.Equal(CargoSafetyReviewStatus.Submitted, declaration.ReviewStatus);
        Assert.Null(declaration.ReviewedByUserId);
        Assert.Single(await assertContext.Set<CargoSafetyReviewEntry>().ToListAsync(CancellationToken.None));
    }

    private static ReviewCargoSafetyInfoRequest CreateRequest(
        CargoSafetyReviewStatus decision,
        string? notes = null)
    {
        return new ReviewCargoSafetyInfoRequest
        {
            Status = decision.ToString(),
            Notes = notes,
        };
    }

    private static ReviewCargoSafetyInfoService CreateService(HarborOpsDbContext dbContext)
    {
        return new ReviewCargoSafetyInfoService(dbContext, new FixedTimeProvider(FixedNow));
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

        if (reviewStatus is CargoSafetyReviewStatus.Rejected)
        {
            var rejected = declaration.Reject(
                ReviewerUserId,
                FixedNow.UtcDateTime.AddHours(-1),
                "Storage conditions are unclear.");
            await dbContext.Set<CargoSafetyReviewEntry>().AddAsync(rejected, CancellationToken.None);
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
