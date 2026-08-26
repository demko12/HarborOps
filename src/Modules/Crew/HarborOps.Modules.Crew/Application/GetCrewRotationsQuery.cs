using HarborOps.Modules.Crew.Contracts;
using HarborOps.Modules.Crew.Domain;
using HarborOps.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Crew.Application;

internal sealed class GetCrewRotationsQuery(DbContext dbContext, IDateTimeProvider dateTimeProvider)
    : IGetCrewRotationsQuery
{
    public async Task<GetCrewRotationsResponse> GetAsync(
        GetCrewRotationsRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var errors = GetCrewRotationsValidator.Validate(request);
        if (errors.Count > 0)
        {
            throw new GetCrewRotationsValidationException(errors);
        }

        var pageNumber = request.PageNumber ?? GetCrewRotationsValidator.DefaultPageNumber;
        var pageSize = request.PageSize ?? GetCrewRotationsValidator.DefaultPageSize;
        var today = DateOnly.FromDateTime(dateTimeProvider.UtcNow);

        var rows =
            from rotation in dbContext.Set<CrewRotation>().AsNoTracking()
            join assignment in dbContext.Set<CrewAssignment>().AsNoTracking()
                on rotation.AssignmentId equals assignment.Id
            join member in dbContext.Set<CrewMember>().AsNoTracking()
                on assignment.CrewMemberId equals member.Id
            join position in dbContext.Set<CrewPosition>().AsNoTracking()
                on assignment.RankId equals position.Id
            join relievingMember in dbContext.Set<CrewMember>().AsNoTracking()
                on rotation.RelievingCrewMemberId equals relievingMember.Id into relievingJoin
            from relievingMember in relievingJoin.DefaultIfEmpty()
            select new
            {
                rotation.Id,
                rotation.AssignmentId,
                assignment.VesselId,
                assignment.CrewMemberId,
                MemberName = member.Name,
                assignment.RankId,
                RankName = position.RankName,
                rotation.RelievingCrewMemberId,
                RelievingCrewMemberName = relievingMember != null ? relievingMember.Name : null,
                rotation.RotationPattern,
                rotation.PlannedReliefDate,
                assignment.ActualSignOffDate,
                rotation.Status,
                rotation.ExecutedAtUtc,
                rotation.ExecutedByUserId,
                rotation.ReplacementAssignmentId,
                rotation.CancelledAtUtc,
                rotation.CancelledByUserId,
                rotation.CancellationNotes,
            };

        if (request.VesselId is { } vesselId)
        {
            rows = rows.Where(row => row.VesselId == vesselId);
        }

        if (request.From is { } from)
        {
            rows = rows.Where(row => row.PlannedReliefDate >= from);
        }

        if (request.To is { } to)
        {
            rows = rows.Where(row => row.PlannedReliefDate <= to);
        }

        if (request.OverdueOnly == true)
        {
            rows = rows.Where(row => row.PlannedReliefDate < today
                && row.ActualSignOffDate == null
                && row.Status == CrewRotation.PlannedStatus);
        }

        var totalCount = await rows.CountAsync(cancellationToken);

        var items = await rows
            .OrderBy(row => row.PlannedReliefDate)
            .ThenBy(row => row.MemberName)
            .ThenBy(row => row.Id)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .Select(row => new CrewRotationListItem(
                row.Id,
                row.AssignmentId,
                row.VesselId,
                row.CrewMemberId,
                row.MemberName,
                row.RankId,
                row.RankName,
                row.RelievingCrewMemberId,
                row.RelievingCrewMemberName,
                row.RotationPattern,
                row.PlannedReliefDate,
                row.PlannedReliefDate < today
                    && row.ActualSignOffDate == null
                    && row.Status == CrewRotation.PlannedStatus,
                row.Status,
                row.ExecutedAtUtc,
                row.ExecutedByUserId,
                row.ReplacementAssignmentId,
                row.CancelledAtUtc,
                row.CancelledByUserId,
                row.CancellationNotes))
            .ToListAsync(cancellationToken);

        return new GetCrewRotationsResponse(items, pageNumber, pageSize, totalCount);
    }
}
