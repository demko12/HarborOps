using HarborOps.Infrastructure.Persistence;
using HarborOps.Modules.Crew.Application;
using HarborOps.Modules.Crew.Contracts;
using HarborOps.Modules.Crew.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.UnitTests.Crew;

public sealed class SearchCrewMembersQueryTests
{
    [Fact]
    public async Task Search_returns_first_page_using_default_paging()
    {
        await using var dbContext = CreateDbContext();
        var rankId = await SeedCrewPositionAsync(dbContext, "Able Seafarer");

        for (var index = 1; index <= 26; index++)
        {
            await SeedCrewMemberAsync(dbContext, rankId, $"Member {index:D2}", $"IMO{index:D5}");
        }

        var query = new SearchCrewMembersQuery(dbContext);

        var response = await query.SearchAsync(
            new SearchCrewMembersRequest(),
            CancellationToken.None);

        Assert.Equal(1, response.PageNumber);
        Assert.Equal(25, response.PageSize);
        Assert.Equal(26, response.TotalCount);
        Assert.Equal(25, response.Items.Count);
        Assert.Equal("Member 01", response.Items[0].Name);
        Assert.Equal("Member 25", response.Items[^1].Name);
    }

    [Fact]
    public async Task Search_orders_by_passport_expiry_then_name_and_id()
    {
        await using var dbContext = CreateDbContext();
        var rankId = await SeedCrewPositionAsync(dbContext, "Able Seafarer");
        var firstMarko = await SeedCrewMemberAsync(dbContext, rankId, "Marko Petrovic", "IMO00001");
        var secondMarko = await SeedCrewMemberAsync(dbContext, rankId, "Marko Petrovic", "IMO00002");
        await SeedCrewMemberAsync(dbContext, rankId, "Nikola Jovanovic", "IMO00003");

        var query = new SearchCrewMembersQuery(dbContext);

        var response = await query.SearchAsync(
            new SearchCrewMembersRequest(),
            CancellationToken.None);

        var expectedMarkoIds = new[] { firstMarko.Id, secondMarko.Id }
            .OrderBy(id => id)
            .ToArray();

        Assert.Equal(3, response.Items.Count);
        Assert.Equal(expectedMarkoIds[0], response.Items[0].CrewMemberId);
        Assert.Equal(expectedMarkoIds[1], response.Items[1].CrewMemberId);
        Assert.Equal("Nikola Jovanovic", response.Items[2].Name);
    }

    [Fact]
    public async Task Search_prioritizes_the_earliest_passport_expiry()
    {
        await using var dbContext = CreateDbContext();
        var rankId = await SeedCrewPositionAsync(dbContext, "Able Seafarer");
        await SeedCrewMemberAsync(dbContext, rankId, "Zoran Later", "IMO00001", passportExpiry: new DateOnly(2030, 1, 1));
        await SeedCrewMemberAsync(dbContext, rankId, "Ana Expiring", "IMO00002", passportExpiry: new DateOnly(2026, 9, 1));
        await SeedCrewMemberAsync(dbContext, rankId, "Marko Soon", "IMO00003", passportExpiry: new DateOnly(2027, 1, 1));

        var response = await new SearchCrewMembersQuery(dbContext).SearchAsync(new SearchCrewMembersRequest(), CancellationToken.None);

        Assert.Equal(["Ana Expiring", "Marko Soon", "Zoran Later"], response.Items.Select(item => item.Name));
    }

    [Fact]
    public async Task Search_matches_a_partial_name()
    {
        await using var dbContext = CreateDbContext();
        var rankId = await SeedCrewPositionAsync(dbContext, "Able Seafarer");
        await SeedCrewMemberAsync(dbContext, rankId, "Marko Petrovic", "IMO00001");
        await SeedCrewMemberAsync(dbContext, rankId, "Nikola Jovanovic", "IMO00002");

        var query = new SearchCrewMembersQuery(dbContext);

        var response = await query.SearchAsync(
            new SearchCrewMembersRequest { Search = "Petrovic" },
            CancellationToken.None);

        Assert.Equal(1, response.TotalCount);
        Assert.Equal("Marko Petrovic", Assert.Single(response.Items).Name);
    }

