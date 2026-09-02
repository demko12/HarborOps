using HarborOps.Infrastructure.Persistence;
using HarborOps.Modules.Cargo.Application;
using HarborOps.Modules.Cargo.Contracts;
using HarborOps.Modules.Cargo.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace HarborOps.UnitTests.Cargo;

public sealed class UpdateCargoSafetyInfoServiceTests
{
    private static readonly Guid OfficerUserId =
        Guid.Parse("1a2b3c4d-5e6f-4a7b-8c9d-0e1f2a3b4c5d");

    private static readonly Guid ReviewerUserId =
        Guid.Parse("9f8e7d6c-5b4a-4938-8271-6a5b4c3d2e1f");

    private static readonly Guid VoyageId = Guid.Parse("5d4c3b2a-1e0f-4a9b-8c7d-6e5f4a3b2c1d");

    private static readonly DateTime ActedAtUtc = new(2026, 9, 1, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task UpdateAsync_declares_the_safety_information_when_none_exists_yet()
    {
        await using var dbContext = CreateDbContext();
        var booking = await AddHazardousBookingAsync(dbContext, "BK-SAFETY-0001");
        var service = new UpdateCargoSafetyInfoService(dbContext);

        var response = await service.UpdateAsync(
            booking.Id,
            CreateValidRequest(),
            CancellationToken.None);

        var declaration = await dbContext
            .Set<CargoSafetyInfo>()
            .SingleAsync(CancellationToken.None);

        Assert.Equal(declaration.Id, response.CargoSafetyInfoId);
        Assert.Equal(booking.Id, declaration.CargoBookingId);
        Assert.Equal(nameof(CargoSafetyReviewStatus.Draft), response.ReviewStatus);
        Assert.Equal(CargoSafetyReviewStatus.Draft, declaration.ReviewStatus);
    }

    [Fact]
    public async Task UpdateAsync_normalizes_the_imdg_class_and_the_un_number()
    {
        await using var dbContext = CreateDbContext();
        var booking = await AddHazardousBookingAsync(dbContext, "BK-SAFETY-0002");
        var service = new UpdateCargoSafetyInfoService(dbContext);

        var response = await service.UpdateAsync(
            booking.Id,
            new UpdateCargoSafetyInfoRequest
            {
                ImdgClass = " 1.4s ",
                UnNumber = " un 1203 ",
                StorageConditions = "  Keep below 25C  ",
                HandlingNotes = "  Away from heat  ",
            },
            CancellationToken.None);

        Assert.Equal("1.4S", response.ImdgClass);
        Assert.Equal("UN1203", response.UnNumber);
        Assert.Equal("Keep below 25C", response.StorageConditions);
        Assert.Equal("Away from heat", response.HandlingNotes);
    }

    [Fact]
    public async Task UpdateAsync_replaces_the_details_of_an_existing_draft()
    {
        await using var dbContext = CreateDbContext();
        var booking = await AddHazardousBookingAsync(dbContext, "BK-SAFETY-0003");
        await AddDeclarationAsync(dbContext, booking.Id);
        var service = new UpdateCargoSafetyInfoService(dbContext);

        var response = await service.UpdateAsync(
            booking.Id,
            new UpdateCargoSafetyInfoRequest
            {
                ImdgClass = "2.1",
                UnNumber = "UN1950",
                StorageConditions = "Ventilated hold",
                HandlingNotes = null,
            },
            CancellationToken.None);

        var declarations = await dbContext.Set<CargoSafetyInfo>().ToListAsync(CancellationToken.None);

        Assert.Single(declarations);
        Assert.Equal("2.1", response.ImdgClass);
        Assert.Equal("UN1950", declarations[0].UnNumber);
        Assert.Null(declarations[0].HandlingNotes);
    }

    [Fact]
    public async Task UpdateAsync_replaces_the_details_of_a_rejected_declaration()
    {
        await using var dbContext = CreateDbContext();
        var booking = await AddHazardousBookingAsync(dbContext, "BK-SAFETY-0004");
        var declaration = await AddDeclarationAsync(dbContext, booking.Id);
        declaration.Submit(OfficerUserId, ActedAtUtc);
        declaration.Reject(ReviewerUserId, ActedAtUtc, "Storage conditions are unclear.");
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var service = new UpdateCargoSafetyInfoService(dbContext);

        var response = await service.UpdateAsync(
            booking.Id,
            CreateValidRequest(storageConditions: "Ventilated hold"),
            CancellationToken.None);

        Assert.Equal("Ventilated hold", response.StorageConditions);
        Assert.Equal(nameof(CargoSafetyReviewStatus.Rejected), response.ReviewStatus);
    }

    [Theory]
    [InlineData(nameof(CargoSafetyReviewStatus.Submitted))]
    [InlineData(nameof(CargoSafetyReviewStatus.Approved))]
    public async Task UpdateAsync_refuses_to_change_a_declaration_that_is_no_longer_editable(
        string reviewStatusName)
    {
        var reviewStatus = Enum.Parse<CargoSafetyReviewStatus>(reviewStatusName);

        await using var dbContext = CreateDbContext();
        var booking = await AddHazardousBookingAsync(dbContext, "BK-SAFETY-0005");
        var declaration = await AddDeclarationAsync(dbContext, booking.Id, reviewStatus);
        var service = new UpdateCargoSafetyInfoService(dbContext);

        var exception = await Assert.ThrowsAsync<CargoSafetyInfoTransitionException>(
            () => service.UpdateAsync(
                booking.Id,
                CreateValidRequest(storageConditions: "Ventilated hold"),
                CancellationToken.None));

        Assert.Equal(declaration.Id, exception.CargoSafetyInfoId);
        Assert.Equal(reviewStatus, exception.CurrentStatus);
        Assert.Equal("Keep below 25C", declaration.StorageConditions);
    }

    [Fact]
    public async Task UpdateAsync_reports_every_invalid_field_at_once()
    {
        await using var dbContext = CreateDbContext();
        var booking = await AddHazardousBookingAsync(dbContext, "BK-SAFETY-0006");
        var service = new UpdateCargoSafetyInfoService(dbContext);

        var exception = await Assert.ThrowsAsync<CargoSafetyInfoValidationException>(
            () => service.UpdateAsync(
                booking.Id,
                new UpdateCargoSafetyInfoRequest
                {
                    ImdgClass = "  ",
                    UnNumber = "12A4",
                    StorageConditions = null,
                    HandlingNotes = new string('x', 1001),
                },
                CancellationToken.None));

        Assert.Contains(CargoSafetyInfoValidator.ImdgClassField, exception.Errors.Keys);
        Assert.Contains(CargoSafetyInfoValidator.UnNumberField, exception.Errors.Keys);
        Assert.Contains(CargoSafetyInfoValidator.StorageConditionsField, exception.Errors.Keys);
        Assert.Contains(CargoSafetyInfoValidator.HandlingNotesField, exception.Errors.Keys);
        Assert.Empty(await dbContext.Set<CargoSafetyInfo>().ToListAsync(CancellationToken.None));
    }

    [Fact]
    public async Task UpdateAsync_rejects_an_unknown_booking()
    {
        await using var dbContext = CreateDbContext();
        var unknownBookingId = Guid.NewGuid();
        var service = new UpdateCargoSafetyInfoService(dbContext);

        var exception = await Assert.ThrowsAsync<CargoBookingNotFoundException>(
            () => service.UpdateAsync(unknownBookingId, CreateValidRequest(), CancellationToken.None));

        Assert.Equal(unknownBookingId, exception.CargoBookingId);
    }

    [Fact]
    public async Task UpdateAsync_rejects_a_booking_that_is_not_hazardous()
    {
        await using var dbContext = CreateDbContext();
        var booking = await AddBookingAsync(dbContext, "BK-SAFETY-0007", isHazardous: false);
        var service = new UpdateCargoSafetyInfoService(dbContext);

        var exception = await Assert.ThrowsAsync<CargoBookingNotHazardousException>(
            () => service.UpdateAsync(booking.Id, CreateValidRequest(), CancellationToken.None));

        Assert.Equal(booking.Id, exception.CargoBookingId);
        Assert.Empty(await dbContext.Set<CargoSafetyInfo>().ToListAsync(CancellationToken.None));
    }

    [Fact]
    public async Task UpdateAsync_persists_nothing_when_the_save_fails()
    {
        var databaseName = $"HarborOpsUnitTests-{Guid.NewGuid()}";
        Guid bookingId;

        await using (var seedContext = CreateDbContext(databaseName))
        {
            var booking = await AddHazardousBookingAsync(seedContext, "BK-SAFETY-0008");
            bookingId = booking.Id;
        }

        await using (var failingContext = CreateDbContext(databaseName, failOnSave: true))
        {
            var service = new UpdateCargoSafetyInfoService(failingContext);

            await Assert.ThrowsAsync<DbUpdateException>(
                () => service.UpdateAsync(bookingId, CreateValidRequest(), CancellationToken.None));
        }

        await using var assertContext = CreateDbContext(databaseName);

        Assert.Empty(await assertContext.Set<CargoSafetyInfo>().ToListAsync(CancellationToken.None));
    }

    private static UpdateCargoSafetyInfoRequest CreateValidRequest(
        string storageConditions = "Keep below 25C")
    {
        return new UpdateCargoSafetyInfoRequest
        {
            ImdgClass = "3",
            UnNumber = "UN1203",
            StorageConditions = storageConditions,
            HandlingNotes = null,
        };
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

        if (reviewStatus is not CargoSafetyReviewStatus.Draft)
        {
            declaration.Submit(OfficerUserId, ActedAtUtc);
        }

        if (reviewStatus is CargoSafetyReviewStatus.Approved)
        {
            declaration.Approve(ReviewerUserId, ActedAtUtc, notes: null);
        }

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
