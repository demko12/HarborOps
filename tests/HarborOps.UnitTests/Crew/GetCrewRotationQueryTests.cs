using HarborOps.Infrastructure.Persistence;
using HarborOps.Modules.Crew.Application;
using HarborOps.Modules.Crew.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.UnitTests.Crew;

public sealed class GetCrewRotationQueryTests
{
    private static readonly DateTime Today = new(2026, 6, 15, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task GetAsync_throws_when_assignment_not_found()
    {
        await using var dbContext = CreateDbContext();
        var query = new GetCrewRotationQuery(dbContext, new FakeDateTimeProvider(Today));

        var exception = await Assert.ThrowsAsync<CrewAssignmentNotFoundException>(
            () => query.GetAsync(Guid.NewGuid(), CancellationToken.None));

        Assert.NotEqual(Guid.Empty, exception.AssignmentId);
    }

    [Fact]
    public async Task GetAsync_throws_when_no_rotation_is_planned()
    {
        await using var dbContext = CreateDbContext();
        var (assignment, _) = await SeedAssignmentAsync(dbContext);
        var query = new GetCrewRotationQuery(dbContext, new FakeDateTimeProvider(Today));

        var exception = await Assert.ThrowsAsync<CrewRotationNotFoundException>(
            () => query.GetAsync(assignment.Id, CancellationToken.None));

        Assert.Equal(assignment.Id, exception.AssignmentId);
    }

    [Fact]
    public async Task GetAsync_returns_the_planned_rotation()
    {
        await using var dbContext = CreateDbContext();
        var (assignment, rankId) = await SeedAssignmentAsync(dbContext);
        var rotation = CrewRotation.Create(assignment.Id, null, "2-2", new DateOnly(2026, 7, 1));
        await dbContext.Set<CrewRotation>().AddAsync(rotation);
        await dbContext.SaveChangesAsync();

        var query = new GetCrewRotationQuery(dbContext, new FakeDateTimeProvider(Today));

        var item = await query.GetAsync(assignment.Id, CancellationToken.None);

        Assert.Equal(rotation.Id, item.RotationId);
        Assert.Equal(assignment.Id, item.AssignmentId);
        Assert.Equal(rankId, item.RankId);
        Assert.Equal("2-2", item.RotationPattern);
        Assert.Equal(new DateOnly(2026, 7, 1), item.PlannedReliefDate);
        Assert.False(item.IsOverdue);
    }

    [Fact]
    public async Task GetAsync_marks_a_past_relief_date_as_overdue()
    {
        await using var dbContext = CreateDbContext();
        var (assignment, _) = await SeedAssignmentAsync(dbContext);
        var rotation = CrewRotation.Create(assignment.Id, null, "2-2", new DateOnly(2026, 5, 1));
        await dbContext.Set<CrewRotation>().AddAsync(rotation);
        await dbContext.SaveChangesAsync();

        var query = new GetCrewRotationQuery(dbContext, new FakeDateTimeProvider(Today));

        var item = await query.GetAsync(assignment.Id, CancellationToken.None);

        Assert.True(item.IsOverdue);
    }

    private static async Task<(CrewAssignment Assignment, Guid RankId)> SeedAssignmentAsync(HarborOpsDbContext dbContext)
    {
        var position = CrewPosition.Create("Able Seafarer", "Deck", "Senior");
        await dbContext.Set<CrewPosition>().AddAsync(position);

        var member = CrewMember.Create(
            position.Id, "Marko Petrovic", new DateOnly(1990, 1, 1), "Montenegro",
            "IMO00001", "P1234567", new DateOnly(2030, 1, 1), "CDC1234567",
            new DateOnly(2020, 1, 1), "Active");
        await dbContext.Set<CrewMember>().AddAsync(member);

        var assignment = CrewAssignment.CreatePlanned(
            member.Id, Guid.NewGuid(), position.Id, null, new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31));
        await dbContext.Set<CrewAssignment>().AddAsync(assignment);

        await dbContext.SaveChangesAsync();

        return (assignment, position.Id);
    }

    private static HarborOpsDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<HarborOpsDbContext>()
            .UseInMemoryDatabase($"HarborOpsUnitTests-{Guid.NewGuid()}")
            .Options;

        return new HarborOpsDbContext(options);
    }
}
