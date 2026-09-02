using HarborOps.Modules.Crew.Contracts;
using HarborOps.Modules.Crew.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Crew.Application;

public sealed class UpdateCrewMemberService(DbContext dbContext)
{
    public async Task<UpdateCrewMemberResponse> UpdateAsync(
        Guid crewMemberId,
        UpdateCrewMemberRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var errors = UpdateCrewMemberValidator.Validate(request);
        if (errors.Count > 0)
        {
            throw new UpdateCrewMemberValidationException(errors);
        }

        var crewMember = await dbContext.Set<CrewMember>()
            .SingleOrDefaultAsync(candidate => candidate.Id == crewMemberId, cancellationToken)
            ?? throw new CrewMemberNotFoundException(crewMemberId);

        var baseRankId = request.BaseRankId!.Value;

        var baseRankExists = await dbContext.Set<CrewPosition>()
            .AsNoTracking()
            .AnyAsync(crewPosition => crewPosition.Id == baseRankId, cancellationToken);

        if (!baseRankExists)
        {
            throw new CrewPositionNotFoundException(baseRankId);
        }

        var firstName = request.FirstName!.Trim();
        var lastName = request.LastName!.Trim();
        var nationality = request.Nationality!.Trim();
        var passportNumber = request.PassportNumber!.Trim();
        var cdcNumber = request.CdcNumber!.Trim();
        var dateOfBirth = request.DateOfBirth!.Value;
        var passportExpiry = request.PassportExpiry!.Value;
        var hireDate = request.HireDate!.Value;

        var isUnchanged =
            string.Equals(crewMember.FirstName, firstName, StringComparison.Ordinal)
            && string.Equals(crewMember.LastName, lastName, StringComparison.Ordinal)
            && crewMember.DateOfBirth == dateOfBirth
            && string.Equals(crewMember.Nationality, nationality, StringComparison.Ordinal)
            && string.Equals(crewMember.PassportNumber, passportNumber, StringComparison.Ordinal)
            && crewMember.PassportExpiry == passportExpiry
            && string.Equals(crewMember.CdcNumber, cdcNumber, StringComparison.Ordinal)
            && crewMember.HireDate == hireDate
            && crewMember.BaseRankId == baseRankId;

        if (!isUnchanged)
        {
            crewMember.UpdateProfile(
                firstName,
                lastName,
                dateOfBirth,
                nationality,
                passportNumber,
                passportExpiry,
                cdcNumber,
                hireDate,
                baseRankId);

            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return new UpdateCrewMemberResponse(crewMember.Id);
    }
}
