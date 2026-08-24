using HarborOps.Infrastructure.Persistence;
using HarborOps.Modules.Voyages.Application;
using HarborOps.Modules.Voyages.Contracts;
using HarborOps.Modules.Voyages.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.UnitTests.Voyages;

public sealed class SearchPortsQueryTests
{
    [Fact]
    public async Task Search_orders_ports_by_name_and_projects_their_master_data()
    {
        await using var dbContext = CreateDbContext();
        await SeedAsync(
            dbContext,
            Port.Create("Port of Kotor", "Montenegro", "Kotor", "MEKOT", 42.4247m, 18.7712m),
            Port.Create("Port of Bar", "Montenegro", "Bar", "MEBAR", 42.0936m, 19.0894m));
        var query = new SearchPortsQuery(dbContext);

        var response = await query.SearchAsync(new SearchPortsRequest(), CancellationToken.None);

        Assert.Equal(2, response.TotalCount);
        Assert.Equal(SearchPortsValidator.DefaultPageNumber, response.PageNumber);
        Assert.Equal(SearchPortsValidator.DefaultPageSize, response.PageSize);
        Assert.Equal(
            ["Port of Bar", "Port of Kotor"],
            response.Items.Select(item => item.Name));

        var bar = response.Items[0];
        Assert.Equal("Montenegro", bar.Country);
        Assert.Equal("Bar", bar.City);
        Assert.Equal("MEBAR", bar.UnLocode);
        Assert.Equal(42.0936m, bar.Latitude);
        Assert.Equal(19.0894m, bar.Longitude);
    }

    [Theory]
    [InlineData("Kotor", "Port of Kotor")]
    [InlineData("Genoa", "Ligurian gateway")]
    [InlineData("MEBAR", "Port of Bar")]
    public async Task Search_matches_name_city_and_un_locode(string search, string expectedName)
    {
        await using var dbContext = CreateDbContext();
        await SeedAsync(
            dbContext,
            Port.Create("Port of Bar", "Montenegro", "Bar", "MEBAR"),
            Port.Create("Port of Kotor", "Montenegro", "Kotor", "MEKOT"),
            Port.Create("Ligurian gateway", "Italy", "Genoa", "ITGOA"));
        var query = new SearchPortsQuery(dbContext);

        var response = await query.SearchAsync(
            new SearchPortsRequest { Search = $" {search} " },
            CancellationToken.None);

        Assert.Equal(expectedName, Assert.Single(response.Items).Name);
    }

    [Fact]
    public async Task Search_filters_by_country()
    {
        await using var dbContext = CreateDbContext();
        await SeedAsync(
            dbContext,
            Port.Create("Port of Bar", "Montenegro", "Bar"),
            Port.Create("Genoa terminal", "Italy", "Genoa"));
        var query = new SearchPortsQuery(dbContext);

        var response = await query.SearchAsync(
            new SearchPortsRequest { Country = "Italy" },
            CancellationToken.None);

        Assert.Equal("Genoa terminal", Assert.Single(response.Items).Name);
        Assert.Equal(1, response.TotalCount);
    }

    [Fact]
    public async Task Search_pages_through_the_directory()
    {
        await using var dbContext = CreateDbContext();
        await SeedAsync(
            dbContext,
            Port.Create("Port A", "Montenegro"),
            Port.Create("Port B", "Montenegro"),
            Port.Create("Port C", "Montenegro"));
        var query = new SearchPortsQuery(dbContext);

        var response = await query.SearchAsync(
            new SearchPortsRequest { PageNumber = 2, PageSize = 2 },
            CancellationToken.None);

        Assert.Equal(3, response.TotalCount);
        Assert.Equal("Port C", Assert.Single(response.Items).Name);
    }

    [Fact]
    public async Task Search_returns_an_empty_page_when_no_port_is_registered()
    {
        await using var dbContext = CreateDbContext();
        var query = new SearchPortsQuery(dbContext);

        var response = await query.SearchAsync(new SearchPortsRequest(), CancellationToken.None);

        Assert.Empty(response.Items);
        Assert.Equal(0, response.TotalCount);
    }

    [Fact]
    public async Task Search_rejects_a_page_size_above_the_bound()
    {
        await using var dbContext = CreateDbContext();
        var query = new SearchPortsQuery(dbContext);

        var exception = await Assert.ThrowsAsync<SearchPortsValidationException>(
            () => query.SearchAsync(
                new SearchPortsRequest { PageSize = SearchPortsValidator.MaxPageSize + 1 },
                CancellationToken.None));

        Assert.Contains(SearchPortsValidator.PageSizeField, exception.Errors.Keys, StringComparer.Ordinal);
    }

    private static async Task SeedAsync(HarborOpsDbContext dbContext, params Port[] ports)
    {
        await dbContext.Set<Port>().AddRangeAsync(ports, CancellationToken.None);
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
