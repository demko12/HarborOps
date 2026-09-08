using HarborOps.Modules.Crew.Contracts;
using HarborOps.Modules.Crew.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Crew.Application;

public sealed class UpdateCrewPositionService(DbContext dbContext)
{
    public async Task<UpdateCrewPositionResponse> UpdateAsync(
        Guid positionId,
        UpdateCrewPositionRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var errors = UpdateCrewPositionValidator.Validate(request);
        if (errors.Count > 0)
        {
            throw new UpdateCrewPositionValidationException(errors);
        }

        var positions = dbContext.Set<CrewPosition>();

        var position = await positions
            .SingleOrDefaultAsync(candidate => candidate.Id == positionId, cancellationToken)
            ?? throw new CrewPositionNotFoundException(positionId);

        var rankName = request.RankName!.Trim();
        var department = request.Department!.Trim();
        var seniorityLevel = request.SeniorityLevel!.Trim();

        // Projected and compared in memory rather than via a translated Where(...), so
        // duplicate detection doesn't depend on SQL Server vs. the in-memory provider's
        // collation/case-sensitivity differences.
        var existingKeys = await positions
            .AsNoTracking()
            .Where(candidate => candidate.Id != positionId)
            .Select(candidate => new { candidate.RankName, candidate.Department })
            .ToListAsync(cancellationToken);

        var normalizedRankName = rankName.ToUpperInvariant();
        var normalizedDepartment = department.ToUpperInvariant();
        var isDuplicate = existingKeys.Any(existing =>
            existing.RankName.Trim().ToUpperInvariant() == normalizedRankName
            && existing.Department.Trim().ToUpperInvariant() == normalizedDepartment);

        if (isDuplicate)
        {
            throw new DuplicateCrewPositionException(rankName, department);
        }

        position.Update(rankName, department, seniorityLevel);

        await dbContext.SaveChangesAsync(cancellationToken);

        return new UpdateCrewPositionResponse(position.Id);
    }
}