    [Fact]
    public async Task Search_matches_the_full_imo_seafarer_id()
    {
        await using var dbContext = CreateDbContext();
        var rankId = await SeedCrewPositionAsync(dbContext, "Able Seafarer");
        await SeedCrewMemberAsync(dbContext, rankId, "Marko Petrovic", "IMO00001");
        await SeedCrewMemberAsync(dbContext, rankId, "Nikola Jovanovic", "IMO99999");

        var query = new SearchCrewMembersQuery(dbContext);

        var response = await query.SearchAsync(
            new SearchCrewMembersRequest { Search = "IMO99999" },
            CancellationToken.None);

        Assert.Equal(1, response.TotalCount);
        Assert.Equal("Nikola Jovanovic", Assert.Single(response.Items).Name);
    }

    [Fact]
    public async Task Search_filters_by_employment_status()
    {
        await using var dbContext = CreateDbContext();
        var rankId = await SeedCrewPositionAsync(dbContext, "Able Seafarer");
        await SeedCrewMemberAsync(dbContext, rankId, "Marko Petrovic", "IMO00001", employmentStatus: "Active");
        await SeedCrewMemberAsync(dbContext, rankId, "Nikola Jovanovic", "IMO00002", employmentStatus: "Inactive");
        await SeedCrewMemberAsync(dbContext, rankId, "Ana Radovic", "IMO00003", employmentStatus: "Inactive");

        var query = new SearchCrewMembersQuery(dbContext);

        var response = await query.SearchAsync(
            new SearchCrewMembersRequest { EmploymentStatus = "Inactive" },
            CancellationToken.None);

        Assert.Equal(2, response.TotalCount);
        Assert.All(response.Items, item => Assert.Equal("Inactive", item.EmploymentStatus));
    }

    [Fact]
    public async Task Search_combines_the_search_term_with_the_employment_status_filter()
    {
        await using var dbContext = CreateDbContext();
        var rankId = await SeedCrewPositionAsync(dbContext, "Able Seafarer");
        await SeedCrewMemberAsync(dbContext, rankId, "Marko Petrovic", "IMO00001", employmentStatus: "Active");
        await SeedCrewMemberAsync(dbContext, rankId, "Marko Radovic", "IMO00002", employmentStatus: "Inactive");

        var query = new SearchCrewMembersQuery(dbContext);

        var response = await query.SearchAsync(
            new SearchCrewMembersRequest { Search = "Marko", EmploymentStatus = "Active" },
            CancellationToken.None);

        Assert.Equal(1, response.TotalCount);
        Assert.Equal("Marko Petrovic", Assert.Single(response.Items).Name);
    }

    [Fact]
    public async Task Search_projects_the_base_rank_name_from_the_crew_position()
    {
        await using var dbContext = CreateDbContext();
        var rankId = await SeedCrewPositionAsync(dbContext, "Chief Engineer");
        var member = await SeedCrewMemberAsync(
            dbContext,
            rankId,
            "Marko Petrovic",
            "IMO00001",
            passportExpiry: new DateOnly(2027, 5, 20),
            hireDate: new DateOnly(2021, 3, 1));

        var query = new SearchCrewMembersQuery(dbContext);

        var response = await query.SearchAsync(
            new SearchCrewMembersRequest(),
            CancellationToken.None);

        var item = Assert.Single(response.Items);

        Assert.Equal(member.Id, item.CrewMemberId);
        Assert.Equal("Marko Petrovic", item.Name);
        Assert.Equal("Montenegro", item.Nationality);
        Assert.Equal("IMO00001", item.ImoSeafarerId);
        Assert.Equal(rankId, item.BaseRankId);
        Assert.Equal("Chief Engineer", item.BaseRankName);
        Assert.Equal("Active", item.EmploymentStatus);
        Assert.Equal(new DateOnly(2027, 5, 20), item.PassportExpiry);
        Assert.Equal(new DateOnly(2021, 3, 1), item.HireDate);
    }

