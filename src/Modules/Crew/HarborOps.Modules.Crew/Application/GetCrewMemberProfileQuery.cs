using HarborOps.Modules.Crew.Contracts;
using HarborOps.Modules.Crew.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Crew.Application;

internal sealed class GetCrewMemberProfileQuery(DbContext dbContext) : IGetCrewMemberProfileQuery
{
    internal const int MaxCertificates = 50;
    internal const int MaxAssignments = 50;

    public async Task<CrewMemberProfileResponse> GetAsync(Guid crewMemberId, CancellationToken cancellationToken)
    {
        var member = await (
            from crewMember in dbContext.Set<CrewMember>().AsNoTracking()
            join position in dbContext.Set<CrewPosition>().AsNoTracking()
                on crewMember.BaseRankId equals position.Id
            where crewMember.Id == crewMemberId
            select new
            {
                crewMember.Id,
                crewMember.Name,
                crewMember.Nationality,
                crewMember.ImoSeafarerId,
                crewMember.PassportNumber,
                crewMember.DateOfBirth,
                crewMember.PassportExpiry,
                crewMember.CdcNumber,
                crewMember.HireDate,
                crewMember.EmploymentStatus,
                crewMember.BaseRankId,
                BaseRankName = position.RankName,
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (member is null)
        {
            throw new CrewMemberNotFoundException(crewMemberId);
        }

        var certificates = await dbContext.Set<CrewCertificate>()
            .AsNoTracking()
            .Where(certificate => certificate.CrewMemberId == crewMemberId)
            .OrderBy(certificate => certificate.ExpiryDate)
            .ThenBy(certificate => certificate.Id)
            .Take(MaxCertificates)
            .Select(certificate => new CrewCertificateProfileItem(
                certificate.Id,
                certificate.Type,
                certificate.Name,
                certificate.IssuingAuthority,
                certificate.LicenseNumber,
                certificate.IssueDate,
                certificate.ExpiryDate,
                certificate.Status))
            .ToListAsync(cancellationToken);

        var assignments = await (
            from assignment in dbContext.Set<CrewAssignment>().AsNoTracking()
            join position in dbContext.Set<CrewPosition>().AsNoTracking()
                on assignment.RankId equals position.Id
            where assignment.CrewMemberId == crewMemberId
            orderby assignment.SignOnDate descending, assignment.Id
            select new CrewAssignmentProfileItem(
                assignment.Id,
                assignment.VesselId,
                assignment.RankId,
                position.RankName,
                assignment.SignOnPortId,
                assignment.SignOffPortId,
                assignment.SignOnDate,
                assignment.PlannedSignOffDate,
                assignment.ActualSignOffDate,
                assignment.Status,
                assignment.CancelledAtUtc,
                assignment.CancelledByUserId,
                assignment.CancellationNotes))
            .Take(MaxAssignments)
            .ToListAsync(cancellationToken);

        return new CrewMemberProfileResponse(
            member.Id,
            member.Name,
            member.Nationality,
            member.ImoSeafarerId,
            member.PassportNumber,
            member.DateOfBirth,
            member.PassportExpiry,
            member.CdcNumber,
            member.HireDate,
            member.EmploymentStatus,
            member.BaseRankId,
            member.BaseRankName,
            certificates,
            assignments);
    }
}
