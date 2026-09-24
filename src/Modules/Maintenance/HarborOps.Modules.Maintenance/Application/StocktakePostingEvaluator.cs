using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using HarborOps.Modules.Maintenance.Contracts;
using HarborOps.Modules.Maintenance.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Maintenance.Application;

internal static class StocktakePostingEvaluator
{
    internal sealed record Evaluation(
        IReadOnlyList<StocktakePreviewLine> Lines,
        IReadOnlyList<StocktakePostingBlocker> Blockers,
        string PreviewToken);

    internal static async Task<Evaluation> EvaluateAsync(
        DbContext dbContext,
        Stocktake stocktake,
        IReadOnlyList<StocktakeLine> lines,
        IReadOnlyDictionary<Guid, InventoryItem> items,
        CancellationToken cancellationToken)
    {
        var itemIds = lines.Select(line => line.InventoryItemId).ToList();

        var holds = await (
            from reservation in dbContext.Set<WorkOrderPartReservation>().AsNoTracking()
            join workOrder in dbContext.Set<MaintenanceWorkOrder>().AsNoTracking()
                on reservation.WorkOrderId equals workOrder.Id
            where itemIds.Contains(reservation.InventoryItemId)
                && (workOrder.Status == WorkOrderStatus.Open || workOrder.Status == WorkOrderStatus.InProgress)
                && reservation.QuantityReserved > reservation.QuantityConsumed
            select new
            {
                reservation.InventoryItemId,
                reservation.WorkOrderId,
                Held = reservation.QuantityReserved - reservation.QuantityConsumed,
            })
            .ToListAsync(cancellationToken);

        var previewLines = new List<StocktakePreviewLine>(lines.Count);
        var blockers = new List<StocktakePostingBlocker>();

        // The preview token covers everything posting depends on, so a post made against an
        // outdated preview is rejected even when intermediate movements restored the same quantity.
        var fingerprint = new StringBuilder()
            .Append(stocktake.Id)
            .Append('|')
            .Append(stocktake.ConcurrencyToken);

        foreach (var line in lines.OrderBy(line => line.InventoryItemId))
        {
            var item = items[line.InventoryItemId];
            var itemHolds = holds.Where(hold => hold.InventoryItemId == item.Id).ToList();
            var reserved = itemHolds.Sum(hold => hold.Held);
            var baselineIsStale = item.StockConcurrencyToken != line.BaselineStockConcurrencyToken;

            if (baselineIsStale)
            {
                blockers.Add(new StocktakePostingBlocker(
                    item.Id,
                    StocktakeBlockerCodes.StaleBaseline,
                    $"Stock for '{item.PartNumber}' changed after the count baseline was captured "
                        + $"({Format(line.BaselineQuantity)} -> {Format(item.Quantity)}). Refresh the line and recount.",
                    []));
            }

            if (line.CountedQuantity is not { } counted)
            {
                blockers.Add(new StocktakePostingBlocker(
                    item.Id,
                    StocktakeBlockerCodes.NotCounted,
                    $"'{item.PartNumber}' has not been counted.",
                    []));
            }
            else
            {
                if (line.Variance != 0 && line.VarianceReason is null)
                {
                    blockers.Add(new StocktakePostingBlocker(
                        item.Id,
                        StocktakeBlockerCodes.MissingReason,
                        $"'{item.PartNumber}' has a nonzero variance and needs a reason.",
                        []));
                }

                if (counted < reserved)
                {
                    blockers.Add(new StocktakePostingBlocker(
                        item.Id,
                        StocktakeBlockerCodes.BelowReserved,
                        $"Counted {Format(counted)} of '{item.PartNumber}' is below the outstanding reserved "
                            + $"quantity {Format(reserved)}. A planner must reduce or release the reservations "
                            + "on the listed work orders before this count can be posted.",
                        itemHolds.Select(hold => hold.WorkOrderId).Distinct().Order().ToList()));
                }
            }

            previewLines.Add(new StocktakePreviewLine(
                item.Id,
                item.Name,
                item.PartNumber,
                line.BaselineQuantity,
                item.Quantity,
                line.CountedQuantity,
                line.Variance,
                reserved,
                baselineIsStale));

            fingerprint
                .Append('|').Append(item.Id)
                .Append(':').Append(line.BaselineStockConcurrencyToken)
                .Append(':').Append(line.CountedQuantity is { } value ? Format(value) : "-")
                .Append(':').Append(item.StockConcurrencyToken)
                .Append(':').Append(item.ReservationConcurrencyToken)
                .Append(':').Append(Format(reserved));
        }

        var previewToken = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(fingerprint.ToString())));

        return new Evaluation(previewLines, blockers, previewToken);
    }

    private static string Format(decimal value)
    {
        return value.ToString("0.000", CultureInfo.InvariantCulture);
    }
}
