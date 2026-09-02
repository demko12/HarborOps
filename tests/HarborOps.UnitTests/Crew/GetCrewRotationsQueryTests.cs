using HarborOps.Infrastructure.Persistence;
using HarborOps.Modules.Crew.Application;
using HarborOps.Modules.Crew.Contracts;
using HarborOps.Modules.Crew.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.UnitTests.Crew;

public sealed class GetCrewRotationsQueryTests
{
    private static readonly DateTime Today = new(2026, 6, 15, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task GetAsync_filters_by_vessel()
    {
        await using var dbContext = CreateDbContext();
        var rankId = await SeedCrewPositionAsync(dbContext);
        var matchingVesselId = Guid.NewGuid();
        var otherVesselId = Guid.NewGuid();
        var matchingAssignment = await SeedAssignmentAsync(dbContext, rankId, matchingVesselId);
        var otherAssignment = await SeedAssignmentAsync(dbContext, rankId, otherVesselId);
        await SeedRotationAsync(dbContext, matchingAssignment.Id, new DateOnly(2026, 7, 1));
        await SeedRotationAsync(dbContext, otherAssignment.Id, new DateOnly(2026, 7, 1));

        var query = new GetCrewRotationsQuery(dbContext, new FakeDateTimeProvider(Today));

        var response = await query.GetAsync(
            new GetCrewRotationsRequest { VesselId = matchingVesselId },
            CancellationToken.None);

        Assert.Equal(1, response.TotalCount);
        Assert.Equal(matchingAssignment.Id, Assert.Single(response.Items).AssignmentId);
    }

    [Fact]
    public async Task GetAsync_filters_by_from_and_to_date()
    {
        await using var dbContext = CreateDbContext();
        var rankId = await SeedCrewPositionAsync(dbContext);
        var early = await SeedAssignmentAsync(dbContext, rankId, Guid.NewGuid());
        var middle = await SeedAssignmentAsync(dbContext, rankId, Guid.NewGuid());
        var late = await SeedAssignmentAsync(dbContext, rankId, Guid.NewGuid());
        await SeedRotationAsync(dbContext, early.Id, new DateOnly(2026, 6, 20));
        await SeedRotationAsync(dbContext, middle.Id, new DateOnly(2026, 7, 10));
        await SeedRotationAsync(dbContext, late.Id, new DateOnly(2026, 8, 1));

        var query = new GetCrewRotationsQuery(dbContext, new FakeDateTimeProvider(Today));

        var response = await query.GetAsync(
            new GetCrewRotationsRequest { From = new DateOnly(2026, 7, 1), To = new DateOnly(2026, 7, 31) },
            CancellationToken.None);

        Assert.Equal(1, response.TotalCount);
        Assert.Equal(middle.Id, Assert.Single(response.Items).AssignmentId);
    }

    [Fact]
    public async Task GetAsync_overdue_only_excludes_future_reliefs_and_closed_assignments()
    {
        await using var dbContext = CreateDbContext();
        var rankId = await SeedCrewPositionAsync(dbContext);
        var overdue = await SeedAssignmentAsync(dbContext, rankId, Guid.NewGuid());
        var future = await SeedAssignmentAsync(dbContext, rankId, Guid.NewGuid());
        var closed = await SeedAssignmentAsync(dbContext, rankId, Guid.NewGuid());
        closed.SignOff(new DateOnly(2026, 6, 1));
        await dbContext.SaveChangesAsync();

        await SeedRotationAsync(dbContext, overdue.Id, new DateOnly(2026, 6, 1));
        await SeedRotationAsync(dbContext, future.Id, new DateOnly(2026, 12, 1));
        await SeedRotationAsync(dbContext, closed.Id, new DateOnly(2026, 5, 1));

        var query = new GetCrewRotationsQuery(dbContext, new FakeDateTimeProvider(Today));

        var response = await query.GetAsync(
            new GetCrewRotationsRequest { OverdueOnly = true },
            CancellationToken.None);

        Assert.Equal(1, response.TotalCount);
        var item = Assert.Single(response.Items);
        Assert.Equal(overdue.Id, item.AssignmentId);
        Assert.True(item.IsOverdue);
    }

    [Fact]
    public async Task GetAsync_orders_by_planned_relief_date_then_member_name_then_id()
    {
        await using var dbContext = CreateDbContext();
        var rankId = await SeedCrewPositionAsync(dbContext);
        var laterMemberName = await SeedAssignmentWithMemberNameAsync(dbContext, rankId, "Zoran Ivanovic");
        var earlierMemberName = await SeedAssignmentWithMemberNameAsync(dbContext, rankId, "Ana Petrovic");
        await SeedRotationAsync(dbContext, laterMemberName.Id, new DateOnly(2026, 7, 1));
        await SeedRotationAsync(dbContext, earlierMemberName.Id, new DateOnly(2026, 7, 1));

        var query = new GetCrewRotationsQuery(dbContext, new FakeDateTimeProvider(Today));

        var response = await query.GetAsync(new GetCrewRotationsRequest(), CancellationToken.None);

        Assert.Equal(2, response.Items.Count);
        Assert.Equal("Ana Petrovic", response.Items[0].CrewMemberName);
        Assert.Equal("Zoran Ivanovic", response.Items[1].CrewMemberName);
    }

    [Fact]
    public async Task GetAsync_projects_the_relieving_crew_member_when_selected()
    {
        await using var dbContext = CreateDbContext();
        var rankId = await SeedCrewPositionAsync(dbContext);
        var assignment = await SeedAssignmentAsync(dbContext, rankId, Guid.NewGuid());
        var relievingMember = await SeedCrewMemberAsync(dbContext, rankId, "Relief Member");
        await SeedRotationAsync(dbContext, assignment.Id, new DateOnly(2026, 7, 1), relievingMember.Id);

        var query = new GetCrewRotationsQuery(dbContext, new FakeDateTimeProvider(Today));

        var response = await query.GetAsync(new GetCrewRotationsRequest(), CancellationToken.None);

        var item = Assert.Single(response.Items);
        Assert.Equal(relievingMember.Id, item.RelievingCrewMemberId);
        Assert.Equal("Relief Member", item.RelievingCrewMemberName);
    }

    [Fact]
    public async Task GetAsync_projects_null_relieving_crew_member_when_none_selected()
    {
        await using var dbContext = CreateDbContext();
        var rankId = await SeedCrewPositionAsync(dbContext);
        var assignment = await SeedAssignmentAsync(dbContext, rankId, Guid.NewGuid());
        await SeedRotationAsync(dbContext, assignment.Id, new DateOnly(2026, 7, 1));

        var query = new GetCrewRotationsQuery(dbContext, new FakeDateTimeProvider(Today));

        var response = await query.GetAsync(new GetCrewRotationsRequest(), CancellationToken.None);

        var item = Assert.Single(response.Items);
        Assert.Null(item.RelievingCrewMemberId);
        Assert.Null(item.RelievingCrewMemberName);
    }

    [Fact]
    public async Task GetAsync_returns_the_requested_page()
    {
        await using var dbContext = CreateDbContext();
        var rankId = await SeedCrewPositionAsync(dbContext);

        for (var index = 1; index <= 5; index++)
        {
            var assignment = await SeedAssignmentAsync(dbContext, rankId, Guid.NewGuid());
            await SeedRotationAsync(dbContext, assignment.Id, new DateOnly(2026, 7, index));
        }

        var query = new GetCrewRotationsQuery(dbContext, new FakeDateTimeProvider(Today));

        var response = await query.GetAsync(
            new GetCrewRotationsRequest { PageNumber = 2, PageSize = 2 },
            CancellationToken.None);

        Assert.Equal(2, response.PageNumber);
        Assert.Equal(2, response.PageSize);
        Assert.Equal(5, response.TotalCount);
        Assert.Equal(2, response.Items.Count);
    }

    [Fact]
    public async Task GetAsync_rejects_a_to_date_before_the_from_date()
    {
        await using var dbContext = CreateDbContext();
        var query = new GetCrewRotationsQuery(dbContext, new FakeDateTimeProvider(Today));

        var exception = await Assert.ThrowsAsync<GetCrewRotationsValidationException>(
            () => query.GetAsync(
                new GetCrewRotationsRequest { From = new DateOnly(2026, 7, 10), To = new DateOnly(2026, 7, 1) },
                CancellationToken.None));

        Assert.Contains(GetCrewRotationsValidator.ToField, exception.Errors);
    }

    [Fact]
    public async Task GetAsync_rejects_page_size_above_maximum()
    {
        await using var dbContext = CreateDbContext();
        var query = new GetCrewRotationsQuery(dbContext, new FakeDateTimeProvider(Today));

        var exception = await Assert.ThrowsAsync<GetCrewRotationsValidationException>(
            () => query.GetAsync(
                new GetCrewRotationsRequest { PageSize = GetCrewRotationsValidator.MaxPageSize + 1 },
                CancellationToken.None));

        Assert.Contains(GetCrewRotationsValidator.PageSizeField, exception.Errors);
    }

    private static async Task<Guid> SeedCrewPositionAsync(HarborOpsDbContext dbContext)
    {
        var position = CrewPosition.Create("Able Seafarer", "Deck", "Senior");
        await dbContext.Set<CrewPosition>().AddAsync(position);
        await dbContext.SaveChangesAsync();
        return position.Id;
    }

    private static async Task<CrewMember> SeedCrewMemberAsync(HarborOpsDbContext dbContext, Guid baseRankId, string name)
    {
        var nameParts = name.Split(' ', 2);
        var member = CrewMember.Create(
            baseRankId,
            nameParts[0],
            nameParts.Length > 1 ? nameParts[1] : string.Empty,
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

    private static async Task<CrewAssignment> SeedAssignmentAsync(HarborOpsDbContext dbContext, Guid rankId, Guid vesselId)
    {
        var member = await SeedCrewMemberAsync(dbContext, rankId, $"Member {Guid.NewGuid():N}"[..14]);

        var assignment = CrewAssignment.CreatePlanned(
            member.Id, vesselId, rankId, null, new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31));
        await dbContext.Set<CrewAssignment>().AddAsync(assignment);
        await dbContext.SaveChangesAsync();
        return assignment;
    }

    private static async Task<CrewAssignment> SeedAssignmentWithMemberNameAsync(HarborOpsDbContext dbContext, Guid rankId, string memberName)
    {
        var member = await SeedCrewMemberAsync(dbContext, rankId, memberName);

        var assignment = CrewAssignment.CreatePlanned(
            member.Id, Guid.NewGuid(), rankId, null, new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31));
        await dbContext.Set<CrewAssignment>().AddAsync(assignment);
        await dbContext.SaveChangesAsync();
        return assignment;
    }

    private static async Task SeedRotationAsync(
        HarborOpsDbContext dbContext, Guid assignmentId, DateOnly plannedReliefDate, Guid? relievingCrewMemberId = null)
    {
        var rotation = CrewRotation.Create(assignmentId, relievingCrewMemberId, "2-2", plannedReliefDate);
        await dbContext.Set<CrewRotation>().AddAsync(rotation);
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
