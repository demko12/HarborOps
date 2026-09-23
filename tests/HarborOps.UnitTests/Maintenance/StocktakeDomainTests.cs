using HarborOps.Modules.Maintenance.Domain;

namespace HarborOps.UnitTests.Maintenance;

public sealed class StocktakeDomainTests
{
    private static readonly DateTime UtcNow = new(2026, 9, 1, 8, 0, 0, DateTimeKind.Utc);
    private static readonly Guid ActorUserId = Guid.NewGuid();

    [Fact]
    public void Create_starts_a_draft_with_audit_fields()
    {
        var stocktake = Stocktake.Create("Engine room", ActorUserId, UtcNow);

        Assert.Equal(StocktakeStatus.Draft, stocktake.Status);
        Assert.Equal("Engine room", stocktake.Notes);
        Assert.Equal(ActorUserId, stocktake.CreatedByUserId);
        Assert.Equal(UtcNow, stocktake.CreatedUtc);
        Assert.NotEqual(Guid.Empty, stocktake.ConcurrencyToken);
    }

    [Fact]
    public void Line_captures_quantity_and_stock_token_as_the_baseline()
    {
        var item = CreateItem(12.5m);
        item.IncreaseStock(1m);

        var line = StocktakeLine.Create(Guid.NewGuid(), item, UtcNow);

        Assert.Equal(item.Id, line.InventoryItemId);
        Assert.Equal(13.5m, line.BaselineQuantity);
        Assert.Equal(item.StockConcurrencyToken, line.BaselineStockConcurrencyToken);
        Assert.Equal(UtcNow, line.BaselineCapturedUtc);
        Assert.Null(line.CountedQuantity);
    }

    [Theory]
    [InlineData(15, 5)]
    [InlineData(4, -6)]
    [InlineData(10, 0)]
    public void RecordCount_computes_positive_negative_and_zero_variance(int counted, int expectedVariance)
    {
        var line = StocktakeLine.Create(Guid.NewGuid(), CreateItem(10m), UtcNow);

        line.RecordCount(counted, "Physical count", ActorUserId, UtcNow);

        Assert.Equal((decimal)counted, line.CountedQuantity);
        Assert.Equal((decimal)expectedVariance, line.Variance);
        Assert.Equal(ActorUserId, line.CountedByUserId);
        Assert.Equal(UtcNow, line.CountedUtc);
    }

    [Fact]
    public void RecordCount_preserves_three_decimal_precision()
    {
        var line = StocktakeLine.Create(Guid.NewGuid(), CreateItem(2.125m), UtcNow);

        line.RecordCount(2.1m, "Evaporation loss", ActorUserId, UtcNow);

        Assert.Equal(-0.025m, line.Variance);
    }

    [Fact]
    public void RecordCount_rejects_more_than_three_decimal_places()
    {
        var line = StocktakeLine.Create(Guid.NewGuid(), CreateItem(1m), UtcNow);

        Assert.Throws<StocktakeInvariantException>(() =>
            line.RecordCount(1.0001m, "Reason", ActorUserId, UtcNow));
    }

