using HarborOps.Infrastructure.Persistence;
using HarborOps.Modules.Voyages.Application;
using HarborOps.Modules.Voyages.Contracts;
using HarborOps.Modules.Voyages.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.UnitTests.Voyages;

public sealed class SearchRoutesQueryTests
{
    [Fact]
    public async Task Search_orders_routes_by_name_and_counts_their_stops()
    {
        await using var dbContext = CreateDbContext();
        await SeedRouteAsync(dbContext, "Tyrrhenian loop", 900m, 3000, 2);
        await SeedRouteAsync(dbContext, "Adriatic feeder", 560m, 2100, 3);
        var query = new SearchRoutesQuery(dbContext);

        var response = await query.SearchAsync(new SearchRoutesRequest(), CancellationToken.None);

        Assert.Equal(2, response.TotalCount);
        Assert.Equal(SearchRoutesValidator.DefaultPageNumber, response.PageNumber);
        Assert.Equal(
            ["Adriatic feeder", "Tyrrhenian loop"],
            response.Items.Select(item => item.Name));

        var adriatic = response.Items[0];
        Assert.Equal(560m, adriatic.EstimatedDistance);
        Assert.Equal(2100, adriatic.EstimatedDuration);
        Assert.Equal(3, adriatic.StopCount);
        Assert.Equal(2, response.Items[1].StopCount);
    }

    [Fact]
    public async Task Search_filters_by_route_name()
    {
        await using var dbContext = CreateDbContext();
        await SeedRouteAsync(dbContext, "Adriatic feeder", 560m, 2100, 2);
        await SeedRouteAsync(dbContext, "Tyrrhenian loop", 900m, 3000, 2);
        var query = new SearchRoutesQuery(dbContext);

        var response = await query.SearchAsync(
            new SearchRoutesRequest { Search = " Adriatic " },
            CancellationToken.None);

        Assert.Equal("Adriatic feeder", Assert.Single(response.Items).Name);
        Assert.Equal(1, response.TotalCount);
    }

    [Fact]
    public async Task Search_pages_through_the_routes()
    {
        await using var dbContext = CreateDbContext();
        await SeedRouteAsync(dbContext, "Route A", 100m, 200, 2);
        await SeedRouteAsync(dbContext, "Route B", 100m, 200, 2);
        await SeedRouteAsync(dbContext, "Route C", 100m, 200, 2);
        var query = new SearchRoutesQuery(dbContext);

        var response = await query.SearchAsync(
            new SearchRoutesRequest { PageNumber = 2, PageSize = 2 },
            CancellationToken.None);

        Assert.Equal(3, response.TotalCount);
        Assert.Equal("Route C", Assert.Single(response.Items).Name);
    }

    [Fact]
    public async Task Search_returns_an_empty_page_when_no_route_is_registered()
    {
        await using var dbContext = CreateDbContext();
        var query = new SearchRoutesQuery(dbContext);

        var response = await query.SearchAsync(new SearchRoutesRequest(), CancellationToken.None);

        Assert.Empty(response.Items);
        Assert.Equal(0, response.TotalCount);
    }

    [Fact]
    public async Task Search_rejects_a_page_number_below_one()
    {
        await using var dbContext = CreateDbContext();
        var query = new SearchRoutesQuery(dbContext);

        var exception = await Assert.ThrowsAsync<SearchRoutesValidationException>(
            () => query.SearchAsync(
                new SearchRoutesRequest { PageNumber = 0 },
                CancellationToken.None));

        Assert.Contains(SearchRoutesValidator.PageNumberField, exception.Errors.Keys, StringComparer.Ordinal);
    }

    private static async Task SeedRouteAsync(
        HarborOpsDbContext dbContext,
        string name,
        decimal estimatedDistance,
        int estimatedDuration,
        int stopCount)
    {
        var route = Route.Create(name, estimatedDistance, estimatedDuration);
        await dbContext.Set<Route>().AddAsync(route, CancellationToken.None);

        for (var stopOrder = 1; stopOrder <= stopCount; stopOrder++)
        {
            var port = Port.Create($"{name} stop {stopOrder}", "Montenegro");
            await dbContext.Set<Port>().AddAsync(port, CancellationToken.None);
            await dbContext.Set<RoutePort>().AddAsync(
                RoutePort.Create(
                    route.Id,
                    port.Id,
                    stopOrder,
                    stopOrder == 1 ? 0m : 10m,
                    stopOrder == 1 ? 0 : 60),
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
