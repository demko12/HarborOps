using HarborOps.Infrastructure.Persistence;
using HarborOps.Modules.Crew.Application;
using HarborOps.Modules.Crew.Contracts;
using HarborOps.Modules.Crew.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.UnitTests.Crew;

public sealed class SearchCrewPositionsQueryTests
{
    [Fact]
    public async Task SearchAsync_returns_positions_ordered_by_rank_name()
    {
        await using var dbContext = CreateDbContext();
        await SeedCrewPositionAsync(dbContext, "Second Officer");
        await SeedCrewPositionAsync(dbContext, "Bosun");
        var query = new SearchCrewPositionsQuery(dbContext);

        var response = await query.SearchAsync(new SearchCrewPositionsRequest(), CancellationToken.None);

        Assert.Equal(2, response.Items.Count);
        Assert.Equal("Bosun", response.Items[0].RankName);
        Assert.Equal("Second Officer", response.Items[1].RankName);
    }

    [Fact]
    public async Task SearchAsync_filters_by_search_term()
    {
        await using var dbContext = CreateDbContext();
        await SeedCrewPositionAsync(dbContext, "Bosun");
        await SeedCrewPositionAsync(dbContext, "Chief Engineer");
        var query = new SearchCrewPositionsQuery(dbContext);

        var response = await query.SearchAsync(
            new SearchCrewPositionsRequest { Search = "Bos" },
            CancellationToken.None);

        Assert.Single(response.Items);
        Assert.Equal("Bosun", response.Items[0].RankName);
    }

    [Fact]
    public async Task SearchAsync_respects_page_size()
    {
        await using var dbContext = CreateDbContext();
        await SeedCrewPositionAsync(dbContext, "Bosun");
        await SeedCrewPositionAsync(dbContext, "Chief Engineer");
        await SeedCrewPositionAsync(dbContext, "Second Officer");
        var query = new SearchCrewPositionsQuery(dbContext);

        var response = await query.SearchAsync(
            new SearchCrewPositionsRequest { PageSize = 2 },
            CancellationToken.None);

        Assert.Equal(2, response.Items.Count);
    }

    [Fact]
    public async Task SearchAsync_rejects_page_size_above_maximum()
    {
        await using var dbContext = CreateDbContext();
        var query = new SearchCrewPositionsQuery(dbContext);

        var exception = await Assert.ThrowsAsync<SearchCrewPositionsValidationException>(
            () => query.SearchAsync(
                new SearchCrewPositionsRequest { PageSize = 101 },
                CancellationToken.None));

        Assert.Contains(SearchCrewPositionsValidator.PageSizeField, exception.Errors);
    }

    private static async Task SeedCrewPositionAsync(HarborOpsDbContext dbContext, string rankName)
    {
        var position = CrewPosition.Create(rankName, "Deck", "Senior");
        await dbContext.Set<CrewPosition>().AddAsync(position);
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