    [Fact]
    public void RecordCount_rejects_a_negative_count()
    {
        var line = StocktakeLine.Create(Guid.NewGuid(), CreateItem(1m), UtcNow);

        Assert.Throws<StocktakeInvariantException>(() =>
            line.RecordCount(-1m, "Reason", ActorUserId, UtcNow));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void RecordCount_requires_a_reason_for_nonzero_variance(string? reason)
    {
        var line = StocktakeLine.Create(Guid.NewGuid(), CreateItem(5m), UtcNow);

        Assert.Throws<StocktakeInvariantException>(() =>
            line.RecordCount(4m, reason, ActorUserId, UtcNow));
    }

    [Fact]
    public void RecordCount_does_not_require_a_reason_for_zero_variance()
    {
        var line = StocktakeLine.Create(Guid.NewGuid(), CreateItem(5m), UtcNow);

        line.RecordCount(5m, null, ActorUserId, UtcNow);

        Assert.Equal(0m, line.Variance);
        Assert.Null(line.VarianceReason);
    }

    [Fact]
    public void RecordCount_with_null_clears_the_count()
    {
        var line = StocktakeLine.Create(Guid.NewGuid(), CreateItem(5m), UtcNow);
        line.RecordCount(3m, "Damaged", ActorUserId, UtcNow);

        line.RecordCount(null, null, ActorUserId, UtcNow);

        Assert.Null(line.CountedQuantity);
        Assert.Null(line.Variance);
        Assert.Null(line.VarianceReason);
        Assert.Null(line.CountedByUserId);
    }

    [Fact]
    public void RefreshBaseline_recaptures_the_baseline_and_invalidates_the_count()
    {
        var item = CreateItem(10m);
        var line = StocktakeLine.Create(Guid.NewGuid(), item, UtcNow);
        line.RecordCount(8m, "Missing", ActorUserId, UtcNow);
        item.ReduceStock(3m);

        line.RefreshBaseline(item, UtcNow.AddHours(1));

        Assert.Equal(7m, line.BaselineQuantity);
        Assert.Equal(item.StockConcurrencyToken, line.BaselineStockConcurrencyToken);
        Assert.Equal(UtcNow.AddHours(1), line.BaselineCapturedUtc);
        Assert.Null(line.CountedQuantity);
        Assert.Null(line.VarianceReason);
    }

    [Fact]
    public void RecordChange_replaces_the_concurrency_token()
    {
        var stocktake = Stocktake.Create(null, ActorUserId, UtcNow);
        var originalToken = stocktake.ConcurrencyToken;

        stocktake.RecordChange(ActorUserId, UtcNow.AddMinutes(1));

        Assert.NotEqual(originalToken, stocktake.ConcurrencyToken);
        Assert.Equal(ActorUserId, stocktake.LastModifiedByUserId);
        Assert.Equal(UtcNow.AddMinutes(1), stocktake.LastModifiedUtc);
    }

    [Fact]
    public void MarkPosted_requires_every_line_to_be_counted()
    {
        var stocktake = Stocktake.Create(null, ActorUserId, UtcNow);
        var counted = StocktakeLine.Create(stocktake.Id, CreateItem(1m), UtcNow);
        var uncounted = StocktakeLine.Create(stocktake.Id, CreateItem(1m), UtcNow);
        counted.RecordCount(1m, null, ActorUserId, UtcNow);

        Assert.Throws<StocktakeInvariantException>(() =>
            stocktake.MarkPosted([counted, uncounted], ActorUserId, UtcNow));
        Assert.Equal(StocktakeStatus.Draft, stocktake.Status);
    }

    [Fact]
    public void MarkPosted_records_actor_and_timestamp()
    {
        var stocktake = Stocktake.Create(null, ActorUserId, UtcNow);
        var line = StocktakeLine.Create(stocktake.Id, CreateItem(1m), UtcNow);
        line.RecordCount(1m, null, ActorUserId, UtcNow);

        stocktake.MarkPosted([line], ActorUserId, UtcNow.AddMinutes(5));

        Assert.Equal(StocktakeStatus.Posted, stocktake.Status);
        Assert.Equal(ActorUserId, stocktake.PostedByUserId);
        Assert.Equal(UtcNow.AddMinutes(5), stocktake.PostedUtc);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(" ")]
    public void Cancel_requires_a_reason(string? reason)
    {
        var stocktake = Stocktake.Create(null, ActorUserId, UtcNow);

        Assert.Throws<StocktakeInvariantException>(() => stocktake.Cancel(reason!, ActorUserId, UtcNow));
        Assert.Equal(StocktakeStatus.Draft, stocktake.Status);
    }

    [Fact]
    public void Posted_stocktake_can_no_longer_be_changed()
    {
        var stocktake = Stocktake.Create(null, ActorUserId, UtcNow);
        var line = StocktakeLine.Create(stocktake.Id, CreateItem(1m), UtcNow);
        line.RecordCount(1m, null, ActorUserId, UtcNow);
        stocktake.MarkPosted([line], ActorUserId, UtcNow);

        Assert.Throws<StocktakeNotDraftException>(() => stocktake.RecordChange(ActorUserId, UtcNow));
        Assert.Throws<StocktakeNotDraftException>(() => stocktake.Cancel("Undo", ActorUserId, UtcNow));
        Assert.Throws<StocktakeNotDraftException>(() => stocktake.MarkPosted([line], ActorUserId, UtcNow));
    }

    [Fact]
    public void Cancelled_stocktake_can_no_longer_be_changed()
    {
        var stocktake = Stocktake.Create(null, ActorUserId, UtcNow);
        stocktake.Cancel(" Wrong locker ", ActorUserId, UtcNow);

        Assert.Equal(StocktakeStatus.Cancelled, stocktake.Status);
        Assert.Equal("Wrong locker", stocktake.CancellationReason);
        Assert.Throws<StocktakeNotDraftException>(() => stocktake.RecordChange(ActorUserId, UtcNow));
        Assert.Throws<StocktakeNotDraftException>(() => stocktake.MarkPosted([], ActorUserId, UtcNow));
    }

    [Fact]
    public void Inventory_item_stock_token_changes_even_when_quantity_is_restored()
    {
        var item = CreateItem(5m);
        var originalStockToken = item.StockConcurrencyToken;
        var originalReservationToken = item.ReservationConcurrencyToken;

        item.IncreaseStock(1m);
        item.ReduceStock(1m);

        Assert.Equal(5m, item.Quantity);
        Assert.NotEqual(originalStockToken, item.StockConcurrencyToken);
        Assert.Equal(originalReservationToken, item.ReservationConcurrencyToken);
    }

    [Fact]
    public void Inventory_item_reservation_token_changes_without_touching_stock()
    {
        var item = CreateItem(5m);
        var originalStockToken = item.StockConcurrencyToken;
        var originalReservationToken = item.ReservationConcurrencyToken;

        item.MarkReservationsChanged();

        Assert.Equal(originalStockToken, item.StockConcurrencyToken);
        Assert.NotEqual(originalReservationToken, item.ReservationConcurrencyToken);
    }

    private static InventoryItem CreateItem(decimal quantity)
    {
        return InventoryItem.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Fuel filter",
            $"PN-{Guid.NewGuid():N}",
            quantity,
            1m,
            10m);
    }
}
