using HarborOps.Infrastructure.Persistence;
using HarborOps.Modules.Cargo.Application;
using HarborOps.Modules.Cargo.Contracts;
using HarborOps.Modules.Cargo.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.UnitTests.Cargo;

public sealed class DeclareCargoSafetyInfoServiceTests
{
    private static readonly Guid VoyageId = Guid.Parse("5d4c3b2a-1e0f-4a9b-8c7d-6e5f4a3b2c1d");

    [Fact]
    public async Task Declare_persists_normalized_safety_information()
    {
        await using var dbContext = CreateDbContext();
        var booking = await AddBookingAsync(dbContext, "BK-SAFE-0001", isHazardous: true);
        var service = new DeclareCargoSafetyInfoService(dbContext);
        var request = CreateValidRequest();

        var response = await service.DeclareAsync(
            booking.Id,
            request,
            CancellationToken.None);

        var declaration = await dbContext
            .Set<CargoSafetyInfo>()
            .SingleAsync(CancellationToken.None);

        Assert.NotEqual(Guid.Empty, response.Id);
        Assert.Equal(response.Id, declaration.Id);
        Assert.Equal(booking.Id, declaration.CargoBookingId);
        Assert.Equal("1.4S", declaration.ImdgClass);
        Assert.Equal("UN0336", declaration.UnNumber);
        Assert.Equal("Keep below 25 degrees Celsius", declaration.StorageConditions);
        Assert.Equal("Segregate from oxidizers", declaration.HandlingNotes);
    }

    [Fact]
    public async Task Declare_stores_null_when_handling_notes_are_blank()
    {
        await using var dbContext = CreateDbContext();
        var booking = await AddBookingAsync(dbContext, "BK-SAFE-0002", isHazardous: true);
        var service = new DeclareCargoSafetyInfoService(dbContext);
        var request = CreateValidRequest() with { HandlingNotes = "   " };

        await service.DeclareAsync(booking.Id, request, CancellationToken.None);

        var declaration = await dbContext
            .Set<CargoSafetyInfo>()
            .SingleAsync(CancellationToken.None);

        Assert.Null(declaration.HandlingNotes);
    }

    [Fact]
    public async Task Declare_rejects_missing_required_fields()
    {
        await using var dbContext = CreateDbContext();
        var booking = await AddBookingAsync(dbContext, "BK-SAFE-0003", isHazardous: true);
        var service = new DeclareCargoSafetyInfoService(dbContext);
        var request = CreateValidRequest() with
        {
            ImdgClass = " ",
            UnNumber = null,
            StorageConditions = string.Empty,
        };

        var exception = await Assert.ThrowsAsync<DeclareCargoSafetyInfoValidationException>(
            () => service.DeclareAsync(booking.Id, request, CancellationToken.None));

        Assert.Equal(3, exception.Errors.Count);
        Assert.Contains(DeclareCargoSafetyInfoValidator.ImdgClassField, exception.Errors);
        Assert.Contains(DeclareCargoSafetyInfoValidator.UnNumberField, exception.Errors);
        Assert.Contains(DeclareCargoSafetyInfoValidator.StorageConditionsField, exception.Errors);
    }

    [Fact]
    public async Task Declare_rejects_values_exceeding_maximum_length()
    {
        await using var dbContext = CreateDbContext();
        var booking = await AddBookingAsync(dbContext, "BK-SAFE-0004", isHazardous: true);
        var service = new DeclareCargoSafetyInfoService(dbContext);
        var request = CreateValidRequest() with
        {
            ImdgClass = new string('A', CargoSafetyInfo.ImdgClassMaxLength + 1),
            UnNumber = new string('B', CargoSafetyInfo.UnNumberMaxLength + 1),
            StorageConditions = new string('C', CargoSafetyInfo.StorageConditionsMaxLength + 1),
            HandlingNotes = new string('D', CargoSafetyInfo.HandlingNotesMaxLength + 1),
        };

        var exception = await Assert.ThrowsAsync<DeclareCargoSafetyInfoValidationException>(
            () => service.DeclareAsync(booking.Id, request, CancellationToken.None));

        Assert.Equal(4, exception.Errors.Count);
        Assert.Contains(DeclareCargoSafetyInfoValidator.ImdgClassField, exception.Errors);
        Assert.Contains(DeclareCargoSafetyInfoValidator.UnNumberField, exception.Errors);
        Assert.Contains(DeclareCargoSafetyInfoValidator.StorageConditionsField, exception.Errors);
        Assert.Contains(DeclareCargoSafetyInfoValidator.HandlingNotesField, exception.Errors);
    }

