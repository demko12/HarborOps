using HarborOps.Infrastructure.Persistence;
using HarborOps.Modules.Voyages.Application;
using HarborOps.Modules.Voyages.Contracts;
using HarborOps.Modules.Voyages.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.UnitTests.Voyages;

public sealed class SearchBerthsQueryTests
{
    [Fact]
    public async Task Search_returns_only_the_berths_of_the_requested_port_ordered_by_name()
    {
        await using var dbContext = CreateDbContext();
        var barId = await SeedPortAsync(dbContext, "Port of Bar");
        var kotorId = await SeedPortAsync(dbContext, "Port of Kotor");
        await SeedBerthsAsync(
            dbContext,
            Berth.Create(barId, "Berth 2", 180m, 10m, "Available"),
            Berth.Create(barId, "Berth 1", 210m, 12m, "Available"),
            Berth.Create(kotorId, "Berth 1", 150m, 8m, "Available"));
        var query = new SearchBerthsQuery(dbContext);

        var response = await query.SearchAsync(
            new SearchBerthsRequest { PortId = barId },
            CancellationToken.None);

        Assert.Equal(2, response.TotalCount);
        Assert.Equal(["Berth 1", "Berth 2"], response.Items.Select(item => item.Name));
        Assert.All(response.Items, item => Assert.Equal(barId, item.PortId));

        var first = response.Items[0];
        Assert.Equal(210m, first.Length);
        Assert.Equal(12m, first.MaxDraft);
        Assert.Equal("Available", first.Status);
    }

    [Fact]
    public async Task Search_filters_by_status_and_name()
    {
        await using var dbContext = CreateDbContext();
        var barId = await SeedPortAsync(dbContext, "Port of Bar");
        await SeedBerthsAsync(
            dbContext,
            Berth.Create(barId, "Berth 1", 210m, 12m, "Available"),
            Berth.Create(barId, "Berth 2", 180m, 10m, "Out of service"),
            Berth.Create(barId, "Tanker quay", 260m, 14m, "Out of service"));
        var query = new SearchBerthsQuery(dbContext);

        var byStatus = await query.SearchAsync(
            new SearchBerthsRequest { PortId = barId, Status = "Out of service" },
            CancellationToken.None);
        var bySearch = await query.SearchAsync(
            new SearchBerthsRequest { PortId = barId, Search = "Tanker" },
            CancellationToken.None);

        Assert.Equal(["Berth 2", "Tanker quay"], byStatus.Items.Select(item => item.Name));
        Assert.Equal("Tanker quay", Assert.Single(bySearch.Items).Name);
    }

    [Fact]
    public async Task Search_pages_through_the_berths_of_a_port()
    {
        await using var dbContext = CreateDbContext();
        var barId = await SeedPortAsync(dbContext, "Port of Bar");
        await SeedBerthsAsync(
            dbContext,
            Berth.Create(barId, "Berth 1", 210m, 12m, "Available"),
            Berth.Create(barId, "Berth 2", 180m, 10m, "Available"),
            Berth.Create(barId, "Berth 3", 160m, 9m, "Available"));
        var query = new SearchBerthsQuery(dbContext);

        var response = await query.SearchAsync(
            new SearchBerthsRequest { PortId = barId, PageNumber = 2, PageSize = 2 },
            CancellationToken.None);

        Assert.Equal(3, response.TotalCount);
        Assert.Equal("Berth 3", Assert.Single(response.Items).Name);
    }

    [Fact]
    public async Task Search_requires_a_port()
    {
        await using var dbContext = CreateDbContext();
        var query = new SearchBerthsQuery(dbContext);

        var exception = await Assert.ThrowsAsync<SearchBerthsValidationException>(
            () => query.SearchAsync(new SearchBerthsRequest(), CancellationToken.None));

        Assert.Contains(SearchBerthsValidator.PortIdField, exception.Errors.Keys, StringComparer.Ordinal);
    }

    [Fact]
    public async Task Search_returns_an_empty_page_for_a_port_without_berths()
    {
        await using var dbContext = CreateDbContext();
        var barId = await SeedPortAsync(dbContext, "Port of Bar");
        var query = new SearchBerthsQuery(dbContext);

        var response = await query.SearchAsync(
            new SearchBerthsRequest { PortId = barId },
            CancellationToken.None);

        Assert.Empty(response.Items);
        Assert.Equal(0, response.TotalCount);
    }

    private static async Task SeedBerthsAsync(HarborOpsDbContext dbContext, params Berth[] berths)
    {
        await dbContext.Set<Berth>().AddRangeAsync(berths, CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);
    }

    private static async Task<Guid> SeedPortAsync(HarborOpsDbContext dbContext, string name)
    {
        var port = Port.Create(name, "Montenegro");

        await dbContext.Set<Port>().AddAsync(port, CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        return port.Id;
    }

    private static HarborOpsDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<HarborOpsDbContext>()
            .UseInMemoryDatabase($"HarborOpsUnitTests-{Guid.NewGuid()}")
            .Options;

        return new HarborOpsDbContext(options);
    }
}
