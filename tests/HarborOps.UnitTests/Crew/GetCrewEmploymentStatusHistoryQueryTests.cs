using HarborOps.Infrastructure.Persistence;
using HarborOps.Modules.Crew.Application;
using HarborOps.Modules.Crew.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.UnitTests.Crew;

public sealed class GetCrewEmploymentStatusHistoryQueryTests
{
    [Fact]
    public async Task GetAsync_throws_when_crew_member_not_found()
    {
        await using var dbContext = CreateDbContext();
        var query = new GetCrewEmploymentStatusHistoryQuery(dbContext);

        await Assert.ThrowsAsync<CrewMemberNotFoundException>(
            () => query.GetAsync(Guid.NewGuid(), CancellationToken.None));
    }

    [Fact]
    public async Task GetAsync_returns_an_empty_list_when_no_changes_are_recorded()
    {
        await using var dbContext = CreateDbContext();
        var member = await SeedMemberAsync(dbContext);
        var query = new GetCrewEmploymentStatusHistoryQuery(dbContext);

        var response = await query.GetAsync(member.Id, CancellationToken.None);

        Assert.Empty(response.Items);
    }

    [Fact]
    public async Task GetAsync_orders_entries_newest_first()
    {
        await using var dbContext = CreateDbContext();
        var member = await SeedMemberAsync(dbContext);
        var older = await SeedHistoryAsync(dbContext, member.Id, "OnLeave", new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var newer = await SeedHistoryAsync(dbContext, member.Id, "Active", new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc));
        var query = new GetCrewEmploymentStatusHistoryQuery(dbContext);

        var response = await query.GetAsync(member.Id, CancellationToken.None);

        Assert.Equal(2, response.Items.Count);
        Assert.Equal(newer.Id, response.Items[0].Id);
        Assert.Equal(older.Id, response.Items[1].Id);
    }

    [Fact]
    public async Task GetAsync_projects_actor_notes_and_timestamp()
    {
        await using var dbContext = CreateDbContext();
        var member = await SeedMemberAsync(dbContext);
        var changedByUserId = Guid.NewGuid();
        var changedAtUtc = new DateTime(2026, 6, 1, 12, 30, 0, DateTimeKind.Utc);
        var history = CrewEmploymentStatusHistory.Record(member.Id, "Suspended", changedByUserId, "Pending review", changedAtUtc);
        await dbContext.Set<CrewEmploymentStatusHistory>().AddAsync(history);
        await dbContext.SaveChangesAsync();
        var query = new GetCrewEmploymentStatusHistoryQuery(dbContext);

        var response = await query.GetAsync(member.Id, CancellationToken.None);

        var item = Assert.Single(response.Items);
        Assert.Equal("Suspended", item.Status);
        Assert.Equal(changedByUserId, item.ChangedByUserId);
        Assert.Equal("Pending review", item.Notes);
        Assert.Equal(changedAtUtc, item.ChangedAtUtc);
    }

    private static async Task<CrewEmploymentStatusHistory> SeedHistoryAsync(
        HarborOpsDbContext dbContext, Guid crewMemberId, string status, DateTime changedAtUtc)
    {
        var history = CrewEmploymentStatusHistory.Record(crewMemberId, status, Guid.NewGuid(), null, changedAtUtc);
        await dbContext.Set<CrewEmploymentStatusHistory>().AddAsync(history);
        await dbContext.SaveChangesAsync();

        return history;
    }

    private static async Task<CrewMember> SeedMemberAsync(HarborOpsDbContext dbContext)
    {
        var position = CrewPosition.Create("Bosun", "Deck", "Senior");
        await dbContext.Set<CrewPosition>().AddAsync(position);

        var member = CrewMember.Create(
            position.Id,
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
