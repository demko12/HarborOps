using HarborOps.Infrastructure.Persistence;
using HarborOps.Modules.Cargo.Application;
using HarborOps.Modules.Cargo.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.UnitTests.Cargo;

public sealed class GetCargoSafetyInformationQueryTests
{
    private static readonly DateTime SubmittedAtUtc = new(2026, 9, 1, 10, 0, 0, DateTimeKind.Utc);

    private static readonly DateTime ReviewedAtUtc = new(2026, 9, 1, 14, 30, 0, DateTimeKind.Utc);

    private static readonly Guid OfficerUserId =
        Guid.Parse("1a2b3c4d-5e6f-4a7b-8c9d-0e1f2a3b4c5d");

    private static readonly Guid ReviewerUserId =
        Guid.Parse("9f8e7d6c-5b4a-4938-8271-6a5b4c3d2e1f");

    private static readonly Guid VoyageId = Guid.Parse("5d4c3b2a-1e0f-4a9b-8c7d-6e5f4a3b2c1d");

    [Fact]
    public async Task GetAsync_returns_the_declaration_with_an_empty_timeline_while_it_is_a_draft()
    {
        await using var dbContext = CreateDbContext();
        var booking = await AddHazardousBookingAsync(dbContext, "BK-SAFETY-GET-0001");
        var declaration = await AddDeclarationAsync(dbContext, booking.Id);
        var query = new GetCargoSafetyInformationQuery(dbContext);

        var response = await query.GetAsync(booking.Id, CancellationToken.None);

        Assert.Equal(declaration.Id, response.Declaration.CargoSafetyInfoId);
        Assert.Equal("3", response.Declaration.ImdgClass);
        Assert.Equal("UN1203", response.Declaration.UnNumber);
        Assert.Equal("Keep below 25C", response.Declaration.StorageConditions);
        Assert.Equal(nameof(CargoSafetyReviewStatus.Draft), response.Declaration.ReviewStatus);
        Assert.Null(response.Declaration.SubmittedByUserId);
        Assert.Empty(response.Timeline);
    }

    [Fact]
    public async Task GetAsync_returns_the_review_fields_of_a_reviewed_declaration()
    {
        await using var dbContext = CreateDbContext();
        var booking = await AddHazardousBookingAsync(dbContext, "BK-SAFETY-GET-0002");
        var declaration = await AddDeclarationAsync(dbContext, booking.Id);
        await SubmitAsync(dbContext, declaration, SubmittedAtUtc);
        await RejectAsync(dbContext, declaration, ReviewedAtUtc, "Storage conditions are unclear.");
        var query = new GetCargoSafetyInformationQuery(dbContext);

        var response = await query.GetAsync(booking.Id, CancellationToken.None);

        Assert.Equal(nameof(CargoSafetyReviewStatus.Rejected), response.Declaration.ReviewStatus);
        Assert.Equal(OfficerUserId, response.Declaration.SubmittedByUserId);
        Assert.Equal(SubmittedAtUtc, response.Declaration.SubmittedAtUtc);
        Assert.Equal(ReviewerUserId, response.Declaration.ReviewedByUserId);
        Assert.Equal(ReviewedAtUtc, response.Declaration.ReviewedAtUtc);
        Assert.Equal("Storage conditions are unclear.", response.Declaration.ReviewNotes);
    }

    [Fact]
    public async Task GetAsync_returns_the_timeline_in_the_order_the_transitions_happened()
    {
        await using var dbContext = CreateDbContext();
        var booking = await AddHazardousBookingAsync(dbContext, "BK-SAFETY-GET-0003");
        var declaration = await AddDeclarationAsync(dbContext, booking.Id);
        await SubmitAsync(dbContext, declaration, SubmittedAtUtc);
        await RejectAsync(dbContext, declaration, ReviewedAtUtc, "Storage conditions are unclear.");
        await SubmitAsync(dbContext, declaration, ReviewedAtUtc.AddHours(1));
        await ApproveAsync(dbContext, declaration, ReviewedAtUtc.AddHours(2));
        var query = new GetCargoSafetyInformationQuery(dbContext);

        var response = await query.GetAsync(booking.Id, CancellationToken.None);

        Assert.Collection(
            response.Timeline,
            entry =>
            {
                Assert.Equal(nameof(CargoSafetyReviewStatus.Draft), entry.FromStatus);
                Assert.Equal(nameof(CargoSafetyReviewStatus.Submitted), entry.ToStatus);
                Assert.Equal(OfficerUserId, entry.ActorUserId);
                Assert.Equal(SubmittedAtUtc, entry.OccurredUtc);
                Assert.Null(entry.Notes);
            },
            entry =>
            {
                Assert.Equal(nameof(CargoSafetyReviewStatus.Submitted), entry.FromStatus);
                Assert.Equal(nameof(CargoSafetyReviewStatus.Rejected), entry.ToStatus);
                Assert.Equal(ReviewerUserId, entry.ActorUserId);
                Assert.Equal("Storage conditions are unclear.", entry.Notes);
            },
            entry =>
            {
                Assert.Equal(nameof(CargoSafetyReviewStatus.Rejected), entry.FromStatus);
                Assert.Equal(nameof(CargoSafetyReviewStatus.Submitted), entry.ToStatus);
            },
            entry =>
            {
                Assert.Equal(nameof(CargoSafetyReviewStatus.Submitted), entry.FromStatus);
                Assert.Equal(nameof(CargoSafetyReviewStatus.Approved), entry.ToStatus);
            });
    }