    [Fact]
    public async Task Declare_accepts_values_at_maximum_length()
    {
        await using var dbContext = CreateDbContext();
        var booking = await AddBookingAsync(dbContext, "BK-SAFE-0005", isHazardous: true);
        var service = new DeclareCargoSafetyInfoService(dbContext);
        var request = CreateValidRequest() with
        {
            ImdgClass = new string('A', CargoSafetyInfo.ImdgClassMaxLength),
            UnNumber = new string('B', CargoSafetyInfo.UnNumberMaxLength),
            StorageConditions = new string('C', CargoSafetyInfo.StorageConditionsMaxLength),
            HandlingNotes = new string('D', CargoSafetyInfo.HandlingNotesMaxLength),
        };

        var response = await service.DeclareAsync(
            booking.Id,
            request,
            CancellationToken.None);

        Assert.NotEqual(Guid.Empty, response.Id);
    }

    [Fact]
    public async Task Declare_rejects_unknown_booking()
    {
        await using var dbContext = CreateDbContext();
        var service = new DeclareCargoSafetyInfoService(dbContext);
        var unknownBookingId = Guid.Parse("0a1b2c3d-4e5f-4a6b-8c9d-0e1f2a3b4c5d");

        var exception = await Assert.ThrowsAsync<CargoBookingNotFoundException>(
            () => service.DeclareAsync(
                unknownBookingId,
                CreateValidRequest(),
                CancellationToken.None));

        Assert.Equal(unknownBookingId, exception.CargoBookingId);
        Assert.Empty(dbContext.Set<CargoSafetyInfo>());
    }

    [Fact]
    public async Task Declare_rejects_non_hazardous_booking()
    {
        await using var dbContext = CreateDbContext();
        var booking = await AddBookingAsync(dbContext, "BK-SAFE-0006", isHazardous: false);
        var service = new DeclareCargoSafetyInfoService(dbContext);

        var exception = await Assert.ThrowsAsync<CargoBookingNotHazardousException>(
            () => service.DeclareAsync(
                booking.Id,
                CreateValidRequest(),
                CancellationToken.None));

        Assert.Equal(booking.Id, exception.CargoBookingId);
        Assert.Empty(dbContext.Set<CargoSafetyInfo>());
    }

    [Fact]
    public async Task Declare_rejects_duplicate_declaration()
    {
        await using var dbContext = CreateDbContext();
        var booking = await AddBookingAsync(dbContext, "BK-SAFE-0007", isHazardous: true);
        var service = new DeclareCargoSafetyInfoService(dbContext);

        await service.DeclareAsync(booking.Id, CreateValidRequest(), CancellationToken.None);

        var exception = await Assert.ThrowsAsync<CargoSafetyInfoAlreadyDeclaredException>(
            () => service.DeclareAsync(
                booking.Id,
                CreateValidRequest() with { UnNumber = "UN1234" },
                CancellationToken.None));

        Assert.Equal(booking.Id, exception.CargoBookingId);
        Assert.Single(dbContext.Set<CargoSafetyInfo>());
    }

    private static DeclareCargoSafetyInfoRequest CreateValidRequest()
    {
        return new DeclareCargoSafetyInfoRequest
        {
            ImdgClass = " 1.4s ",
            UnNumber = " un0336 ",
            StorageConditions = " Keep below 25 degrees Celsius ",
            HandlingNotes = " Segregate from oxidizers ",
        };
    }

    private static async Task<CargoBooking> AddBookingAsync(
        HarborOpsDbContext dbContext,
        string bookingNumber,
        bool isHazardous)
    {
        var booking = CargoBooking.Register(
            bookingNumber,
            VoyageId,
            "Ammonium nitrate",
            "Dangerous Goods",
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
