using HarborOps.Infrastructure.Persistence;
using HarborOps.Modules.Maintenance.Application;
using HarborOps.Modules.Maintenance.Contracts;
using HarborOps.Modules.Maintenance.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.UnitTests.Maintenance;

public sealed class RegisterEquipmentServiceTests
{
    [Fact]
    public async Task RegisterAsync_persists_trimmed_equipment_for_the_vessel()
    {
        await using var dbContext = CreateDbContext();
        var vesselId = Guid.NewGuid();
        var service = new RegisterEquipmentService(dbContext);

        var response = await service.RegisterAsync(
            new RegisterEquipmentRequest
            {
                VesselId = vesselId,
                Name = "  Main engine  ",
                Category = " Propulsion ",
                Manufacturer = " Wartsila ",
                SerialNumber = " SN-1001 ",
            },
            CancellationToken.None);

        var stored = await dbContext.Set<Equipment>().SingleAsync();

        Assert.Equal(stored.Id, response.Id);
        Assert.Equal(vesselId, stored.VesselId);
        Assert.Equal("Main engine", stored.Name);
        Assert.Equal("Propulsion", stored.Category);
        Assert.Equal("Wartsila", stored.Manufacturer);
        Assert.Equal("SN-1001", stored.SerialNumber);
    }

    [Fact]
    public async Task RegisterAsync_stores_blank_optional_values_as_null()
    {
        await using var dbContext = CreateDbContext();
        var service = new RegisterEquipmentService(dbContext);

        await service.RegisterAsync(
            new RegisterEquipmentRequest
            {
                VesselId = Guid.NewGuid(),
                Name = "Bow thruster",
                Category = "Manoeuvring",
                Manufacturer = "   ",
                SerialNumber = null,
            },
            CancellationToken.None);

        var stored = await dbContext.Set<Equipment>().SingleAsync();

        Assert.Null(stored.Manufacturer);
        Assert.Null(stored.SerialNumber);
    }

    [Fact]
    public async Task RegisterAsync_requires_a_vessel_id()
    {
        await using var dbContext = CreateDbContext();
        var service = new RegisterEquipmentService(dbContext);

        var exception = await Assert.ThrowsAsync<RegisterEquipmentValidationException>(
            () => service.RegisterAsync(
                new RegisterEquipmentRequest
                {
                    VesselId = Guid.Empty,
                    Name = "Main engine",
                    Category = "Propulsion",
                },
                CancellationToken.None));

        Assert.Contains(RegisterEquipmentValidator.VesselIdField, exception.Errors);
        Assert.Empty(dbContext.Set<Equipment>());
    }

    [Fact]
    public async Task RegisterAsync_requires_a_name_and_a_category()
    {
        await using var dbContext = CreateDbContext();
        var service = new RegisterEquipmentService(dbContext);

        var exception = await Assert.ThrowsAsync<RegisterEquipmentValidationException>(
            () => service.RegisterAsync(
                new RegisterEquipmentRequest
                {
                    VesselId = Guid.NewGuid(),
                    Name = "  ",
                    Category = null,
                },
                CancellationToken.None));

        Assert.Contains(RegisterEquipmentValidator.NameField, exception.Errors);
        Assert.Contains(RegisterEquipmentValidator.CategoryField, exception.Errors);
    }

    [Fact]
    public async Task RegisterAsync_rejects_values_longer_than_the_column_length()
    {
        await using var dbContext = CreateDbContext();
        var service = new RegisterEquipmentService(dbContext);

        var exception = await Assert.ThrowsAsync<RegisterEquipmentValidationException>(
            () => service.RegisterAsync(
                new RegisterEquipmentRequest
                {
                    VesselId = Guid.NewGuid(),
                    Name = new string('n', Equipment.NameMaxLength + 1),
                    Category = new string('c', Equipment.CategoryMaxLength + 1),
                    Manufacturer = new string('m', Equipment.ManufacturerMaxLength + 1),
                    SerialNumber = new string('s', Equipment.SerialNumberMaxLength + 1),
                },
                CancellationToken.None));

        Assert.Contains(RegisterEquipmentValidator.NameField, exception.Errors);
        Assert.Contains(RegisterEquipmentValidator.CategoryField, exception.Errors);
        Assert.Contains(RegisterEquipmentValidator.ManufacturerField, exception.Errors);
        Assert.Contains(RegisterEquipmentValidator.SerialNumberField, exception.Errors);
    }

    private static HarborOpsDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<HarborOpsDbContext>()
            .UseInMemoryDatabase($"HarborOpsUnitTests-{Guid.NewGuid()}")
            .Options;

        return new HarborOpsDbContext(options);
    }
}
