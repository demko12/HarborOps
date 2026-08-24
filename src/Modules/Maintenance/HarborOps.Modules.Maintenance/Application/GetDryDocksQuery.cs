using HarborOps.Modules.Maintenance.Contracts;
using HarborOps.Modules.Maintenance.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Maintenance.Application;

public sealed class GetDryDocksQuery(DbContext dbContext, TimeProvider timeProvider)
{
    public async Task<GetDryDocksResponse> GetDryDocksAsync(
        GetDryDocksRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var errors = GetDryDocksValidator.Validate(request);
        if (errors.Count > 0)
        {
            throw new GetDryDocksValidationException(errors);
        }

        var pageNumber = request.PageNumber ?? GetDryDocksValidator.DefaultPageNumber;
        var pageSize = request.PageSize ?? GetDryDocksValidator.DefaultPageSize;
        var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);

        var dryDocks = dbContext.Set<DryDock>().AsNoTracking();

        if (request.VesselId is { } vesselId)
        {
            dryDocks = dryDocks.Where(dryDock => dryDock.VesselId == vesselId);
        }

        if (request.PortId is { } portId)
        {
            dryDocks = dryDocks.Where(dryDock => dryDock.PortId == portId);
        }

        if (GetDryDocksValidator.TryParseStatus(request.Status, out var status))
        {
            dryDocks = dryDocks.Where(dryDock => dryDock.Status == status);
        }

        if (request.StartFrom is { } startFrom)
        {
            dryDocks = dryDocks.Where(dryDock => dryDock.StartDate >= startFrom);
        }

        if (request.StartTo is { } startTo)
        {
            dryDocks = dryDocks.Where(dryDock => dryDock.StartDate <= startTo);
        }

        if (request.Overdue is { } overdue)
        {
            dryDocks = overdue
                ? dryDocks.Where(dryDock =>
                    dryDock.Status == DryDockStatus.Planned && dryDock.StartDate < today)
                : dryDocks.Where(dryDock =>
                    dryDock.Status != DryDockStatus.Planned || dryDock.StartDate >= today);
        }

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var search = request.Search.Trim();
            dryDocks = dryDocks.Where(dryDock => dryDock.PlannedTasks.Contains(search));
        }

        var totalCount = await dryDocks.CountAsync(cancellationToken);

        var rows = await dryDocks
            .OrderBy(dryDock => dryDock.StartDate)
            .ThenBy(dryDock => dryDock.Id)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .Select(dryDock => new
            {
                dryDock.Id,
                dryDock.VesselId,
                dryDock.PortId,
                dryDock.StartDate,
                dryDock.EndDate,
                dryDock.PlannedTasks,
                dryDock.ActualCost,
                dryDock.Status,
            })
            .ToListAsync(cancellationToken);

        var items = rows
            .Select(row => new DryDockListItem(
                row.Id,
                row.VesselId,
                row.PortId,
                row.StartDate,
                row.EndDate,
                row.PlannedTasks,
                row.ActualCost,
                row.Status.ToString(),
                DryDock.IsOverdue(row.Status, row.StartDate, today),
                DryDock.DaysUntilStart(row.Status, row.StartDate, today)))
            .ToList();

        return new GetDryDocksResponse(items, pageNumber, pageSize, totalCount);
    }
}
