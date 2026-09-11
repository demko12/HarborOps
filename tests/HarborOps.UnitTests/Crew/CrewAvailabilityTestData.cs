using HarborOps.Infrastructure.Persistence;
using HarborOps.Modules.Crew.Application;
using HarborOps.Modules.Crew.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.UnitTests.Crew;

internal static class CrewAvailabilityTestData
{
    internal static HarborOpsDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<HarborOpsDbContext>()
            .UseInMemoryDatabase($"HarborOpsUnitTests-{Guid.NewGuid()}")
            .Options;

        return new HarborOpsDbContext(options);
    }

    internal static async Task<CrewMember> SeedMemberAsync(HarborOpsDbContext dbContext)
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
            CrewEmploymentStatuses.Active);

        await dbContext.Set<CrewMember>().AddAsync(member);
        await dbContext.SaveChangesAsync();

        return member;
    }

    internal static async Task<CrewAvailabilityRequest> SeedRequestAsync(
        HarborOpsDbContext dbContext,
        Guid crewMemberId,
        DateOnly startDate,
        DateOnly endDate,
        string status = CrewAvailabilityRequest.RequestedStatus,
        string type = "AnnualLeave",
        Guid? requestedByUserId = null)
    {
        var request = CrewAvailabilityRequest.Create(
            crewMemberId,
            type,
            startDate,
            endDate,
            "Seeded reason.",
            notes: "Seeded private notes.",
            requestedByUserId ?? Guid.NewGuid(),
            new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        if (status == CrewAvailabilityRequest.ApprovedStatus)
        {
            request.Approve(Guid.NewGuid(), new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc));
        }
        else if (status == CrewAvailabilityRequest.RejectedStatus)
        {
            request.Reject(Guid.NewGuid(), "Seeded rejection.", new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc));
        }
        else if (status == CrewAvailabilityRequest.CancelledStatus)
        {
            request.Cancel(Guid.NewGuid(), "Seeded cancellation.", new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc), new DateOnly(2026, 1, 2));
        }

        await dbContext.Set<CrewAvailabilityRequest>().AddAsync(request);
        await dbContext.SaveChangesAsync();

        return request;
    }

    internal static async Task<CrewAssignment> SeedAssignmentAsync(
        HarborOpsDbContext dbContext,
        Guid crewMemberId,
        DateOnly signOnDate,
        DateOnly plannedSignOffDate,
        bool active = false)
    {
        var assignment = CrewAssignment.CreatePlanned(
            crewMemberId, Guid.NewGuid(), Guid.NewGuid(), null, signOnDate, plannedSignOffDate);

        if (active)
        {
            assignment.Activate();
        }

        await dbContext.Set<CrewAssignment>().AddAsync(assignment);
        await dbContext.SaveChangesAsync();

        return assignment;
    }
}