    [Fact]
    public async Task GetAsync_returns_only_the_timeline_of_the_requested_booking()
    {
        await using var dbContext = CreateDbContext();
        var booking = await AddHazardousBookingAsync(dbContext, "BK-SAFETY-GET-0004");
        var otherBooking = await AddHazardousBookingAsync(dbContext, "BK-SAFETY-GET-0005");
        var declaration = await AddDeclarationAsync(dbContext, booking.Id);
        var otherDeclaration = await AddDeclarationAsync(dbContext, otherBooking.Id);
        await SubmitAsync(dbContext, declaration, SubmittedAtUtc);
        await SubmitAsync(dbContext, otherDeclaration, SubmittedAtUtc);
        var query = new GetCargoSafetyInformationQuery(dbContext);

        var response = await query.GetAsync(booking.Id, CancellationToken.None);

        Assert.Single(response.Timeline);
        Assert.Equal(declaration.Id, response.Declaration.CargoSafetyInfoId);
    }

    [Fact]
    public async Task GetAsync_rejects_a_booking_without_a_declaration()
    {
        await using var dbContext = CreateDbContext();
        var booking = await AddHazardousBookingAsync(dbContext, "BK-SAFETY-GET-0006");
        var query = new GetCargoSafetyInformationQuery(dbContext);

        var exception = await Assert.ThrowsAsync<CargoSafetyInfoNotFoundException>(
            () => query.GetAsync(booking.Id, CancellationToken.None));

        Assert.Equal(booking.Id, exception.CargoBookingId);
    }

    [Fact]
    public async Task GetAsync_rejects_an_unknown_booking()
    {
        await using var dbContext = CreateDbContext();
        var unknownBookingId = Guid.NewGuid();
        var query = new GetCargoSafetyInformationQuery(dbContext);

        var exception = await Assert.ThrowsAsync<CargoBookingNotFoundException>(
            () => query.GetAsync(unknownBookingId, CancellationToken.None));

        Assert.Equal(unknownBookingId, exception.CargoBookingId);
    }

    [Fact]
    public async Task GetAsync_rejects_a_booking_that_is_not_hazardous()
    {
        await using var dbContext = CreateDbContext();
        var booking = await AddBookingAsync(dbContext, "BK-SAFETY-GET-0007", isHazardous: false);
        var query = new GetCargoSafetyInformationQuery(dbContext);

        var exception = await Assert.ThrowsAsync<CargoBookingNotHazardousException>(
            () => query.GetAsync(booking.Id, CancellationToken.None));

        Assert.Equal(booking.Id, exception.CargoBookingId);
    }

    private static async Task SubmitAsync(
        HarborOpsDbContext dbContext,
        CargoSafetyInfo declaration,
        DateTime occurredUtc)
    {
        var entry = declaration.Submit(OfficerUserId, occurredUtc);

        await dbContext.Set<CargoSafetyReviewEntry>().AddAsync(entry, CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);
    }

    private static async Task ApproveAsync(
        HarborOpsDbContext dbContext,
        CargoSafetyInfo declaration,
        DateTime occurredUtc)
    {
        var entry = declaration.Approve(ReviewerUserId, occurredUtc, notes: null);

        await dbContext.Set<CargoSafetyReviewEntry>().AddAsync(entry, CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);
    }

    private static async Task RejectAsync(
        HarborOpsDbContext dbContext,
        CargoSafetyInfo declaration,
        DateTime occurredUtc,
        string notes)
    {
        var entry = declaration.Reject(ReviewerUserId, occurredUtc, notes);

        await dbContext.Set<CargoSafetyReviewEntry>().AddAsync(entry, CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);
    }

    private static async Task<CargoSafetyInfo> AddDeclarationAsync(
        HarborOpsDbContext dbContext,
        Guid cargoBookingId)
    {
        var declaration = CargoSafetyInfo.Declare(
            cargoBookingId,
            "3",
            "UN1203",
            "Keep below 25C",
            handlingNotes: null);

        await dbContext.Set<CargoSafetyInfo>().AddAsync(declaration, CancellationToken.None);
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

    private static HarborOpsDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<HarborOpsDbContext>()
            .UseInMemoryDatabase($"HarborOpsUnitTests-{Guid.NewGuid()}")
            .Options;

        return new HarborOpsDbContext(options);
    }
}
