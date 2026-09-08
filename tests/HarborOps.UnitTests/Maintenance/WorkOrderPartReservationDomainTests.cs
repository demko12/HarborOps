using HarborOps.Modules.Maintenance.Domain;

namespace HarborOps.UnitTests.Maintenance;

public sealed class WorkOrderPartReservationDomainTests
{
    private static readonly DateTime UtcNow = new(2026, 6, 15, 9, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Create_reserves_the_whole_request_when_stock_covers_it()
    {
        var reservation = WorkOrderPartReservation.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            quantityRequested: 4m,
            availableQuantity: 10m,
            UtcNow);

        Assert.Equal(4m, reservation.QuantityRequested);
        Assert.Equal(4m, reservation.QuantityReserved);
        Assert.Equal(0m, reservation.QuantityConsumed);
        Assert.Equal(4m, reservation.QuantityRemaining);
        Assert.Equal(0m, reservation.ShortageQuantity);
        Assert.False(reservation.HasShortage);
    }

    [Fact]
    public void Create_clamps_the_reservation_to_the_available_quantity()
    {
        var reservation = WorkOrderPartReservation.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            quantityRequested: 5m,
            availableQuantity: 3m,
            UtcNow);

        Assert.Equal(5m, reservation.QuantityRequested);
        Assert.Equal(3m, reservation.QuantityReserved);
        Assert.Equal(2m, reservation.ShortageQuantity);
        Assert.True(reservation.HasShortage);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-4)]
    public void Create_reserves_nothing_when_stock_is_exhausted(int availableQuantity)
    {
        var reservation = WorkOrderPartReservation.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            quantityRequested: 5m,
            availableQuantity,
            UtcNow);

        Assert.Equal(0m, reservation.QuantityReserved);
        Assert.Equal(5m, reservation.ShortageQuantity);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Create_rejects_a_request_that_is_not_positive(int quantityRequested)
    {
        Assert.Throws<WorkOrderPartReservationInvariantException>(() =>
            WorkOrderPartReservation.Create(
                Guid.NewGuid(),
                Guid.NewGuid(),
                quantityRequested,
                availableQuantity: 10m,
                UtcNow));
    }

    [Fact]
    public void Create_stamps_both_timestamps_from_the_caller()
    {
        var reservation = CreateReservation(requested: 2m, available: 10m);

        Assert.Equal(UtcNow, reservation.CreatedUtc);
        Assert.Equal(UtcNow, reservation.UpdatedUtc);
    }

    [Fact]
    public void ChangeRequestedQuantity_grows_the_reservation_when_stock_allows()
    {
        var reservation = CreateReservation(requested: 2m, available: 10m);

        reservation.ChangeRequestedQuantity(6m, availableQuantity: 8m, UtcNow);

        Assert.Equal(6m, reservation.QuantityRequested);
        Assert.Equal(6m, reservation.QuantityReserved);
        Assert.False(reservation.HasShortage);
    }

    [Fact]
    public void ChangeRequestedQuantity_shrinks_the_reservation()
    {
        var reservation = CreateReservation(requested: 6m, available: 10m);

        reservation.ChangeRequestedQuantity(2m, availableQuantity: 10m, UtcNow);

        Assert.Equal(2m, reservation.QuantityRequested);
        Assert.Equal(2m, reservation.QuantityReserved);
    }

    [Fact]
    public void ChangeRequestedQuantity_reports_a_shortage_when_stock_falls_short()
    {
        var reservation = CreateReservation(requested: 2m, available: 10m);

        reservation.ChangeRequestedQuantity(9m, availableQuantity: 4m, UtcNow);

        Assert.Equal(9m, reservation.QuantityRequested);
        Assert.Equal(4m, reservation.QuantityReserved);
        Assert.Equal(5m, reservation.ShortageQuantity);
        Assert.True(reservation.HasShortage);
    }

    [Fact]
    public void ChangeRequestedQuantity_never_releases_what_was_already_consumed()
    {
        var reservation = CreateReservation(requested: 5m, available: 10m);
        reservation.Consume(2m, UtcNow);

        reservation.ChangeRequestedQuantity(6m, availableQuantity: 1m, UtcNow);

        Assert.Equal(6m, reservation.QuantityRequested);
        Assert.Equal(3m, reservation.QuantityReserved);
        Assert.Equal(2m, reservation.QuantityConsumed);
        Assert.Equal(1m, reservation.QuantityRemaining);
        Assert.Equal(3m, reservation.ShortageQuantity);
    }

    [Fact]
    public void ChangeRequestedQuantity_rejects_a_request_below_the_consumed_quantity()
    {
        var reservation = CreateReservation(requested: 5m, available: 10m);
        reservation.Consume(3m, UtcNow);

        var exception = Assert.Throws<WorkOrderPartReservationInvariantException>(() =>
            reservation.ChangeRequestedQuantity(2m, availableQuantity: 10m, UtcNow));

        Assert.Contains("consumed quantity", exception.Message, StringComparison.Ordinal);
        Assert.Equal(5m, reservation.QuantityRequested);
    }

    [Fact]
    public void ChangeRequestedQuantity_moves_only_the_updated_timestamp()
    {
        var reservation = CreateReservation(requested: 2m, available: 10m);
        var later = UtcNow.AddHours(3);

        reservation.ChangeRequestedQuantity(3m, availableQuantity: 10m, later);

        Assert.Equal(UtcNow, reservation.CreatedUtc);
        Assert.Equal(later, reservation.UpdatedUtc);
    }

    [Fact]
    public void Consume_moves_quantity_from_remaining_to_consumed()
    {
        var reservation = CreateReservation(requested: 5m, available: 10m);

        reservation.Consume(2m, UtcNow);

        Assert.Equal(5m, reservation.QuantityReserved);
        Assert.Equal(2m, reservation.QuantityConsumed);
        Assert.Equal(3m, reservation.QuantityRemaining);
    }

    [Fact]
    public void Consume_can_be_repeated_up_to_the_reserved_quantity()
    {
        var reservation = CreateReservation(requested: 5m, available: 10m);

        reservation.Consume(2m, UtcNow);
        reservation.Consume(3m, UtcNow);

        Assert.Equal(5m, reservation.QuantityConsumed);
        Assert.Equal(0m, reservation.QuantityRemaining);
    }

    [Fact]
    public void Consume_rejects_more_than_the_remaining_reserved_quantity()
    {
        var reservation = CreateReservation(requested: 5m, available: 10m);
        reservation.Consume(4m, UtcNow);

        var exception = Assert.Throws<WorkOrderPartReservationInvariantException>(() =>
            reservation.Consume(2m, UtcNow));

        Assert.Contains(
            "exceeds the remaining reserved quantity",
            exception.Message,
            StringComparison.Ordinal);
        Assert.Equal(4m, reservation.QuantityConsumed);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-2)]
    public void Consume_rejects_a_quantity_that_is_not_positive(int quantity)
    {
        var reservation = CreateReservation(requested: 5m, available: 10m);

        Assert.Throws<WorkOrderPartReservationInvariantException>(() =>
            reservation.Consume(quantity, UtcNow));
    }

    [Fact]
    public void ReleaseRemaining_clears_a_reservation_that_was_never_consumed()
    {
        var reservation = CreateReservation(requested: 5m, available: 10m);

        reservation.ReleaseRemaining(UtcNow);

        Assert.Equal(0m, reservation.QuantityRequested);
        Assert.Equal(0m, reservation.QuantityReserved);
        Assert.Equal(0m, reservation.QuantityRemaining);
        Assert.False(reservation.HasShortage);
    }

    [Fact]
    public void ReleaseRemaining_keeps_what_was_already_consumed()
    {
        var reservation = CreateReservation(requested: 5m, available: 10m);
        reservation.Consume(2m, UtcNow);

        reservation.ReleaseRemaining(UtcNow);

        Assert.Equal(2m, reservation.QuantityRequested);
        Assert.Equal(2m, reservation.QuantityReserved);
        Assert.Equal(2m, reservation.QuantityConsumed);
        Assert.Equal(0m, reservation.QuantityRemaining);
        Assert.False(reservation.HasShortage);
    }

    [Fact]
    public void An_open_work_order_may_change_and_release_its_reservations()
    {
        var workOrder = CreateWorkOrder(WorkOrderStatus.Open);

        workOrder.EnsureReservationsCanChange();
        workOrder.EnsureReservationsCanRelease();
    }

    [Theory]
    [InlineData(WorkOrderStatus.InProgress)]
    [InlineData(WorkOrderStatus.Completed)]
    [InlineData(WorkOrderStatus.Canceled)]
    public void Only_an_open_work_order_may_change_its_reservations(WorkOrderStatus status)
    {
        var workOrder = CreateWorkOrder(status);

        var exception = Assert.Throws<WorkOrderReservationChangeNotAllowedException>(
            workOrder.EnsureReservationsCanChange);

        Assert.Equal(workOrder.Id, exception.WorkOrderId);
        Assert.Equal(status, exception.CurrentStatus);
    }

    [Fact]
    public void A_canceled_work_order_may_still_release_its_reservations()
    {
        var workOrder = CreateWorkOrder(WorkOrderStatus.Canceled);

        workOrder.EnsureReservationsCanRelease();
    }

    [Theory]
    [InlineData(WorkOrderStatus.InProgress)]
    [InlineData(WorkOrderStatus.Completed)]
    public void A_work_order_in_flight_or_finished_may_not_release_its_reservations(
        WorkOrderStatus status)
    {
        var workOrder = CreateWorkOrder(status);

        var exception = Assert.Throws<WorkOrderReservationReleaseNotAllowedException>(
            workOrder.EnsureReservationsCanRelease);

        Assert.Equal(workOrder.Id, exception.WorkOrderId);
        Assert.Equal(status, exception.CurrentStatus);
    }

    private static WorkOrderPartReservation CreateReservation(decimal requested, decimal available)
    {
        return WorkOrderPartReservation.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            requested,
            available,
            UtcNow);
    }

    private static MaintenanceWorkOrder CreateWorkOrder(WorkOrderStatus status)
    {
        var workOrder = MaintenanceWorkOrder.Create(
            Guid.NewGuid(),
            "Replace main engine filter",
            null,
            WorkOrderPriority.High,
            UtcNow.AddDays(7));

        switch (status)
        {
            case WorkOrderStatus.InProgress:
                workOrder.Start();
                break;

            case WorkOrderStatus.Completed:
                workOrder.Start();
                workOrder.Complete(UtcNow);
                break;

            case WorkOrderStatus.Canceled:
                workOrder.Cancel();
                break;

            default:
                break;
        }

        return workOrder;
    }
}
