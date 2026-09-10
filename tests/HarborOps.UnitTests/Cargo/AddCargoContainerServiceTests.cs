using HarborOps.Infrastructure.Persistence;
using HarborOps.Modules.Cargo.Application;
using HarborOps.Modules.Cargo.Contracts;
using HarborOps.Modules.Cargo.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.UnitTests.Cargo;

public sealed class AddCargoContainerServiceTests
{
    private static readonly Guid VoyageId = Guid.Parse("7f6e5d4c-3b2a-4190-8f7e-6d5c4b3a2190");

    [Fact]
    public async Task AddAsync_registers_the_container_against_the_booking()
    {
        await using var dbContext = CreateDbContext();
        var booking = await AddBookingAsync(dbContext, "BK-ADD-0001");
        var service = new AddCargoContainerService(dbContext);

        var response = await service.AddAsync(
            booking.Id,
            Request("MSKU1234567", "42G1"),
            CancellationToken.None);

        var container = await dbContext.Set<Container>().SingleAsync(CancellationToken.None);
        Assert.Equal(container.Id, response.Id);
        Assert.Equal("MSKU1234567", response.ContainerNumber);
        Assert.Equal(nameof(CargoStatus.Registered), response.Status);
        Assert.Equal(booking.Id, container.CargoBookingId);
        Assert.Equal("42G1", container.ContainerType);
        Assert.Equal("NLRTM", container.PortOfLoadingCode);
        Assert.Equal("USNYC", container.PortOfDischargeCode);
        Assert.Equal(12500m, container.WeightKg);
    }

    [Fact]
    public async Task AddAsync_leaves_the_vessel_unset()
    {
        await using var dbContext = CreateDbContext();
        var booking = await AddBookingAsync(dbContext, "BK-ADD-0002");
        var service = new AddCargoContainerService(dbContext);

        await service.AddAsync(booking.Id, Request("MSKU1234567"), CancellationToken.None);

        var container = await dbContext.Set<Container>().SingleAsync(CancellationToken.None);
        Assert.Null(container.VesselId);
    }

    [Fact]
    public async Task AddAsync_normalizes_the_container_number()
    {
        await using var dbContext = CreateDbContext();
        var booking = await AddBookingAsync(dbContext, "BK-ADD-0003");
        var service = new AddCargoContainerService(dbContext);

        var response = await service.AddAsync(
            booking.Id,
            Request("  msku1234567  "),
            CancellationToken.None);

        Assert.Equal("MSKU1234567", response.ContainerNumber);
    }

    [Fact]
    public async Task AddAsync_rejects_a_duplicate_number_that_differs_only_by_case()
    {
        await using var dbContext = CreateDbContext();
        var booking = await AddBookingAsync(dbContext, "BK-ADD-0004");
        var service = new AddCargoContainerService(dbContext);
        await service.AddAsync(booking.Id, Request("MSKU1234567"), CancellationToken.None);

        var exception = await Assert.ThrowsAsync<DuplicateContainerNumberException>(
            () => service.AddAsync(booking.Id, Request("msku1234567"), CancellationToken.None));

        Assert.Equal("MSKU1234567", exception.ContainerNumber);
        Assert.Equal(1, await dbContext.Set<Container>().CountAsync(CancellationToken.None));
    }

    [Fact]
    public async Task AddAsync_rejects_a_number_already_used_by_another_booking()
    {
        await using var dbContext = CreateDbContext();
        var booking = await AddBookingAsync(dbContext, "BK-ADD-0005");
        var otherBooking = await AddBookingAsync(dbContext, "BK-ADD-0006");
        var service = new AddCargoContainerService(dbContext);
        await service.AddAsync(otherBooking.Id, Request("MSKU1234567"), CancellationToken.None);

        await Assert.ThrowsAsync<DuplicateContainerNumberException>(
            () => service.AddAsync(booking.Id, Request("MSKU1234567"), CancellationToken.None));
    }

