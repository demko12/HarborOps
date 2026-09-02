using HarborOps.Infrastructure.Persistence;
using HarborOps.Modules.Crew.Application;
using HarborOps.Modules.Crew.Contracts;
using HarborOps.Modules.Crew.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.UnitTests.Crew;

public sealed class ChangeCrewMemberEmploymentStatusServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 15, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ChangeAsync_records_the_transition_and_updates_current_status()
    {
        await using var dbContext = CreateDbContext();
        var member = await SeedMemberAsync(dbContext, CrewEmploymentStatuses.Active);
        var service = CreateService(dbContext);
        var actingUserId = Guid.NewGuid();

        var response = await service.ChangeAsync(
            member.Id,
            actingUserId,
            new ChangeCrewMemberEmploymentStatusRequest { Status = CrewEmploymentStatuses.OnLeave, Notes = "Medical leave" },
            CancellationToken.None);

        Assert.Equal(member.Id, response.CrewMemberId);
        Assert.Equal(CrewEmploymentStatuses.Active, response.PreviousStatus);
        Assert.Equal(CrewEmploymentStatuses.OnLeave, response.NewStatus);
        Assert.Equal(actingUserId, response.ChangedByUserId);
        Assert.Equal("Medical leave", response.Notes);
        Assert.Equal(Now.UtcDateTime, response.ChangedAtUtc);

        var updated = await dbContext.Set<CrewMember>().SingleAsync(m => m.Id == member.Id, CancellationToken.None);
        Assert.Equal(CrewEmploymentStatuses.OnLeave, updated.EmploymentStatus);

        var history = await dbContext.Set<CrewEmploymentStatusHistory>()
            .SingleAsync(h => h.CrewMemberId == member.Id, CancellationToken.None);
        Assert.Equal(CrewEmploymentStatuses.OnLeave, history.Status);
        Assert.Equal(actingUserId, history.ChangedByUserId);
        Assert.Equal("Medical leave", history.Notes);
        Assert.Equal(Now.UtcDateTime, history.ChangedAtUtc);
    }

    [Fact]
    public async Task ChangeAsync_normalizes_blank_notes_to_null()
    {
        await using var dbContext = CreateDbContext();
        var member = await SeedMemberAsync(dbContext, CrewEmploymentStatuses.Active);
        var service = CreateService(dbContext);

        var response = await service.ChangeAsync(
            member.Id,
            Guid.NewGuid(),
            new ChangeCrewMemberEmploymentStatusRequest { Status = CrewEmploymentStatuses.OnLeave, Notes = "   " },
            CancellationToken.None);

        Assert.Null(response.Notes);
    }

    [Fact]
    public async Task ChangeAsync_rejects_an_unknown_crew_member()
    {
        await using var dbContext = CreateDbContext();
        var service = CreateService(dbContext);

        await Assert.ThrowsAsync<CrewMemberNotFoundException>(
            () => service.ChangeAsync(
                Guid.NewGuid(),
                Guid.NewGuid(),
                new ChangeCrewMemberEmploymentStatusRequest { Status = CrewEmploymentStatuses.OnLeave },
                CancellationToken.None));
    }

    [Fact]
    public async Task ChangeAsync_rejects_an_unsupported_status()
    {
        await using var dbContext = CreateDbContext();
        var member = await SeedMemberAsync(dbContext, CrewEmploymentStatuses.Active);
        var service = CreateService(dbContext);

        await Assert.ThrowsAsync<ChangeCrewMemberEmploymentStatusValidationException>(
            () => service.ChangeAsync(
                member.Id,
                Guid.NewGuid(),
                new ChangeCrewMemberEmploymentStatusRequest { Status = "Inactive" },
                CancellationToken.None));
    }

    [Fact]
    public async Task ChangeAsync_rejects_an_unchanged_status()
    {
        await using var dbContext = CreateDbContext();
        var member = await SeedMemberAsync(dbContext, CrewEmploymentStatuses.Active);
        var service = CreateService(dbContext);

        var exception = await Assert.ThrowsAsync<CrewMemberEmploymentStatusUnchangedException>(
            () => service.ChangeAsync(
                member.Id,
                Guid.NewGuid(),
                new ChangeCrewMemberEmploymentStatusRequest { Status = CrewEmploymentStatuses.Active },
                CancellationToken.None));

        Assert.Equal(member.Id, exception.CrewMemberId);
        Assert.Equal(0, await dbContext.Set<CrewEmploymentStatusHistory>().CountAsync(CancellationToken.None));
    }

    [Fact]
    public async Task ChangeAsync_rejects_termination_when_an_open_assignment_exists()
    {
        await using var dbContext = CreateDbContext();
        var member = await SeedMemberAsync(dbContext, CrewEmploymentStatuses.Active);
        await SeedOpenAssignmentAsync(dbContext, member.Id);
        var service = CreateService(dbContext);

        var exception = await Assert.ThrowsAsync<CrewMemberHasOpenAssignmentOrRotationException>(
            () => service.ChangeAsync(
                member.Id,
                Guid.NewGuid(),
                new ChangeCrewMemberEmploymentStatusRequest { Status = CrewEmploymentStatuses.Terminated },
                CancellationToken.None));

        Assert.Equal(member.Id, exception.CrewMemberId);
    }

    [Fact]
    public async Task ChangeAsync_rejects_termination_when_a_planned_rotation_exists()
    {
        await using var dbContext = CreateDbContext();
        var member = await SeedMemberAsync(dbContext, CrewEmploymentStatuses.Active);
        var assignment = await SeedOpenAssignmentAsync(dbContext, member.Id);
        assignment.Complete(new DateOnly(2026, 5, 1), null);
        var rotation = CrewRotation.Create(assignment.Id, null, "2-2", new DateOnly(2026, 6, 1));
        await dbContext.Set<CrewRotation>().AddAsync(rotation);
        await dbContext.SaveChangesAsync();
        var service = CreateService(dbContext);

        var exception = await Assert.ThrowsAsync<CrewMemberHasOpenAssignmentOrRotationException>(
            () => service.ChangeAsync(
                member.Id,
                Guid.NewGuid(),
                new ChangeCrewMemberEmploymentStatusRequest { Status = CrewEmploymentStatuses.Terminated },
                CancellationToken.None));

        Assert.Equal(member.Id, exception.CrewMemberId);
    }

    [Fact]
    public async Task ChangeAsync_allows_termination_when_no_open_work_exists()
    {
        await using var dbContext = CreateDbContext();
        var member = await SeedMemberAsync(dbContext, CrewEmploymentStatuses.Active);
        var service = CreateService(dbContext);

        var response = await service.ChangeAsync(
            member.Id,
            Guid.NewGuid(),
            new ChangeCrewMemberEmploymentStatusRequest { Status = CrewEmploymentStatuses.Terminated },
            CancellationToken.None);

        Assert.Equal(CrewEmploymentStatuses.Terminated, response.NewStatus);
    }

    [Fact]
    public async Task ChangeAsync_allows_terminating_a_member_with_a_completed_assignment()
    {
        await using var dbContext = CreateDbContext();
        var member = await SeedMemberAsync(dbContext, CrewEmploymentStatuses.Active);
        var assignment = await SeedOpenAssignmentAsync(dbContext, member.Id);
        assignment.Complete(new DateOnly(2026, 5, 1), null);
        await dbContext.SaveChangesAsync();
        var service = CreateService(dbContext);

        var response = await service.ChangeAsync(
            member.Id,
            Guid.NewGuid(),
            new ChangeCrewMemberEmploymentStatusRequest { Status = CrewEmploymentStatuses.Terminated },
            CancellationToken.None);

        Assert.Equal(CrewEmploymentStatuses.Terminated, response.NewStatus);
    }

    private static ChangeCrewMemberEmploymentStatusService CreateService(HarborOpsDbContext dbContext) =>
        new(dbContext, new FixedTimeProvider(Now));

    private static async Task<CrewMember> SeedMemberAsync(HarborOpsDbContext dbContext, string employmentStatus)
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
            employmentStatus);

        await dbContext.Set<CrewMember>().AddAsync(member);
        await dbContext.SaveChangesAsync();

        return member;
    }

    private static async Task<CrewAssignment> SeedOpenAssignmentAsync(HarborOpsDbContext dbContext, Guid crewMemberId)
    {
        var assignment = CrewAssignment.CreatePlanned(
            crewMemberId, Guid.NewGuid(), Guid.NewGuid(), null, new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31));

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
