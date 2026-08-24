using HarborOps.Infrastructure.Persistence;
using HarborOps.Modules.Voyages.Application;
using HarborOps.Modules.Voyages.Contracts;
using HarborOps.Modules.Voyages.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.UnitTests.Voyages;

public sealed class PortQueryTests
{
    [Fact]
    public async Task SearchAsync_returns_every_display_field()
    {
        await using var dbContext = CreateDbContext();
        var port = Port.Create("Rotterdam", "Netherlands", "Rotterdam", "NLRTM", shipyardName: "Damen");
        await SeedAsync(dbContext, port);

        var response = await new SearchPortsQuery(dbContext).SearchAsync(
            new SearchPortsRequest(),
            CancellationToken.None);

        var item = Assert.Single(response.Items);

        Assert.Equal(port.Id, item.PortId);
        Assert.Equal("Rotterdam", item.Name);
        Assert.Equal("Rotterdam", item.City);
        Assert.Equal("Netherlands", item.Country);
        Assert.Equal("NLRTM", item.UnLocode);
        Assert.Equal("Damen", item.ShipyardName);
    }

    [Theory]
    [InlineData("otterd")]
    [InlineData("NLRTM")]
    public async Task SearchAsync_matches_the_name_city_or_locode(string search)
    {
        await using var dbContext = CreateDbContext();
        await SeedAsync(
            dbContext,
            Port.Create("Rotterdam", "Netherlands", "Rotterdam", "NLRTM"),
            Port.Create("Hamburg", "Germany", "Hamburg", "DEHAM"));

        var response = await new SearchPortsQuery(dbContext).SearchAsync(
            new SearchPortsRequest { Search = search },
            CancellationToken.None);

        Assert.Equal("Rotterdam", Assert.Single(response.Items).Name);
    }

    [Fact]
    public async Task SearchAsync_can_return_only_ports_that_carry_a_shipyard()
    {
        await using var dbContext = CreateDbContext();
        await SeedAsync(
            dbContext,
            Port.Create("Gdansk", "Poland", shipyardName: "Remontowa"),
            Port.Create("Hamburg", "Germany"));

        var response = await new SearchPortsQuery(dbContext).SearchAsync(
            new SearchPortsRequest { ShipyardsOnly = true },
            CancellationToken.None);

        Assert.Equal("Gdansk", Assert.Single(response.Items).Name);
    }

    [Fact]
    public async Task SearchAsync_orders_by_name_then_identifier()
    {
        await using var dbContext = CreateDbContext();
        await SeedAsync(
            dbContext,
            Port.Create("Singapore", "Singapore"),
            Port.Create("Antwerp", "Belgium"),
            Port.Create("Hamburg", "Germany"));

        var response = await new SearchPortsQuery(dbContext).SearchAsync(
            new SearchPortsRequest(),
            CancellationToken.None);

        Assert.Equal(["Antwerp", "Hamburg", "Singapore"], response.Items.Select(item => item.Name));
    }

    [Fact]
    public async Task SearchAsync_pages_without_losing_the_total_count()
    {
        await using var dbContext = CreateDbContext();
        await SeedAsync(
            dbContext,
            Port.Create("Antwerp", "Belgium"),
            Port.Create("Hamburg", "Germany"),
            Port.Create("Singapore", "Singapore"));

        var response = await new SearchPortsQuery(dbContext).SearchAsync(
            new SearchPortsRequest { PageNumber = 2, PageSize = 2 },
            CancellationToken.None);

        Assert.Equal(3, response.TotalCount);
        Assert.Equal("Singapore", Assert.Single(response.Items).Name);
    }

    [Fact]
    public async Task SearchAsync_rejects_a_page_size_beyond_the_bound()
    {
        await using var dbContext = CreateDbContext();

        var exception = await Assert.ThrowsAsync<SearchPortsValidationException>(
            () => new SearchPortsQuery(dbContext).SearchAsync(
                new SearchPortsRequest { PageSize = SearchPortsValidator.MaxPageSize + 1 },
                CancellationToken.None));

        Assert.True(exception.Errors.ContainsKey(SearchPortsValidator.PageSizeField));
    }

    [Fact]
    public async Task LookupAsync_resolves_only_the_requested_ports()
    {
        await using var dbContext = CreateDbContext();
        var wanted = Port.Create("Rotterdam", "Netherlands");
        await SeedAsync(dbContext, wanted, Port.Create("Hamburg", "Germany"));

        var response = await new PortLookupService(dbContext).LookupAsync(
            new PortLookupRequest { PortIds = [wanted.Id] },
            CancellationToken.None);

        var item = Assert.Single(response.Ports);

        Assert.Equal(wanted.Id, item.Id);
        Assert.Equal("Rotterdam", item.Name);
        Assert.Equal("Netherlands", item.Country);
    }

    [Fact]
    public async Task LookupAsync_ignores_an_identifier_that_no_longer_exists()
    {
        await using var dbContext = CreateDbContext();
        var wanted = Port.Create("Rotterdam", "Netherlands");
        await SeedAsync(dbContext, wanted);

        var response = await new PortLookupService(dbContext).LookupAsync(
            new PortLookupRequest { PortIds = [wanted.Id, Guid.NewGuid()] },
            CancellationToken.None);

        Assert.Single(response.Ports);
    }

    [Fact]
    public async Task LookupAsync_rejects_an_empty_request()
    {
        await using var dbContext = CreateDbContext();

        var exception = await Assert.ThrowsAsync<PortLookupValidationException>(
            () => new PortLookupService(dbContext).LookupAsync(
                new PortLookupRequest { PortIds = [] },
                CancellationToken.None));

        Assert.True(exception.Errors.ContainsKey(PortLookupValidator.PortIdsField));
    }

    [Fact]
    public async Task LookupAsync_rejects_more_identifiers_than_the_bound_allows()
    {
        await using var dbContext = CreateDbContext();
        var portIds = Enumerable.Range(0, PortLookupValidator.MaxPortIds + 1)
            .Select(_ => Guid.NewGuid())
            .ToArray();

        var exception = await Assert.ThrowsAsync<PortLookupValidationException>(
            () => new PortLookupService(dbContext).LookupAsync(
                new PortLookupRequest { PortIds = portIds },
                CancellationToken.None));

        Assert.True(exception.Errors.ContainsKey(PortLookupValidator.PortIdsField));
    }

    private static async Task SeedAsync(HarborOpsDbContext dbContext, params Port[] ports)
    {
        await dbContext.Set<Port>().AddRangeAsync(ports);
        await dbContext.SaveChangesAsync();
    }

    private static HarborOpsDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<HarborOpsDbContext>()
            .UseInMemoryDatabase($"HarborOpsUnitTests-{Guid.NewGuid()}")
            .Options;

        return new HarborOpsDbContext(options);
    }
}