    [Fact]
    public async Task AddAsync_rejects_a_missing_container_number()
    {
        await using var dbContext = CreateDbContext();
        var booking = await AddBookingAsync(dbContext, "BK-ADD-0007");
        var service = new AddCargoContainerService(dbContext);

        var exception = await Assert.ThrowsAsync<AddCargoContainerValidationException>(
            () => service.AddAsync(booking.Id, Request("   "), CancellationToken.None));

        Assert.Contains(
            AddCargoContainerValidator.ContainerNumberField,
            exception.Errors.Keys,
            StringComparer.Ordinal);
    }

    [Fact]
    public async Task AddAsync_rejects_a_missing_container_type()
    {
        await using var dbContext = CreateDbContext();
        var booking = await AddBookingAsync(dbContext, "BK-ADD-0008");
        var service = new AddCargoContainerService(dbContext);

        var exception = await Assert.ThrowsAsync<AddCargoContainerValidationException>(
            () => service.AddAsync(
                booking.Id,
                Request("MSKU1234567", "  "),
                CancellationToken.None));

        Assert.Contains(
            AddCargoContainerValidator.ContainerTypeField,
            exception.Errors.Keys,
            StringComparer.Ordinal);
    }

    [Fact]
    public async Task AddAsync_rejects_a_container_number_that_is_too_long()
    {
        await using var dbContext = CreateDbContext();
        var booking = await AddBookingAsync(dbContext, "BK-ADD-0009");
        var service = new AddCargoContainerService(dbContext);
        var tooLong = new string('A', Container.ContainerNumberMaxLength + 1);

        var exception = await Assert.ThrowsAsync<AddCargoContainerValidationException>(
            () => service.AddAsync(booking.Id, Request(tooLong), CancellationToken.None));

        Assert.Contains(
            AddCargoContainerValidator.ContainerNumberField,
            exception.Errors.Keys,
            StringComparer.Ordinal);
    }

    [Fact]
    public async Task AddAsync_rejects_a_completed_booking()
    {
        await using var dbContext = CreateDbContext();
        var booking = await AddBookingAsync(dbContext, "BK-ADD-0010", CargoStatus.Completed);
        var service = new AddCargoContainerService(dbContext);

        var exception = await Assert.ThrowsAsync<CargoBookingCompletedException>(
            () => service.AddAsync(booking.Id, Request("MSKU1234567"), CancellationToken.None));

        Assert.Equal(booking.Id, exception.CargoBookingId);
        Assert.Empty(await dbContext.Set<Container>().ToListAsync(CancellationToken.None));
    }

    [Fact]
    public async Task AddAsync_rejects_an_unknown_booking()
    {
        await using var dbContext = CreateDbContext();
        var service = new AddCargoContainerService(dbContext);
        var unknownBookingId = Guid.NewGuid();

        var exception = await Assert.ThrowsAsync<CargoBookingNotFoundException>(
            () => service.AddAsync(unknownBookingId, Request("MSKU1234567"), CancellationToken.None));

        Assert.Equal(unknownBookingId, exception.CargoBookingId);
    }

    private static AddCargoContainerRequest Request(
        string? containerNumber,
        string? containerType = "22G1")
    {
        return new AddCargoContainerRequest
        {
            ContainerNumber = containerNumber,
            ContainerType = containerType,
            PortOfLoadingCode = "NLRTM",
            PortOfDischargeCode = "USNYC",
            WeightKg = 12500m,
        };
    }

    private static async Task<CargoBooking> AddBookingAsync(
        HarborOpsDbContext dbContext,
        string bookingNumber,
        CargoStatus status = CargoStatus.Registered)
    {
        var booking = CargoBooking.Register(
            bookingNumber,
            VoyageId,
            "Steel coils",
            "General Cargo",
            5000.000m,
            10,
            isHazardous: false);

        while (booking.Status != status)
        {
            booking.AdvanceStatusTo(booking.Status + 1);
        }

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
