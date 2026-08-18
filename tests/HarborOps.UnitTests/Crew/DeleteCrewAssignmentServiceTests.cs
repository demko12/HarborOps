using HarborOps.Infrastructure.Persistence;
using HarborOps.Modules.Crew.Application;
using HarborOps.Modules.Crew.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.UnitTests.Crew;

public sealed class DeleteCrewAssignmentServiceTests
{
    [Fact]
    public async Task DeleteAsync_removes_a_planned_assignment()
    {
        await using var dbContext = CreateDbContext();
        var assignment = await SeedAssignmentAsync(dbContext, CrewAssignment.PlannedStatus);
        var service = new DeleteCrewAssignmentService(dbContext);

        await service.DeleteAsync(assignment.Id, CancellationToken.None);

        Assert.False(await dbContext.Set<CrewAssignment>().AnyAsync(a => a.Id == assignment.Id));
    }

    [Fact]
    public async Task DeleteAsync_throws_when_assignment_not_found()
    {
        await using var dbContext = CreateDbContext();
        var service = new DeleteCrewAssignmentService(dbContext);

        var exception = await Assert.ThrowsAsync<CrewAssignmentNotFoundException>(
            () => service.DeleteAsync(Guid.NewGuid(), CancellationToken.None));

        Assert.NotEqual(Guid.Empty, exception.AssignmentId);
    }

    [Fact]
    public async Task DeleteAsync_throws_when_assignment_is_not_planned()
    {
        await using var dbContext = CreateDbContext();
        var assignment = await SeedAssignmentAsync(dbContext, CrewAssignment.ActiveStatus);
        var service = new DeleteCrewAssignmentService(dbContext);

        var exception = await Assert.ThrowsAsync<CrewAssignmentNotDeletableException>(
            () => service.DeleteAsync(assignment.Id, CancellationToken.None));

        Assert.Equal(assignment.Id, exception.AssignmentId);
        Assert.True(await dbContext.Set<CrewAssignment>().AnyAsync(a => a.Id == assignment.Id));
    }

    // The domain has no public transition from Planned to Active (signing on/off an
    // assignment is out of scope), so status is forced directly for this guard-clause test.
    private static async Task<CrewAssignment> SeedAssignmentAsync(HarborOpsDbContext dbContext, string status)
    {
        var assignment = CrewAssignment.CreatePlanned(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            null,
            new DateOnly(2026, 1, 1),
            new DateOnly(2026, 6, 1));

        if (status != CrewAssignment.PlannedStatus)
        {
            typeof(CrewAssignment).GetProperty(nameof(CrewAssignment.Status))!.SetValue(assignment, status);
        }

        await dbContext.Set<CrewAssignment>().AddAsync(assignment);
        await dbContext.SaveChangesAsync();

        return assignment;
    }

    private static HarborOpsDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<HarborOpsDbContext>()
            .UseInMemoryDatabase($"HarborOpsUnitTests-{Guid.NewGuid()}")
            .Options;

        return new HarborOpsDbContext(options);
    }
}
