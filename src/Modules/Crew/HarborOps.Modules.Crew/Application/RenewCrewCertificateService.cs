using HarborOps.Modules.Crew.Contracts;
using HarborOps.Modules.Crew.Domain;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Crew.Application;

public sealed class RenewCrewCertificateService(DbContext dbContext, TimeProvider timeProvider)
{
    private const string ValidStatus = "Valid";
    private const string ExpiredStatus = "Expired";
    private const int MaxChainWalk = 50;
    private const int UniqueIndexViolation = 2601;
    private const int UniqueConstraintViolation = 2627;

    public async Task<RenewCrewCertificateResponse> RenewAsync(
        Guid certificateId,
        Guid actorUserId,
        RenewCrewCertificateRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var errors = RenewCrewCertificateValidator.Validate(request);
        if (errors.Count > 0)
        {
            throw new RenewCrewCertificateValidationException(errors);
        }

        var name = request.Name!.Trim();
        var issuingAuthority = request.IssuingAuthority!.Trim();
        var licenseNumber = request.LicenseNumber!.Trim().ToUpperInvariant();
        var issueDate = request.IssueDate!.Value;
        var expiryDate = request.ExpiryDate!.Value;

        var crewCertificates = dbContext.Set<CrewCertificate>();

        var predecessor = await crewCertificates
            .AsNoTracking()
            .FirstOrDefaultAsync(certificate => certificate.Id == certificateId, cancellationToken)
            ?? throw new CrewCertificateNotFoundException(certificateId);

        var hasSuccessor = await crewCertificates
            .AsNoTracking()
            .AnyAsync(certificate => certificate.PredecessorCertificateId == certificateId, cancellationToken);

        if (hasSuccessor)
        {
            throw new DuplicateCrewCertificateRenewalException(certificateId);
        }

        var chainIds = await GetAncestorChainIdsAsync(crewCertificates, predecessor, cancellationToken);
        chainIds.Add(predecessor.Id);

        // License numbers stay attached to the same physical license across renewals, so a
        // collision against the certificate's own renewal chain is expected, not a duplicate.
        var licenseNumberExists = await crewCertificates
            .AsNoTracking()
            .AnyAsync(
                certificate => certificate.CrewMemberId == predecessor.CrewMemberId
                    && certificate.LicenseNumber == licenseNumber
                    && !chainIds.Contains(certificate.Id),
                cancellationToken);

        if (licenseNumberExists)
        {
            throw new DuplicateCrewCertificateLicenseException(predecessor.CrewMemberId, licenseNumber);
        }

        var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
        var status = expiryDate >= today ? ValidStatus : ExpiredStatus;

        var successor = CrewCertificate.CreateRenewal(
            predecessor.Id,
            predecessor.CrewMemberId,
            predecessor.Type,
            name,
            issuingAuthority,
            licenseNumber,
            issueDate,
            expiryDate,
            status);

        var occurredAtUtc = timeProvider.GetUtcNow().UtcDateTime;
        var history = CrewCertificateHistory.RecordRenewed(successor.Id, actorUserId, occurredAtUtc, predecessor.Id);

        await crewCertificates.AddAsync(successor, cancellationToken);
        await dbContext.Set<CrewCertificateHistory>().AddAsync(history, cancellationToken);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            throw new DuplicateCrewCertificateRenewalException(predecessor.Id);
        }

        return new RenewCrewCertificateResponse(
            successor.Id,
            predecessor.Id,
            successor.CrewMemberId,
            successor.Type,
            successor.Status,
            successor.ConcurrencyToken);
    }

    private static async Task<HashSet<Guid>> GetAncestorChainIdsAsync(
        DbSet<CrewCertificate> crewCertificates,
        CrewCertificate predecessor,
        CancellationToken cancellationToken)
    {
        var ancestorIds = new HashSet<Guid>();
        var currentPredecessorId = predecessor.PredecessorCertificateId;
        var hops = 0;

        while (currentPredecessorId is { } id && hops < MaxChainWalk)
        {
            ancestorIds.Add(id);
            currentPredecessorId = await crewCertificates
                .AsNoTracking()
                .Where(certificate => certificate.Id == id)
                .Select(certificate => certificate.PredecessorCertificateId)
                .FirstOrDefaultAsync(cancellationToken);
            hops++;
        }

        return ancestorIds;
    }

    private static bool IsUniqueViolation(DbUpdateException exception)
    {
        return exception.InnerException is SqlException
        {
            Number: UniqueIndexViolation or UniqueConstraintViolation,
        };
    }
}
