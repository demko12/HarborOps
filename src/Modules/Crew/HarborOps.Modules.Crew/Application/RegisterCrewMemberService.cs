using HarborOps.Modules.Crew.Contracts;
using HarborOps.Modules.Crew.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Crew.Application;

public sealed class RegisterCrewMemberService(DbContext dbContext)
{
    public async Task<RegisterCrewMemberResponse> RegisterAsync(
        RegisterCrewMemberRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var errors = RegisterCrewMemberValidator.Validate(request);
        if (errors.Count > 0)
        {
            throw new RegisterCrewMemberValidationException(errors);
        }

        var baseRankId = request.BaseRankId!.Value;
        var name = request.Name!.Trim();
        var nationality = request.Nationality!.Trim();
        var imoSeafarerId = request.ImoSeafarerId!.Trim();
        var passportNumber = request.PassportNumber!.Trim();
        var cdcNumber = request.CdcNumber!.Trim();
        var employmentStatus = request.EmploymentStatus!.Trim();

        var crewPositions = dbContext.Set<CrewPosition>();
        var crewMembers = dbContext.Set<CrewMember>();

        var baseRankExists = await crewPositions
            .AsNoTracking()
            .AnyAsync(crewPosition => crewPosition.Id == baseRankId, cancellationToken);

        if (!baseRankExists)
        {
            throw new CrewPositionNotFoundException(baseRankId);
        }

        var imoSeafarerIdExists = await crewMembers
            .AsNoTracking()
            .AnyAsync(crewMember => crewMember.ImoSeafarerId == imoSeafarerId, cancellationToken);

        if (imoSeafarerIdExists)
        {
            throw new DuplicateImoSeafarerIdException(imoSeafarerId);
        }

        var crewMember = CrewMember.Create(
            baseRankId,
            name,
            request.DateOfBirth!.Value,
            nationality,
            imoSeafarerId,
            passportNumber,
            request.PassportExpiry!.Value,
            cdcNumber,
            request.HireDate!.Value,
            employmentStatus);

        await crewMembers.AddAsync(crewMember, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);

        return new RegisterCrewMemberResponse(crewMember.Id);
    }
}
