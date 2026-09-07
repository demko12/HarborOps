using HarborOps.Modules.Crew.Contracts;
using HarborOps.Modules.Crew.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Crew.Application;

public sealed class RegisterCrewPositionService(DbContext dbContext)
{
    public async Task<RegisterCrewPositionResponse> RegisterAsync(
        RegisterCrewPositionRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var errors = RegisterCrewPositionValidator.Validate(request);
        if (errors.Count > 0)
        {
            throw new RegisterCrewPositionValidationException(errors);
        }

        var rankName = request.RankName!.Trim();
        var department = request.Department!.Trim();
        var seniorityLevel = request.SeniorityLevel!.Trim();

        var positions = dbContext.Set<CrewPosition>();

        // Projected and compared in memory rather than via a translated Where(...), so
        // duplicate detection doesn't depend on SQL Server vs. the in-memory provider's
        // collation/case-sensitivity differences.
        var existingKeys = await positions
            .AsNoTracking()
            .Select(position => new { position.RankName, position.Department })
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

        var position = CrewPosition.Create(rankName, department, seniorityLevel);

        await positions.AddAsync(position, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);

        return new RegisterCrewPositionResponse(position.Id);
    }
}
