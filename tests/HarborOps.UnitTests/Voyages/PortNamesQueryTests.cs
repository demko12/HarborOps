using HarborOps.Infrastructure.Persistence;
using HarborOps.Modules.Voyages.Application;
using HarborOps.Modules.Voyages.Contracts;
using HarborOps.Modules.Voyages.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.UnitTests.Voyages;

public sealed class PortNamesQueryTests
{
    [Fact]
    public async Task ResolveAsync_returns_only_the_requested_ports()
    {
        await using var dbContext = CreateDbContext();
        var wanted = Port.Create("Rotterdam", "Netherlands");
        await SeedAsync(dbContext, wanted, Port.Create("Hamburg", "Germany"));

        var response = await new PortNamesQuery(dbContext).ResolveAsync(
            new PortNamesRequest { PortIds = [wanted.Id] },
            CancellationToken.None);

        var item = Assert.Single(response.Ports);

        Assert.Equal(wanted.Id, item.PortId);
        Assert.Equal("Rotterdam", item.Name);
        Assert.Equal("Netherlands", item.Country);
    }

    [Fact]
    public async Task ResolveAsync_ignores_an_identifier_that_no_longer_exists()
    {
        await using var dbContext = CreateDbContext();
        var wanted = Port.Create("Rotterdam", "Netherlands");
        await SeedAsync(dbContext, wanted);

        var response = await new PortNamesQuery(dbContext).ResolveAsync(
            new PortNamesRequest { PortIds = [wanted.Id, Guid.NewGuid()] },
            CancellationToken.None);

        Assert.Single(response.Ports);
    }

    [Fact]
    public async Task ResolveAsync_rejects_an_empty_request()
    {
        await using var dbContext = CreateDbContext();

        var exception = await Assert.ThrowsAsync<PortNamesValidationException>(
            () => new PortNamesQuery(dbContext).ResolveAsync(
                new PortNamesRequest { PortIds = [] },
                CancellationToken.None));

        Assert.True(exception.Errors.ContainsKey(PortNamesValidator.PortIdsField));
    }

    [Fact]
    public async Task ResolveAsync_rejects_more_identifiers_than_the_bound_allows()
    {
        await using var dbContext = CreateDbContext();
        var portIds = Enumerable.Range(0, PortNamesValidator.MaxPortIds + 1)
            .Select(_ => Guid.NewGuid())
            .ToArray();

        var exception = await Assert.ThrowsAsync<PortNamesValidationException>(
            () => new PortNamesQuery(dbContext).ResolveAsync(
                new PortNamesRequest { PortIds = portIds },
                CancellationToken.None));

        Assert.True(exception.Errors.ContainsKey(PortNamesValidator.PortIdsField));
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
