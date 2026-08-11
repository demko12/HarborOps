using HarborOps.Modules.Crew.Contracts;
using HarborOps.Modules.Crew.Domain;
using HarborOps.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Crew.Application;

public sealed class RegisterCrewCertificateService(DbContext dbContext, IDateTimeProvider dateTimeProvider)
{
    private const string ValidStatus = "Valid";
    private const string ExpiredStatus = "Expired";

    public async Task<RegisterCrewCertificateResponse> RegisterAsync(
        RegisterCrewCertificateRequest request,
        Guid crewMemberId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var errors = new Dictionary<string, string[]>(
            RegisterCrewCertificateValidator.Validate(request),
            StringComparer.Ordinal);

        if (crewMemberId == Guid.Empty)
        {
            errors[RegisterCrewCertificateValidator.CrewMemberIdField] = ["Crew member ID is required."];
        }

        if (errors.Count > 0)
        {
            throw new RegisterCrewCertificateValidationException(errors);
        }

        var type = request.Type!.Trim();
        var name = request.Name!.Trim();
        var issuingAuthority = request.IssuingAuthority!.Trim();
        var licenseNumber = request.LicenseNumber!.Trim().ToUpperInvariant();
        var issueDate = request.IssueDate!.Value;
        var expiryDate = request.ExpiryDate!.Value;

        var crewMembers = dbContext.Set<CrewMember>();
        var crewCertificates = dbContext.Set<CrewCertificate>();

        var crewMemberExists = await crewMembers
            .AsNoTracking()
            .AnyAsync(crewMember => crewMember.Id == crewMemberId, cancellationToken);

        if (!crewMemberExists)
        {
            throw new CrewMemberNotFoundException(crewMemberId);
        }

        // LicenseNumber is always stored already trimmed + uppercased (see above), so a
        // direct equality comparison against the normalized incoming value is sufficient to
        // catch case/whitespace-insensitive duplicates without needing an in-memory workaround.
        var licenseNumberExists = await crewCertificates
            .AsNoTracking()
            .AnyAsync(
                certificate => certificate.CrewMemberId == crewMemberId
                    && certificate.LicenseNumber == licenseNumber,
                cancellationToken);

        if (licenseNumberExists)
        {
            throw new DuplicateCrewCertificateLicenseException(crewMemberId, licenseNumber);
        }

        var today = DateOnly.FromDateTime(dateTimeProvider.UtcNow);
        var status = expiryDate >= today ? ValidStatus : ExpiredStatus;

        var crewCertificate = CrewCertificate.Create(
            crewMemberId,
            type,
            name,
            issuingAuthority,
            licenseNumber,
            issueDate,
            expiryDate,
            status);

        await crewCertificates.AddAsync(crewCertificate, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);

        return new RegisterCrewCertificateResponse(crewCertificate.Id);
    }
}
