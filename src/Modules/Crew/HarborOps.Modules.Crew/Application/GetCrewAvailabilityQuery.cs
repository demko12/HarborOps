using HarborOps.Modules.Crew.Contracts;
using HarborOps.Modules.Crew.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Crew.Application;

internal sealed class GetCrewAvailabilityQuery(DbContext dbContext) : IGetCrewAvailabilityQuery
{
    public async Task<GetCrewAvailabilityResponse> GetAsync(
        GetCrewAvailabilityRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var errors = GetCrewAvailabilityValidator.Validate(request);
        if (errors.Count > 0)
        {
            throw new GetCrewAvailabilityValidationException(errors);
        }

        var pageNumber = request.PageNumber ?? GetCrewAvailabilityValidator.DefaultPageNumber;
        var pageSize = request.PageSize ?? GetCrewAvailabilityValidator.DefaultPageSize;

        var rows =
            from availability in dbContext.Set<CrewAvailabilityRequest>().AsNoTracking()
            join member in dbContext.Set<CrewMember>().AsNoTracking()
                on availability.CrewMemberId equals member.Id
            select new
            {
                availability.Id,
                availability.CrewMemberId,
                MemberName = member.FirstName + " " + member.LastName,
                availability.Type,
                availability.Status,
                availability.StartDate,
                availability.EndDate,
                availability.Reason,
                availability.Notes,
                availability.RequestedByUserId,
                availability.RequestedAtUtc,
                availability.ReviewedByUserId,
                availability.ReviewedAtUtc,
                availability.DecisionReason,
            };

        if (request.CrewMemberId is { } crewMemberId)
        {
            rows = rows.Where(row => row.CrewMemberId == crewMemberId);
        }

        if (!string.IsNullOrWhiteSpace(request.Type))
        {
            var type = request.Type.Trim();
            rows = rows.Where(row => row.Type == type);
        }

        if (!string.IsNullOrWhiteSpace(request.Status))
        {
            var status = request.Status.Trim();
            rows = rows.Where(row => row.Status == status);
        }

        if (request.From is { } from)
        {
            rows = rows.Where(row => row.EndDate >= from);
        }

        if (request.To is { } to)
        {
            rows = rows.Where(row => row.StartDate <= to);
        }

        var totalCount = await rows.CountAsync(cancellationToken);

        var items = await rows
            .OrderBy(row => row.StartDate)
            .ThenBy(row => row.EndDate)
            .ThenBy(row => row.Id)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .Select(row => new CrewAvailabilityListItem(
                row.Id,
                row.CrewMemberId,
                row.MemberName,
                row.Type,
                row.Status,
                row.StartDate,
                row.EndDate,
                row.Reason,
                row.Notes,
                row.RequestedByUserId,
                row.RequestedAtUtc,
                row.ReviewedByUserId,
                row.ReviewedAtUtc,
                row.DecisionReason))
            .ToListAsync(cancellationToken);

        return new GetCrewAvailabilityResponse(items, pageNumber, pageSize, totalCount);
    }
}
