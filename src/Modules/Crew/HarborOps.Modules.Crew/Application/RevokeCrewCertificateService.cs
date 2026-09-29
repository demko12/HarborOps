using System.Text.Json;
using HarborOps.Modules.Crew.Contracts;
using HarborOps.Modules.Crew.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Crew.Application;

public sealed class RevokeCrewCertificateService(DbContext dbContext, TimeProvider timeProvider)
{
    public async Task<RevokeCrewCertificateResponse> RevokeAsync(
        Guid certificateId,
        Guid actorUserId,
        RevokeCrewCertificateRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var errors = RevokeCrewCertificateValidator.Validate(request);
        if (errors.Count > 0)
        {
            throw new RevokeCrewCertificateValidationException(errors);
        }

        var reason = request.Reason!.Trim();

        var certificate = await dbContext.Set<CrewCertificate>()
            .FirstOrDefaultAsync(candidate => candidate.Id == certificateId, cancellationToken)
            ?? throw new CrewCertificateNotFoundException(certificateId);

        var revokedAtUtc = timeProvider.GetUtcNow().UtcDateTime;
        certificate.Revoke(actorUserId, reason, revokedAtUtc);

        var history = CrewCertificateHistory.RecordRevoked(certificate.Id, actorUserId, revokedAtUtc, reason);
        await dbContext.Set<CrewCertificateHistory>().AddAsync(history, cancellationToken);

        // Affected assignments are surfaced as a warning only -- prior activation evidence is
        // never mutated, and no assignment is cancelled automatically.
        var affectedAssignments = await GetAffectedActiveAssignmentsAsync(certificate, cancellationToken);

        await dbContext.SaveChangesAsync(cancellationToken);

        return new RevokeCrewCertificateResponse(
            certificate.Id,
            certificate.RevokedAtUtc!.Value,
            certificate.ConcurrencyToken,
            affectedAssignments);
    }

    private async Task<IReadOnlyList<AffectedCertificateAssignment>> GetAffectedActiveAssignmentsAsync(
        CrewCertificate certificate, CancellationToken cancellationToken)
    {
        var activeAssignments = await dbContext.Set<CrewAssignment>()
            .AsNoTracking()
            .Where(assignment => assignment.CrewMemberId == certificate.CrewMemberId
                && assignment.Status == CrewAssignment.ActiveStatus
                && assignment.ActualSignOffDate == null)
            .ToListAsync(cancellationToken);

        if (activeAssignments.Count == 0)
        {
            return [];
        }

        var activeAssignmentIds = activeAssignments.Select(assignment => assignment.Id).ToList();

        var evidenceRows = await dbContext.Set<CrewAssignmentQualificationEvidence>()
            .AsNoTracking()
            .Where(evidence => activeAssignmentIds.Contains(evidence.AssignmentId))
            .ToListAsync(cancellationToken);

        var affectedAssignmentIds = evidenceRows
            .Where(evidence => JsonSerializer.Deserialize<List<Guid>>(evidence.CertificateIdsSnapshot)!.Contains(certificate.Id))
            .Select(evidence => evidence.AssignmentId)
            .ToHashSet();

        return activeAssignments
            .Where(assignment => affectedAssignmentIds.Contains(assignment.Id))
            .Select(assignment => new AffectedCertificateAssignment(assignment.Id, assignment.VesselId, assignment.RankId))
            .ToList();
    }
}
