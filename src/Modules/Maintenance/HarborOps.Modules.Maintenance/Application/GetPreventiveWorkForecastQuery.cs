using HarborOps.Modules.Maintenance.Contracts;
using HarborOps.Modules.Maintenance.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Maintenance.Application;

public sealed class GetPreventiveWorkForecastQuery(DbContext dbContext, TimeProvider timeProvider)
{
    public async Task<GetPreventiveWorkForecastResponse> GetForecastAsync(
        GetPreventiveWorkForecastRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var errors = GetPreventiveWorkForecastValidator.Validate(request);
        if (errors.Count > 0)
        {
            throw new GetPreventiveWorkForecastValidationException(errors);
        }

        var pageNumber = request.PageNumber ?? GetPreventiveWorkForecastValidator.DefaultPageNumber;
        var pageSize = request.PageSize ?? GetPreventiveWorkForecastValidator.DefaultPageSize;
        var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);

        var occurrences = await PreventiveWorkOccurrenceReader.ReadAsync(
            dbContext,
            request.From!.Value,
            request.To!.Value,
            request.VesselId,
            scheduleId: null,
            cancellationToken);

        var pendingCount = occurrences.Count(occurrence =>
            occurrence.State is ScheduleOccurrenceState.Pending);
        var generatedCount = occurrences.Count(occurrence =>
            occurrence.State is ScheduleOccurrenceState.Generated);

        var overdueCount = occurrences.Count(occurrence =>
            ScheduleOccurrence.IsOverdue(occurrence.State, occurrence.DueDate, today));

        var filtered = occurrences;

        if (GetPreventiveWorkForecastValidator.TryParseState(request.State, out var state))
        {
            filtered = occurrences.Where(occurrence => occurrence.State == state).ToList();
        }

        var items = filtered
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .Select(occurrence => occurrence.ToItem(today))
            .ToList();

        return new GetPreventiveWorkForecastResponse(
            items,
            pageNumber,
            pageSize,
            filtered.Count,
            pendingCount,
            generatedCount,
            overdueCount);
    }
}