    [Fact]
    public async Task Search_projects_the_rank_each_member_actually_holds()
    {
        await using var dbContext = CreateDbContext();
        var engineerRankId = await SeedCrewPositionAsync(dbContext, "Chief Engineer");
        var deckRankId = await SeedCrewPositionAsync(dbContext, "Able Seafarer");
        await SeedCrewMemberAsync(dbContext, engineerRankId, "Marko Petrovic", "IMO00001");
        await SeedCrewMemberAsync(dbContext, deckRankId, "Nikola Jovanovic", "IMO00002");

        var query = new SearchCrewMembersQuery(dbContext);

        var response = await query.SearchAsync(
            new SearchCrewMembersRequest(),
            CancellationToken.None);

        Assert.Equal("Chief Engineer", response.Items[0].BaseRankName);
        Assert.Equal("Able Seafarer", response.Items[1].BaseRankName);
    }

    [Fact]
    public async Task Search_excludes_members_whose_rank_is_missing()
    {
        await using var dbContext = CreateDbContext();
        var rankId = await SeedCrewPositionAsync(dbContext, "Able Seafarer");
        await SeedCrewMemberAsync(dbContext, rankId, "Marko Petrovic", "IMO00001");
        await SeedCrewMemberAsync(dbContext, Guid.NewGuid(), "Nikola Jovanovic", "IMO00002");

        var query = new SearchCrewMembersQuery(dbContext);

        var response = await query.SearchAsync(
            new SearchCrewMembersRequest(),
            CancellationToken.None);

        Assert.Equal(1, response.TotalCount);
        Assert.Equal("Marko Petrovic", Assert.Single(response.Items).Name);
    }

    [Fact]
    public async Task Search_returns_the_requested_page()
    {
        await using var dbContext = CreateDbContext();
        var rankId = await SeedCrewPositionAsync(dbContext, "Able Seafarer");

        for (var index = 1; index <= 5; index++)
        {
            await SeedCrewMemberAsync(dbContext, rankId, $"Member {index:D2}", $"IMO{index:D5}");
        }

        var query = new SearchCrewMembersQuery(dbContext);

        var response = await query.SearchAsync(
            new SearchCrewMembersRequest { PageNumber = 2, PageSize = 2 },
            CancellationToken.None);

        Assert.Equal(2, response.PageNumber);
        Assert.Equal(2, response.PageSize);
        Assert.Equal(5, response.TotalCount);
        Assert.Equal(["Member 03", "Member 04"], response.Items.Select(item => item.Name));
    }

    [Fact]
    public async Task Search_returns_no_items_for_a_page_beyond_the_result_set()
    {
        await using var dbContext = CreateDbContext();
        var rankId = await SeedCrewPositionAsync(dbContext, "Able Seafarer");
        await SeedCrewMemberAsync(dbContext, rankId, "Marko Petrovic", "IMO00001");

        var query = new SearchCrewMembersQuery(dbContext);

        var response = await query.SearchAsync(
            new SearchCrewMembersRequest { PageNumber = 4, PageSize = 10 },
            CancellationToken.None);

        Assert.Equal(1, response.TotalCount);
        Assert.Empty(response.Items);
    }

    [Fact]
    public async Task Search_returns_an_empty_page_when_nothing_matches()
    {
        await using var dbContext = CreateDbContext();
        var rankId = await SeedCrewPositionAsync(dbContext, "Able Seafarer");
        await SeedCrewMemberAsync(dbContext, rankId, "Marko Petrovic", "IMO00001");

        var query = new SearchCrewMembersQuery(dbContext);

        var response = await query.SearchAsync(
            new SearchCrewMembersRequest { Search = "Nothing matches this" },
            CancellationToken.None);

        Assert.Equal(0, response.TotalCount);
        Assert.Empty(response.Items);
        Assert.Equal(1, response.PageNumber);
    }

