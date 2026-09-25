using HarborOps.Modules.Maintenance.Contracts;
using HarborOps.Modules.Maintenance.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Maintenance.Application;

public sealed class GetStocktakesQuery(DbContext dbContext)
{
    public async Task<GetStocktakesResponse> GetStocktakesAsync(
        GetStocktakesRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var errors = GetStocktakesValidator.Validate(request);
        if (errors.Count > 0)
        {
            throw new GetStocktakesValidationException(errors);
        }

        var pageNumber = request.PageNumber ?? GetStocktakesValidator.DefaultPageNumber;
        var pageSize = request.PageSize ?? GetStocktakesValidator.DefaultPageSize;

        var lines = dbContext.Set<StocktakeLine>().AsNoTracking();
        var stocktakes = dbContext.Set<Stocktake>().AsNoTracking();

        if (!string.IsNullOrWhiteSpace(request.Status)
            && GetStocktakesValidator.TryParseStatus(request.Status, out var status))
        {
            stocktakes = stocktakes.Where(stocktake => stocktake.Status == status);
        }

        var totalCount = await stocktakes.CountAsync(cancellationToken);

        var rows = await stocktakes
            .OrderByDescending(stocktake => stocktake.CreatedUtc)
            .ThenBy(stocktake => stocktake.Id)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .Select(stocktake => new
            {
                stocktake.Id,
                stocktake.Status,
                LineCount = lines.Count(line => line.StocktakeId == stocktake.Id),
                CountedLineCount = lines.Count(line =>
                    line.StocktakeId == stocktake.Id && line.CountedQuantity != null),
                VarianceLineCount = lines.Count(line =>
                    line.StocktakeId == stocktake.Id && line.Variance != null && line.Variance != 0),
                stocktake.CreatedByUserId,
                stocktake.CreatedUtc,
                stocktake.PostedUtc,
                stocktake.CancelledUtc,
                stocktake.ConcurrencyToken,
            })
            .ToListAsync(cancellationToken);

        var items = rows
            .Select(row => new StocktakeListItem(
                row.Id,
                row.Status.ToString(),
                row.LineCount,
                row.CountedLineCount,
                row.VarianceLineCount,
                row.CreatedByUserId,
                row.CreatedUtc,
                row.PostedUtc,
                row.CancelledUtc,
                row.ConcurrencyToken))
            .ToList();

        return new GetStocktakesResponse(items, pageNumber, pageSize, totalCount);
    }
}
