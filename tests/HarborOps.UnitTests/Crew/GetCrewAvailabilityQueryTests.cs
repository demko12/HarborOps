using HarborOps.Modules.Crew.Application;
using HarborOps.Modules.Crew.Contracts;
using HarborOps.Modules.Crew.Domain;

namespace HarborOps.UnitTests.Crew;

public sealed class GetCrewAvailabilityQueryTests
{
    [Fact]
    public async Task GetAsync_filters_by_crew_member_type_status_and_date_window()
    {
        await using var dbContext = CrewAvailabilityTestData.CreateDbContext();
        var member = await CrewAvailabilityTestData.SeedMemberAsync(dbContext);
        var other = await CrewAvailabilityTestData.SeedMemberAsync(dbContext);

        var match = await CrewAvailabilityTestData.SeedRequestAsync(
            dbContext, member.Id, new DateOnly(2026, 8, 10), new DateOnly(2026, 8, 20), CrewAvailabilityRequest.ApprovedStatus, "SickLeave");
        await CrewAvailabilityTestData.SeedRequestAsync(
            dbContext, member.Id, new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 5), CrewAvailabilityRequest.ApprovedStatus, "AnnualLeave");
        await CrewAvailabilityTestData.SeedRequestAsync(
            dbContext, other.Id, new DateOnly(2026, 8, 10), new DateOnly(2026, 8, 20), CrewAvailabilityRequest.ApprovedStatus, "SickLeave");

        var query = new GetCrewAvailabilityQuery(dbContext);

        var response = await query.GetAsync(
            new GetCrewAvailabilityRequest
            {
                CrewMemberId = member.Id,
                Type = "SickLeave",
                Status = CrewAvailabilityRequest.ApprovedStatus,
                From = new DateOnly(2026, 8, 1),
                To = new DateOnly(2026, 8, 31),
            },
            CancellationToken.None);

        var item = Assert.Single(response.Items);
        Assert.Equal(match.Id, item.Id);
        Assert.Equal("Seeded private notes.", item.Notes);
    }

    [Fact]
    public async Task GetAsync_orders_by_start_date_then_id_and_pages()
    {
        await using var dbContext = CrewAvailabilityTestData.CreateDbContext();
        var member = await CrewAvailabilityTestData.SeedMemberAsync(dbContext);
        await CrewAvailabilityTestData.SeedRequestAsync(dbContext, member.Id, new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 3));
        await CrewAvailabilityTestData.SeedRequestAsync(dbContext, member.Id, new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 3));
        await CrewAvailabilityTestData.SeedRequestAsync(dbContext, member.Id, new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 3));

        var query = new GetCrewAvailabilityQuery(dbContext);

        var page1 = await query.GetAsync(new GetCrewAvailabilityRequest { PageNumber = 1, PageSize = 2 }, CancellationToken.None);
        var page2 = await query.GetAsync(new GetCrewAvailabilityRequest { PageNumber = 2, PageSize = 2 }, CancellationToken.None);

        Assert.Equal(3, page1.TotalCount);
        Assert.Equal(new DateOnly(2026, 7, 1), page1.Items[0].StartDate);
        Assert.Equal(new DateOnly(2026, 8, 1), page1.Items[1].StartDate);
        Assert.Equal(new DateOnly(2026, 9, 1), Assert.Single(page2.Items).StartDate);
    }

    [Fact]
    public async Task GetAsync_rejects_a_reversed_date_window()
    {
        await using var dbContext = CrewAvailabilityTestData.CreateDbContext();
        var query = new GetCrewAvailabilityQuery(dbContext);

        await Assert.ThrowsAsync<GetCrewAvailabilityValidationException>(
            () => query.GetAsync(
                new GetCrewAvailabilityRequest { From = new DateOnly(2026, 9, 1), To = new DateOnly(2026, 8, 1) },
                CancellationToken.None));
    }
}
