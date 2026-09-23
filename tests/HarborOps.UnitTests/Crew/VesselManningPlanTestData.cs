using HarborOps.Infrastructure.Persistence;
using HarborOps.Modules.Crew.Application;
using HarborOps.Modules.Crew.Contracts;
using HarborOps.Modules.Crew.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.UnitTests.Crew;

internal static class VesselManningPlanTestData
{
    internal static HarborOpsDbContext CreateDbContext() => CrewAvailabilityTestData.CreateDbContext();

    internal static async Task<CrewPosition> SeedPositionAsync(HarborOpsDbContext dbContext, string rankName = "Bosun")
    {
        var position = CrewPosition.Create(rankName, "Deck", "Senior");
        await dbContext.Set<CrewPosition>().AddAsync(position);
        await dbContext.SaveChangesAsync();

        return position;
    }

    internal static async Task<CrewMember> SeedMemberAsync(
        HarborOpsDbContext dbContext,
        Guid baseRankId,
        string employmentStatus = CrewEmploymentStatuses.Active)
    {
        var member = CrewMember.Create(
            baseRankId,
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

    internal static async Task<CrewAssignment> SeedAssignmentAsync(
        HarborOpsDbContext dbContext,
        Guid crewMemberId,
        Guid vesselId,
        Guid rankId,
        DateOnly signOnDate,
        DateOnly plannedSignOffDate,
        string status = CrewAssignment.PlannedStatus,
        DateOnly? actualSignOffDate = null)
    {
        var assignment = CrewAssignment.CreatePlanned(crewMemberId, vesselId, rankId, null, signOnDate, plannedSignOffDate);

        switch (status)
        {
            case CrewAssignment.ActiveStatus:
                assignment.Activate();
                break;
            case CrewAssignment.CompletedStatus:
                assignment.Complete(actualSignOffDate ?? plannedSignOffDate, null);
                break;
            case CrewAssignment.CancelledStatus:
                assignment.Cancel(Guid.NewGuid(), DateTime.UtcNow, null);
                break;
        }

        await dbContext.Set<CrewAssignment>().AddAsync(assignment);
        await dbContext.SaveChangesAsync();

        return assignment;
    }

    internal static async Task<VesselManningPlan> SeedPlanAsync(
        HarborOpsDbContext dbContext,
        Guid vesselId,
        DateOnly effectiveStartDate,
        DateOnly effectiveEndDate,
        IEnumerable<(Guid RankId, int RequiredHeadcount)> lines,
        Guid? createdByUserId = null,
        DateTime? createdAtUtc = null)
    {
        var plan = VesselManningPlan.Create(
            vesselId,
            effectiveStartDate,
            effectiveEndDate,
            createdByUserId ?? Guid.NewGuid(),
            createdAtUtc ?? new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        await dbContext.Set<VesselManningPlan>().AddAsync(plan);

        foreach (var (rankId, requiredHeadcount) in lines)
        {
            await dbContext.Set<VesselManningPlanLine>().AddAsync(
                VesselManningPlanLine.Create(plan.Id, rankId, requiredHeadcount));
        }

        await dbContext.SaveChangesAsync();

        return plan;
    }
}

internal sealed class FakeVesselExistenceLookup(VesselExistenceResult? result) : IVesselExistenceLookup
{
    public Task<VesselExistenceResult?> GetAsync(Guid vesselId, CancellationToken cancellationToken) =>
        Task.FromResult(result);
}
