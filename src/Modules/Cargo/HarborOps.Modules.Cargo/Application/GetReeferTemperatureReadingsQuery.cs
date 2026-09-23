using HarborOps.Modules.Cargo.Contracts;
using HarborOps.Modules.Cargo.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Cargo.Application;

public sealed class GetReeferTemperatureReadingsQuery(DbContext dbContext)
{
    public async Task<GetReeferTemperatureReadingsResponse> GetAsync(
        Guid containerId,
        GetReeferTemperatureReadingsRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        await ReeferMonitoringGuard.FindReeferContainerAsync(
            dbContext,
            containerId,
            cancellationToken);

        var errors = ReeferMonitoringValidator.ValidateReadingPage(request);
        if (errors.Count > 0)
        {
            throw new ReeferMonitoringValidationException(errors);
        }

        var pageNumber = request.PageNumber ?? ReeferMonitoringValidator.DefaultPageNumber;
        var pageSize = request.PageSize ?? ReeferMonitoringValidator.DefaultPageSize;

        var readings = dbContext
            .Set<ReeferTemperatureReading>()
            .AsNoTracking()
            .Where(reading => reading.ContainerId == containerId);

        var totalCount = await readings.CountAsync(cancellationToken);

        var items = await readings
            .OrderByDescending(reading => reading.MeasuredAtUtc)
            .ThenByDescending(reading => reading.RecordedAtUtc)
            .ThenBy(reading => reading.Id)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return new GetReeferTemperatureReadingsResponse(
            [.. items.Select(ReeferMonitoringMapper.ToListItem)],
            pageNumber,
            pageSize,
            totalCount);
    }
}
