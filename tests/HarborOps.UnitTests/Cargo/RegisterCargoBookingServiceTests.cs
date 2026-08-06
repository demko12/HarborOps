using HarborOps.Infrastructure.Persistence;
using HarborOps.Modules.Cargo.Application;
using HarborOps.Modules.Cargo.Contracts;
using HarborOps.Modules.Cargo.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.UnitTests.Cargo;

public sealed class RegisterCargoBookingServiceTests
{
    private static readonly Guid VoyageId = Guid.Parse("7b6a5c4d-3e2f-4a1b-9c8d-0e1f2a3b4c5d");

    [Fact]
    public async Task Register_rejects_missing_text_fields()
    {
        await using var dbContext = CreateDbContext();
        var service = new RegisterCargoBookingService(dbContext);
        var request = CreateValidRequest("BK-0001") with
        {
            BookingNumber = " ",
            Description = null,
            CargoType = string.Empty,
        };

        var exception = await Assert.ThrowsAsync<RegisterCargoBookingValidationException>(
            () => service.RegisterAsync(request, CancellationToken.None));

        Assert.Equal(3, exception.Errors.Count);
        Assert.Contains(RegisterCargoBookingValidator.BookingNumberField, exception.Errors);
        Assert.Contains(RegisterCargoBookingValidator.DescriptionField, exception.Errors);
        Assert.Contains(RegisterCargoBookingValidator.CargoTypeField, exception.Errors);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public async Task Register_rejects_missing_or_empty_voyage_id(string? voyageId)
    {
        await using var dbContext = CreateDbContext();
        var service = new RegisterCargoBookingService(dbContext);
        var request = CreateValidRequest("BK-0002") with
        {
            VoyageId = voyageId is null ? null : Guid.Parse(voyageId),
        };

        var exception = await Assert.ThrowsAsync<RegisterCargoBookingValidationException>(
            () => service.RegisterAsync(request, CancellationToken.None));

        Assert.Contains(RegisterCargoBookingValidator.VoyageIdField, exception.Errors);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0d)]
    [InlineData(-1.5d)]
    public async Task Register_rejects_invalid_weight(double? weightKg)
    {
        await using var dbContext = CreateDbContext();
        var service = new RegisterCargoBookingService(dbContext);
        var request = CreateValidRequest("BK-0003") with
        {
            WeightKg = weightKg is null ? null : (decimal)weightKg,
        };

        var exception = await Assert.ThrowsAsync<RegisterCargoBookingValidationException>(
            () => service.RegisterAsync(request, CancellationToken.None));

        Assert.Contains(RegisterCargoBookingValidator.WeightKgField, exception.Errors);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    [InlineData(-3)]
    public async Task Register_rejects_invalid_package_count(int? packageCount)
    {
        await using var dbContext = CreateDbContext();
        var service = new RegisterCargoBookingService(dbContext);
        var request = CreateValidRequest("BK-0004") with
        {
            PackageCount = packageCount,
        };

        var exception = await Assert.ThrowsAsync<RegisterCargoBookingValidationException>(
            () => service.RegisterAsync(request, CancellationToken.None));

        Assert.Contains(RegisterCargoBookingValidator.PackageCountField, exception.Errors);
    }

    [Fact]
    public async Task Register_rejects_duplicate_booking_number()
    {
        await using var dbContext = CreateDbContext();
        var service = new RegisterCargoBookingService(dbContext);
        var firstRequest = CreateValidRequest("BK-1001");
        var duplicateRequest = firstRequest with { Description = "Different cargo" };

        await service.RegisterAsync(firstRequest, CancellationToken.None);

        var exception = await Assert.ThrowsAsync<DuplicateBookingNumberException>(
            () => service.RegisterAsync(duplicateRequest, CancellationToken.None));

        Assert.Equal("BK-1001", exception.BookingNumber);
    }

    [Fact]
    public async Task Register_persists_normalized_booking_and_returns_id()
    {
        await using var dbContext = CreateDbContext();
        var service = new RegisterCargoBookingService(dbContext);
        var request = CreateValidRequest("BK-2002");
        var startedUtc = DateTime.UtcNow;

        var response = await service.RegisterAsync(request, CancellationToken.None);

        var booking = await dbContext
            .Set<CargoBooking>()
            .SingleAsync(CancellationToken.None);

        Assert.NotEqual(Guid.Empty, response.Id);
        Assert.Equal(response.Id, booking.Id);
        Assert.Equal("BK-2002", booking.BookingNumber);
        Assert.Equal(VoyageId, booking.VoyageId);
        Assert.Equal("Palletized coffee beans", booking.Description);
        Assert.Equal("General Cargo", booking.CargoType);
        Assert.Equal(12500.500m, booking.WeightKg);
        Assert.Equal(42, booking.PackageCount);
        Assert.True(booking.IsHazardous);
        Assert.Equal(CargoStatus.Registered, booking.Status);
        Assert.InRange(booking.CreatedUtc, startedUtc, DateTime.UtcNow);
    }

    private static RegisterCargoBookingRequest CreateValidRequest(string bookingNumber)
    {
        return new RegisterCargoBookingRequest
        {
            BookingNumber = $" {bookingNumber} ",
            VoyageId = VoyageId,
            Description = " Palletized coffee beans ",
            CargoType = " General Cargo ",
            WeightKg = 12500.500m,
            PackageCount = 42,
            IsHazardous = true,
        };
    }

    private static HarborOpsDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<HarborOpsDbContext>()
            .UseInMemoryDatabase($"HarborOpsUnitTests-{Guid.NewGuid()}")
            .Options;

        return new HarborOpsDbContext(options);
    }
}