    [Fact]
    public async Task Search_returns_an_empty_page_when_no_crew_is_registered()
    {
        await using var dbContext = CreateDbContext();
        var query = new SearchCrewMembersQuery(dbContext);

        var response = await query.SearchAsync(
            new SearchCrewMembersRequest(),
            CancellationToken.None);

        Assert.Equal(0, response.TotalCount);
        Assert.Empty(response.Items);
    }

    [Fact]
    public async Task Search_rejects_page_number_below_one()
    {
        await using var dbContext = CreateDbContext();
        var query = new SearchCrewMembersQuery(dbContext);

        var exception = await Assert.ThrowsAsync<SearchCrewMembersValidationException>(
            () => query.SearchAsync(
                new SearchCrewMembersRequest { PageNumber = 0 },
                CancellationToken.None));

        Assert.Contains(SearchCrewMembersValidator.PageNumberField, exception.Errors);
    }

    [Fact]
    public async Task Search_rejects_page_size_below_one()
    {
        await using var dbContext = CreateDbContext();
        var query = new SearchCrewMembersQuery(dbContext);

        var exception = await Assert.ThrowsAsync<SearchCrewMembersValidationException>(
            () => query.SearchAsync(
                new SearchCrewMembersRequest { PageSize = 0 },
                CancellationToken.None));

        Assert.Contains(SearchCrewMembersValidator.PageSizeField, exception.Errors);
    }

    [Fact]
    public async Task Search_rejects_page_size_above_maximum()
    {
        await using var dbContext = CreateDbContext();
        var query = new SearchCrewMembersQuery(dbContext);

        var exception = await Assert.ThrowsAsync<SearchCrewMembersValidationException>(
            () => query.SearchAsync(
                new SearchCrewMembersRequest { PageSize = SearchCrewMembersValidator.MaxPageSize + 1 },
                CancellationToken.None));

        Assert.Contains(SearchCrewMembersValidator.PageSizeField, exception.Errors);
    }

    [Fact]
    public async Task Search_accepts_maximum_page_size()
    {
        await using var dbContext = CreateDbContext();
        var rankId = await SeedCrewPositionAsync(dbContext, "Able Seafarer");
        await SeedCrewMemberAsync(dbContext, rankId, "Marko Petrovic", "IMO00001");

        var query = new SearchCrewMembersQuery(dbContext);

        var response = await query.SearchAsync(
            new SearchCrewMembersRequest { PageSize = SearchCrewMembersValidator.MaxPageSize },
            CancellationToken.None);

        Assert.Equal(SearchCrewMembersValidator.MaxPageSize, response.PageSize);
        Assert.Single(response.Items);
    }

    private static async Task<Guid> SeedCrewPositionAsync(HarborOpsDbContext dbContext, string rankName)
    {
        var position = CrewPosition.Create(rankName, "Deck", "Senior");

        await dbContext.Set<CrewPosition>().AddAsync(position);
        await dbContext.SaveChangesAsync();

        return position.Id;
    }

    private static async Task<CrewMember> SeedCrewMemberAsync(
        HarborOpsDbContext dbContext,
        Guid baseRankId,
        string name,
        string imoSeafarerId,
        string employmentStatus = "Active",
        DateOnly? passportExpiry = null,
        DateOnly? hireDate = null)
    {
        var member = CrewMember.Create(
            baseRankId,
            name,
            new DateOnly(1990, 1, 1),
            "Montenegro",
            imoSeafarerId,
            "P1234567",
            passportExpiry ?? new DateOnly(2030, 1, 1),
            "CDC1234567",
            hireDate ?? new DateOnly(2020, 1, 1),
            employmentStatus);

        await dbContext.Set<CrewMember>().AddAsync(member);
        await dbContext.SaveChangesAsync();

        return member;
    }

    private static HarborOpsDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<HarborOpsDbContext>()
            .UseInMemoryDatabase($"HarborOpsUnitTests-{Guid.NewGuid()}")
            .Options;

        return new HarborOpsDbContext(options);
    }
}
