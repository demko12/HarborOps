using HarborOps.Infrastructure.Persistence;
using HarborOps.Modules.Crew.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.UnitTests.Crew;

internal static class QualificationPolicyTestData
{
    internal static HarborOpsDbContext CreateDbContext() => VesselManningPlanTestData.CreateDbContext();

    internal static async Task<CrewCertificate> SeedCertificateAsync(
        HarborOpsDbContext dbContext,
        Guid crewMemberId,
        string type,
        DateOnly issueDate,
        DateOnly expiryDate,
        string status = "Valid")
    {
        var certificate = CrewCertificate.Create(
            crewMemberId, type, $"{type} certificate", "Flag Authority", $"LIC-{Guid.NewGuid():N}"[..15],
            issueDate, expiryDate, status);

        await dbContext.Set<CrewCertificate>().AddAsync(certificate);
        await dbContext.SaveChangesAsync();

        return certificate;
    }

    internal static async Task<CrewQualificationPolicy> SeedPolicyAsync(
        HarborOpsDbContext dbContext,
        Guid crewPositionId,
        bool enabled,
        IEnumerable<string> requiredTypes,
        Guid? createdByUserId = null,
        DateTime? createdAtUtc = null)
    {
        var policy = CrewQualificationPolicy.Create(
            crewPositionId, enabled, createdByUserId ?? Guid.NewGuid(),
            createdAtUtc ?? new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        await dbContext.Set<CrewQualificationPolicy>().AddAsync(policy);

        foreach (var type in requiredTypes)
        {
            await dbContext.Set<CrewQualificationRequirement>().AddAsync(
                CrewQualificationRequirement.Create(policy.Id, type));
        }

        await dbContext.SaveChangesAsync();

        return policy;
    }
}
