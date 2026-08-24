using System.Globalization;
using HarborOps.Infrastructure.Persistence;
using HarborOps.Modules.Voyages.Application;
using HarborOps.Modules.Voyages.Contracts;
using HarborOps.Modules.Voyages.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.UnitTests.Voyages;

public sealed class MasterDataLookupQueryTests
{
    [Fact]
    public async Task Port_lookup_is_bounded_by_the_default_take()
    {
        await using var dbContext = CreateDbContext();
        await SeedPortsAsync(dbContext, PortLookupValidator.DefaultTake + 5);
        var query = new PortLookupQuery(dbContext);

        var response = await query.LookupAsync(new PortLookupRequest(), CancellationToken.None);

        Assert.Equal(PortLookupValidator.DefaultTake, response.Items.Count);
        Assert.Equal("Port 01", response.Items[0].Name);
    }

    [Fact]
    public async Task Port_lookup_filters_by_name_and_un_locode()
    {
        await using var dbContext = CreateDbContext();
        await dbContext.Set<Port>().AddRangeAsync(
            [
                Port.Create("Port of Bar", "Montenegro", "Bar", "MEBAR"),
                Port.Create("Port of Kotor", "Montenegro", "Kotor", "MEKOT"),
            ],
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var query = new PortLookupQuery(dbContext);

        var byName = await query.LookupAsync(
            new PortLookupRequest { Search = "Kotor" },
            CancellationToken.None);
        var byUnLocode = await query.LookupAsync(
            new PortLookupRequest { Search = "MEBAR" },
            CancellationToken.None);

        var kotor = Assert.Single(byName.Items);
        Assert.Equal("Port of Kotor", kotor.Name);
        Assert.Equal("Montenegro", kotor.Country);
        Assert.Equal("MEKOT", kotor.UnLocode);
        Assert.Equal("Port of Bar", Assert.Single(byUnLocode.Items).Name);
    }

    [Fact]
    public async Task Port_lookup_rejects_a_take_above_the_bound()
    {
        await using var dbContext = CreateDbContext();
        var query = new PortLookupQuery(dbContext);

        var exception = await Assert.ThrowsAsync<PortLookupValidationException>(
            () => query.LookupAsync(
                new PortLookupRequest { Take = PortLookupValidator.MaxTake + 1 },
                CancellationToken.None));

        Assert.Contains(PortLookupValidator.TakeField, exception.Errors.Keys, StringComparer.Ordinal);
    }

    [Fact]
    public async Task Port_lookup_returns_no_items_when_no_port_is_registered()
    {
        await using var dbContext = CreateDbContext();
        var query = new PortLookupQuery(dbContext);

        var response = await query.LookupAsync(new PortLookupRequest(), CancellationToken.None);

        Assert.Empty(response.Items);
    }

    [Fact]
    public async Task Berth_lookup_returns_the_berths_of_one_port_in_name_order()
    {
        await using var dbContext = CreateDbContext();
        var bar = Port.Create("Port of Bar", "Montenegro");
        var kotor = Port.Create("Port of Kotor", "Montenegro");
        await dbContext.Set<Port>().AddRangeAsync([bar, kotor], CancellationToken.None);
        await dbContext.Set<Berth>().AddRangeAsync(
            [
                Berth.Create(bar.Id, "Berth 2", 180m, 10m, "Available"),
                Berth.Create(bar.Id, "Berth 1", 210m, 12m, "Out of service"),
                Berth.Create(kotor.Id, "Berth 9", 150m, 8m, "Available"),
            ],
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var query = new BerthLookupQuery(dbContext);

        var response = await query.LookupAsync(
            new BerthLookupRequest { PortId = bar.Id },
            CancellationToken.None);

        Assert.Equal(["Berth 1", "Berth 2"], response.Items.Select(item => item.Name));
        Assert.All(response.Items, item => Assert.Equal(bar.Id, item.PortId));
        Assert.Equal("Out of service", response.Items[0].Status);
    }

    [Fact]
    public async Task Berth_lookup_requires_a_port()
    {
        await using var dbContext = CreateDbContext();
        var query = new BerthLookupQuery(dbContext);

        var exception = await Assert.ThrowsAsync<BerthLookupValidationException>(
            () => query.LookupAsync(new BerthLookupRequest(), CancellationToken.None));

        Assert.Contains(BerthLookupValidator.PortIdField, exception.Errors.Keys, StringComparer.Ordinal);
    }

    [Fact]
    public async Task Berth_lookup_returns_no_items_for_a_port_without_berths()
    {
        await using var dbContext = CreateDbContext();
        var query = new BerthLookupQuery(dbContext);

        var response = await query.LookupAsync(
            new BerthLookupRequest { PortId = Guid.NewGuid() },
            CancellationToken.None);

        Assert.Empty(response.Items);
    }

    [Fact]
    public async Task Route_lookup_filters_by_name_and_honours_the_requested_take()
    {
        await using var dbContext = CreateDbContext();
        await dbContext.Set<Route>().AddRangeAsync(
            [
                Route.Create("Adriatic feeder", 560m, 2100),
                Route.Create("Adriatic shuttle", 120m, 480),
                Route.Create("Tyrrhenian loop", 900m, 3000),
            ],
            CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var query = new RouteLookupQuery(dbContext);

        var filtered = await query.LookupAsync(
            new RouteLookupRequest { Search = "Adriatic" },
            CancellationToken.None);
        var limited = await query.LookupAsync(
            new RouteLookupRequest { Take = 1 },
            CancellationToken.None);

        Assert.Equal(
            ["Adriatic feeder", "Adriatic shuttle"],
            filtered.Items.Select(item => item.Name));
        Assert.Equal(560m, filtered.Items[0].EstimatedDistance);
        Assert.Equal(2100, filtered.Items[0].EstimatedDuration);
        Assert.Equal("Adriatic feeder", Assert.Single(limited.Items).Name);
    }

    [Fact]
    public async Task Route_lookup_rejects_a_take_below_one()
    {
        await using var dbContext = CreateDbContext();
        var query = new RouteLookupQuery(dbContext);

        var exception = await Assert.ThrowsAsync<RouteLookupValidationException>(
            () => query.LookupAsync(
                new RouteLookupRequest { Take = 0 },
                CancellationToken.None));

        Assert.Contains(RouteLookupValidator.TakeField, exception.Errors.Keys, StringComparer.Ordinal);
    }

    private static async Task SeedPortsAsync(HarborOpsDbContext dbContext, int count)
    {
        for (var index = 1; index <= count; index++)
        {
            var name = $"Port {index.ToString("00", CultureInfo.InvariantCulture)}";
            await dbContext.Set<Port>().AddAsync(
                Port.Create(name, "Montenegro"),
                CancellationToken.None);
        }

        await dbContext.SaveChangesAsync(CancellationToken.None);
    }

    private static HarborOpsDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<HarborOpsDbContext>()
            .UseInMemoryDatabase($"HarborOpsUnitTests-{Guid.NewGuid()}")
            .Options;

        return new HarborOpsDbContext(options);
    }
}
