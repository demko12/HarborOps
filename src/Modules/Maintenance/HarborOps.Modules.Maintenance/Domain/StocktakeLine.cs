using System;
using System.Collections.Generic;
using System.Text;

namespace HarborOps.Modules.Maintenance.Domain;

internal sealed class StocktakeLine
{
    internal const int QuantityScale = 3;
    internal const decimal MaxQuantity = 999_999_999_999_999.999m;

    private StocktakeLine()
    {
    }

    private StocktakeLine(
        Guid id,
        Guid stocktakeId,
        Guid inventoryItemId,
        decimal baselineQuantity,
        Guid baselineStockConcurrencyToken,
        DateTime baselineCapturedUtc)
    {
        Id = id;
        StocktakeId = stocktakeId;
        InventoryItemId = inventoryItemId;
        BaselineQuantity = baselineQuantity;
        BaselineStockConcurrencyToken = baselineStockConcurrencyToken;
        BaselineCapturedUtc = baselineCapturedUtc;
    }

    public Guid Id { get; private set; }

    public Guid StocktakeId { get; private set; }

    public Guid InventoryItemId { get; private set; }

    public decimal BaselineQuantity { get; private set; }

    public Guid BaselineStockConcurrencyToken { get; private set; }

    public DateTime BaselineCapturedUtc { get; private set; }

    public decimal? CountedQuantity { get; private set; }

    public decimal? Variance { get; private set; }

    public string? VarianceReason { get; private set; }

    public Guid? CountedByUserId { get; private set; }

    public DateTime? CountedUtc { get; private set; }

    internal static StocktakeLine Create(Guid stocktakeId, InventoryItem inventoryItem, DateTime utcNow)
    {
        return new StocktakeLine(
            Guid.NewGuid(),
            stocktakeId,
            inventoryItem.Id,
            inventoryItem.Quantity,
            inventoryItem.StockConcurrencyToken,
            utcNow);
    }

    internal static bool HasSupportedPrecision(decimal quantity)
    {
        return decimal.Round(quantity, QuantityScale) == quantity;
    }

    internal void RecordCount(decimal? countedQuantity, string? reason, Guid actorUserId, DateTime utcNow)
    {
        if (countedQuantity is null)
        {
            ClearCount();
            return;
        }

        var counted = countedQuantity.Value;

        if (counted < 0 || counted > MaxQuantity)
        {
            throw new StocktakeInvariantException(
                $"Counted quantity for item '{InventoryItemId}' must be between 0 and {MaxQuantity}.");
        }

        if (!HasSupportedPrecision(counted))
        {
            throw new StocktakeInvariantException(
                $"Counted quantity for item '{InventoryItemId}' supports at most {QuantityScale} decimal places.");
        }

        var variance = counted - BaselineQuantity;
        var normalizedReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();

        if (variance != 0 && normalizedReason is null)
        {
            throw new StocktakeInvariantException(
                $"A reason is required for the nonzero variance of item '{InventoryItemId}'.");
        }

        if (normalizedReason is { Length: > Stocktake.ReasonMaxLength })
        {
            throw new StocktakeInvariantException(
                $"Variance reason must not exceed {Stocktake.ReasonMaxLength} characters.");
        }

        CountedQuantity = counted;
        Variance = variance;
        VarianceReason = normalizedReason;
        CountedByUserId = actorUserId;
        CountedUtc = utcNow;
    }

    internal void RefreshBaseline(InventoryItem inventoryItem, DateTime utcNow)
    {
        BaselineQuantity = inventoryItem.Quantity;
        BaselineStockConcurrencyToken = inventoryItem.StockConcurrencyToken;
        BaselineCapturedUtc = utcNow;

        // A refreshed baseline invalidates the previous physical count, so the operator must recount.
        ClearCount();
    }

    internal void EnsureReadyToPost()
    {
        if (CountedQuantity is null)
        {
            throw new StocktakeInvariantException($"Item '{InventoryItemId}' has not been counted.");
        }

        if (Variance != 0 && VarianceReason is null)
        {
            throw new StocktakeInvariantException(
                $"A reason is required for the nonzero variance of item '{InventoryItemId}'.");
        }
    }

    private void ClearCount()
    {
        CountedQuantity = null;
        Variance = null;
        VarianceReason = null;
        CountedByUserId = null;
        CountedUtc = null;
    }
}
