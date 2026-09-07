using HarborOps.Infrastructure.Persistence;
using HarborOps.Modules.Crew.Application;
using HarborOps.Modules.Crew.Contracts;
using HarborOps.Modules.Crew.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.UnitTests.Crew;

public sealed class SearchCrewPositionsQueryTests
{
    [Fact]
    public async Task SearchAsync_orders_by_department_then_rank_name()
    {
        await using var dbContext = CreateDbContext();
        await SeedCrewPositionAsync(dbContext, "Second Officer", CrewPositionDepartments.Deck);
        await SeedCrewPositionAsync(dbContext, "Bosun", CrewPositionDepartments.Deck);
        await SeedCrewPositionAsync(dbContext, "Chief Engineer", CrewPositionDepartments.Engine);
        var query = new SearchCrewPositionsQuery(dbContext);

        var response = await query.SearchAsync(new SearchCrewPositionsRequest(), CancellationToken.None);

        Assert.Equal(3, response.Items.Count);
        Assert.Equal("Bosun", response.Items[0].RankName);
        Assert.Equal("Second Officer", response.Items[1].RankName);
        Assert.Equal("Chief Engineer", response.Items[2].RankName);
    }

    [Fact]
    public async Task SearchAsync_filters_by_search_term()
    {
        await using var dbContext = CreateDbContext();
        await SeedCrewPositionAsync(dbContext, "Bosun", CrewPositionDepartments.Deck);
        await SeedCrewPositionAsync(dbContext, "Chief Engineer", CrewPositionDepartments.Engine);
        var query = new SearchCrewPositionsQuery(dbContext);

        var response = await query.SearchAsync(
            new SearchCrewPositionsRequest { Search = "Bos" },
            CancellationToken.None);

        Assert.Single(response.Items);
        Assert.Equal("Bosun", response.Items[0].RankName);
    }

    [Fact]
    public async Task SearchAsync_filters_by_department()
    {
        await using var dbContext = CreateDbContext();
        await SeedCrewPositionAsync(dbContext, "Bosun", CrewPositionDepartments.Deck);
        await SeedCrewPositionAsync(dbContext, "Chief Engineer", CrewPositionDepartments.Engine);
        var query = new SearchCrewPositionsQuery(dbContext);

        var response = await query.SearchAsync(
            new SearchCrewPositionsRequest { Department = CrewPositionDepartments.Engine },
            CancellationToken.None);

        Assert.Single(response.Items);
        Assert.Equal("Chief Engineer", response.Items[0].RankName);
    }

    [Fact]
    public async Task SearchAsync_projects_crew_member_and_assignment_usage_counts()
    {
        await using var dbContext = CreateDbContext();
        var positionId = await SeedCrewPositionAsync(dbContext, "Bosun", CrewPositionDepartments.Deck);
        await SeedCrewMemberAsync(dbContext, positionId);
        await SeedCrewMemberAsync(dbContext, positionId);
        await SeedCrewAssignmentAsync(dbContext, positionId);
        var query = new SearchCrewPositionsQuery(dbContext);

        var response = await query.SearchAsync(new SearchCrewPositionsRequest(), CancellationToken.None);

        var item = Assert.Single(response.Items);
        Assert.Equal(2, item.CrewMemberUsageCount);
        Assert.Equal(1, item.AssignmentUsageCount);
    }

    [Fact]
    public async Task SearchAsync_respects_page_size_and_page_number()
    {
        await using var dbContext = CreateDbContext();
        await SeedCrewPositionAsync(dbContext, "Bosun", CrewPositionDepartments.Deck);
        await SeedCrewPositionAsync(dbContext, "Chief Engineer", CrewPositionDepartments.Engine);
        await SeedCrewPositionAsync(dbContext, "Second Officer", CrewPositionDepartments.Deck);
        var query = new SearchCrewPositionsQuery(dbContext);

        var firstPage = await query.SearchAsync(
            new SearchCrewPositionsRequest { PageSize = 2, PageNumber = 1 },
            CancellationToken.None);
        var secondPage = await query.SearchAsync(
            new SearchCrewPositionsRequest { PageSize = 2, PageNumber = 2 },
            CancellationToken.None);

        Assert.Equal(2, firstPage.Items.Count);
        Assert.Equal(3, firstPage.TotalCount);
        Assert.Equal(1, firstPage.PageNumber);
        Assert.Single(secondPage.Items);
        Assert.Equal(2, secondPage.PageNumber);
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

    [Fact]
    public async Task SearchAsync_rejects_a_page_number_below_one()
    {
        await using var dbContext = CreateDbContext();
        var query = new SearchCrewPositionsQuery(dbContext);

        var exception = await Assert.ThrowsAsync<SearchCrewPositionsValidationException>(
            () => query.SearchAsync(
                new SearchCrewPositionsRequest { PageNumber = 0 },
                CancellationToken.None));

        Assert.Contains(SearchCrewPositionsValidator.PageNumberField, exception.Errors);
    }

    private static async Task<Guid> SeedCrewPositionAsync(HarborOpsDbContext dbContext, string rankName, string department)
    {
        var position = CrewPosition.Create(rankName, department, CrewPositionSeniorityLevels.SeniorRating);
        await dbContext.Set<CrewPosition>().AddAsync(position);
        await dbContext.SaveChangesAsync();

        return position.Id;
    }

    private static async Task SeedCrewMemberAsync(HarborOpsDbContext dbContext, Guid rankId)
    {
        var member = CrewMember.Create(
            rankId,
            "Ana",
            "Petrović",
            new DateOnly(1990, 1, 1),
            "Montenegro",
            $"IMO{Guid.NewGuid():N}"[..10],
            "P1234567",
            new DateOnly(2030, 1, 1),
            "CDC1234567",
            new DateOnly(2020, 1, 1),
            "Active");

        await dbContext.Set<CrewMember>().AddAsync(member);
        await dbContext.SaveChangesAsync();
    }

    private static async Task SeedCrewAssignmentAsync(HarborOpsDbContext dbContext, Guid rankId)
    {
        var assignment = CrewAssignment.CreatePlanned(
            Guid.NewGuid(), Guid.NewGuid(), rankId, null, new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31));

        await dbContext.Set<CrewAssignment>().AddAsync(assignment);
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
