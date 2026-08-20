using HarborOps.Infrastructure.Persistence;
using HarborOps.Modules.Maintenance.Application;
using HarborOps.Modules.Maintenance.Contracts;
using HarborOps.Modules.Maintenance.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.UnitTests.Maintenance;

public sealed class GetEquipmentQueryTests
{
    [Fact]
    public async Task GetEquipmentAsync_returns_the_first_page_using_default_paging()
    {
        await using var dbContext = CreateDbContext();
        var vesselId = Guid.NewGuid();

        for (var index = 1; index <= 26; index++)
        {
            await dbContext.Set<Equipment>().AddAsync(
                Equipment.Register(vesselId, $"Pump {index:D2}", "Auxiliary", "Alfa Laval", $"SN-{index:D2}"));
        }
        await dbContext.SaveChangesAsync();

        var query = new GetEquipmentQuery(dbContext);

        var response = await query.GetEquipmentAsync(new GetEquipmentRequest(), CancellationToken.None);

        Assert.Equal(1, response.PageNumber);
        Assert.Equal(25, response.PageSize);
        Assert.Equal(26, response.TotalCount);
        Assert.Equal(25, response.Items.Count);
        Assert.Equal("Pump 01", response.Items[0].Name);
        Assert.Equal("Pump 25", response.Items[^1].Name);
    }

    [Fact]
    public async Task GetEquipmentAsync_orders_by_name_then_id()
    {
        await using var dbContext = CreateDbContext();
        var vesselId = Guid.NewGuid();

        await dbContext.Set<Equipment>().AddAsync(Equipment.Register(vesselId, "Winch", "Deck", null, null));
        await dbContext.Set<Equipment>().AddAsync(Equipment.Register(vesselId, "Boiler", "Auxiliary", null, null));
        await dbContext.Set<Equipment>().AddAsync(Equipment.Register(vesselId, "Main engine", "Propulsion", null, null));
        await dbContext.SaveChangesAsync();

        var query = new GetEquipmentQuery(dbContext);

        var response = await query.GetEquipmentAsync(new GetEquipmentRequest(), CancellationToken.None);

        Assert.Equal(["Boiler", "Main engine", "Winch"], response.Items.Select(item => item.Name));
    }

    [Fact]
    public async Task GetEquipmentAsync_filters_by_vessel()
    {
        await using var dbContext = CreateDbContext();
        var vesselId = Guid.NewGuid();
        var otherVesselId = Guid.NewGuid();

        await dbContext.Set<Equipment>().AddAsync(Equipment.Register(vesselId, "Main engine", "Propulsion", null, null));
        await dbContext.Set<Equipment>().AddAsync(Equipment.Register(otherVesselId, "Boiler", "Auxiliary", null, null));
        await dbContext.SaveChangesAsync();

        var query = new GetEquipmentQuery(dbContext);

        var response = await query.GetEquipmentAsync(
            new GetEquipmentRequest { VesselId = vesselId },
            CancellationToken.None);

        Assert.Equal(1, response.TotalCount);
        Assert.Equal("Main engine", Assert.Single(response.Items).Name);
    }

    [Fact]
    public async Task GetEquipmentAsync_filters_by_category_and_manufacturer()
    {
        await using var dbContext = CreateDbContext();
        var vesselId = Guid.NewGuid();

        await dbContext.Set<Equipment>().AddAsync(
            Equipment.Register(vesselId, "Main engine", "Propulsion", "Wartsila", null));
        await dbContext.Set<Equipment>().AddAsync(
            Equipment.Register(vesselId, "Auxiliary engine", "Propulsion", "Caterpillar", null));
        await dbContext.Set<Equipment>().AddAsync(
            Equipment.Register(vesselId, "Boiler", "Auxiliary", "Wartsila", null));
        await dbContext.SaveChangesAsync();

        var query = new GetEquipmentQuery(dbContext);

        var byCategory = await query.GetEquipmentAsync(
            new GetEquipmentRequest { Category = "Propulsion" },
            CancellationToken.None);

        var byManufacturer = await query.GetEquipmentAsync(
            new GetEquipmentRequest { Manufacturer = "Wartsila" },
            CancellationToken.None);

        Assert.Equal(2, byCategory.TotalCount);
        Assert.Equal(2, byManufacturer.TotalCount);
        Assert.Equal(["Auxiliary engine", "Main engine"], byCategory.Items.Select(item => item.Name));
        Assert.Equal(["Boiler", "Main engine"], byManufacturer.Items.Select(item => item.Name));
    }

    [Fact]
    public async Task GetEquipmentAsync_searches_name_and_serial_number()
    {
        await using var dbContext = CreateDbContext();
        var vesselId = Guid.NewGuid();

        await dbContext.Set<Equipment>().AddAsync(
            Equipment.Register(vesselId, "Main engine", "Propulsion", null, "SN-1001"));
        await dbContext.Set<Equipment>().AddAsync(
            Equipment.Register(vesselId, "Boiler", "Auxiliary", null, "SN-2002"));
        await dbContext.SaveChangesAsync();

        var query = new GetEquipmentQuery(dbContext);

        var byName = await query.GetEquipmentAsync(
            new GetEquipmentRequest { Search = "engine" },
            CancellationToken.None);

        var bySerial = await query.GetEquipmentAsync(
            new GetEquipmentRequest { Search = "2002" },
            CancellationToken.None);

        Assert.Equal("Main engine", Assert.Single(byName.Items).Name);
        Assert.Equal("Boiler", Assert.Single(bySerial.Items).Name);
    }

    [Fact]
    public async Task GetEquipmentAsync_applies_custom_paging()
    {
        await using var dbContext = CreateDbContext();
        var vesselId = Guid.NewGuid();

        for (var index = 1; index <= 5; index++)
        {
            await dbContext.Set<Equipment>().AddAsync(
                Equipment.Register(vesselId, $"Pump {index:D2}", "Auxiliary", null, null));
        }
        await dbContext.SaveChangesAsync();

        var query = new GetEquipmentQuery(dbContext);

        var response = await query.GetEquipmentAsync(
            new GetEquipmentRequest { PageNumber = 2, PageSize = 2 },
            CancellationToken.None);

        Assert.Equal(2, response.PageNumber);
        Assert.Equal(2, response.PageSize);
        Assert.Equal(5, response.TotalCount);
        Assert.Equal(["Pump 03", "Pump 04"], response.Items.Select(item => item.Name));
    }

    [Fact]
    public async Task GetEquipmentAsync_rejects_an_invalid_page_size()
    {
        await using var dbContext = CreateDbContext();
        var query = new GetEquipmentQuery(dbContext);

        var exception = await Assert.ThrowsAsync<GetEquipmentValidationException>(
            () => query.GetEquipmentAsync(
                new GetEquipmentRequest { PageSize = GetEquipmentValidator.MaxPageSize + 1 },
                CancellationToken.None));

        Assert.Contains(GetEquipmentValidator.PageSizeField, exception.Errors);
    }

    private static HarborOpsDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<HarborOpsDbContext>()
            .UseInMemoryDatabase($"HarborOpsUnitTests-{Guid.NewGuid()}")
            .Options;

        return new HarborOpsDbContext(options);
    }
}
