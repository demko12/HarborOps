using System.Net;
using System.Net.Http.Json;
using HarborOps.Infrastructure.Persistence;
using HarborOps.IntegrationTests.Authentication;
using HarborOps.Modules.Identity.Contracts;
using HarborOps.Modules.Maintenance.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HarborOps.IntegrationTests.Maintenance;

public sealed class ReleaseWorkOrderPartReservationsEndpointTests : IntegrationTestBase
{
    private readonly HarborOpsApiFactory _application;

    public ReleaseWorkOrderPartReservationsEndpointTests(HarborOpsApiFactory application)
        : base(application)
    {
        _application = application;
    }

    [Fact]
    public async Task Post_returns_204_and_clears_untouched_reservations()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Maintenance.Manage);
        var workOrder = await SeedWorkOrderAsync();
        var filter = await SeedItemAsync("Main engine filter", "FILTER-001", quantity: 10m);
        await SeedReservationAsync(workOrder.Id, filter.Id, requested: 4m, available: 10m);

        using var response = await client.PostAsync(
            ReleaseUri(workOrder.Id),
            null,
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Empty(await StoredReservationsAsync(workOrder.Id));
    }

    [Fact]
    public async Task Post_leaves_stock_on_hand_untouched()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Maintenance.Manage);
        var workOrder = await SeedWorkOrderAsync();
        var filter = await SeedItemAsync("Main engine filter", "FILTER-001", quantity: 10m);
        await SeedReservationAsync(workOrder.Id, filter.Id, requested: 4m, available: 10m);

        using var response = await client.PostAsync(
            ReleaseUri(workOrder.Id),
            null,
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(10m, await StoredStockAsync(filter.Id));
    }

    [Fact]
    public async Task Post_keeps_the_consumed_quantity()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Maintenance.Manage);
        var workOrder = await SeedWorkOrderAsync();
        var filter = await SeedItemAsync("Main engine filter", "FILTER-001", quantity: 10m);
        await SeedReservationAsync(workOrder.Id, filter.Id, requested: 4m, available: 10m);

        using var usage = await client.PostAsJsonAsync(
            new Uri(
                $"/api/maintenance/work-orders/{workOrder.Id}/parts/{filter.Id}/usage",
                UriKind.Relative),
            new { quantityUsed = 3m },
            CancellationToken.None);
        usage.EnsureSuccessStatusCode();

        using var response = await client.PostAsync(
            ReleaseUri(workOrder.Id),
            null,
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var stored = Assert.Single(await StoredReservationsAsync(workOrder.Id));

        Assert.Equal(3m, stored.QuantityRequested);
        Assert.Equal(3m, stored.QuantityReserved);
        Assert.Equal(3m, stored.QuantityConsumed);
        Assert.Equal(7m, await StoredStockAsync(filter.Id));
    }

    [Fact]
    public async Task Post_returns_204_for_a_canceled_work_order()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Maintenance.Manage);
        var workOrder = await SeedWorkOrderAsync(WorkOrderStatus.Canceled);
        var filter = await SeedItemAsync("Main engine filter", "FILTER-001", quantity: 10m);
        await SeedReservationAsync(workOrder.Id, filter.Id, requested: 4m, available: 10m);

        using var response = await client.PostAsync(
            ReleaseUri(workOrder.Id),
            null,
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Empty(await StoredReservationsAsync(workOrder.Id));
    }

    [Fact]
    public async Task Post_returns_204_when_there_is_nothing_to_release()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Maintenance.Manage);
        var workOrder = await SeedWorkOrderAsync();

        using var response = await client.PostAsync(
            ReleaseUri(workOrder.Id),
            null,
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Theory]
    [InlineData(WorkOrderStatus.InProgress)]
    [InlineData(WorkOrderStatus.Completed)]
    public async Task Post_returns_409_for_a_work_order_in_flight_or_finished(WorkOrderStatus status)
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Maintenance.Manage);
        var workOrder = await SeedWorkOrderAsync(status);
        var filter = await SeedItemAsync("Main engine filter", "FILTER-001", quantity: 10m);
        await SeedReservationAsync(workOrder.Id, filter.Id, requested: 4m, available: 10m);

        using var response = await client.PostAsync(
            ReleaseUri(workOrder.Id),
            null,
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Single(await StoredReservationsAsync(workOrder.Id));
    }

    [Fact]
    public async Task Post_returns_404_for_an_unknown_work_order()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Maintenance.Manage);

        using var response = await client.PostAsync(
            ReleaseUri(Guid.NewGuid()),
            null,
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Post_returns_401_for_an_anonymous_client()
    {
        using var client = _application.CreateClient();

        using var response = await client.PostAsync(
            ReleaseUri(Guid.NewGuid()),
            null,
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Post_returns_403_with_only_the_read_permission()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Maintenance.Read);
        var workOrder = await SeedWorkOrderAsync();
        var filter = await SeedItemAsync("Main engine filter", "FILTER-001", quantity: 10m);
        await SeedReservationAsync(workOrder.Id, filter.Id, requested: 4m, available: 10m);

        using var response = await client.PostAsync(
            ReleaseUri(workOrder.Id),
            null,
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Single(await StoredReservationsAsync(workOrder.Id));
    }

    private static Uri ReleaseUri(Guid workOrderId)
    {
        return new Uri(
            $"/api/maintenance/work-orders/{workOrderId}/part-reservations/release",
            UriKind.Relative);
    }

    private async Task<MaintenanceWorkOrder> SeedWorkOrderAsync(
        WorkOrderStatus status = WorkOrderStatus.Open)
    {
        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();

        var workOrder = MaintenanceWorkOrder.Create(
            Guid.NewGuid(),
            "Replace main engine filter",
            null,
            WorkOrderPriority.High,
            DateTime.UtcNow.AddDays(7));

        switch (status)
        {
            case WorkOrderStatus.InProgress:
                workOrder.Start();
                break;

            case WorkOrderStatus.Completed:
                workOrder.Start();
                workOrder.Complete(DateTime.UtcNow);
                break;

            case WorkOrderStatus.Canceled:
                workOrder.Cancel();
                break;

            default:
                break;
        }

        await dbContext.Set<MaintenanceWorkOrder>().AddAsync(workOrder, CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        return workOrder;
    }

    private async Task<InventoryItem> SeedItemAsync(string name, string partNumber, decimal quantity)
    {
        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();

        var item = InventoryItem.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            name,
            partNumber,
            quantity,
            minimumQuantity: 2m,
            unitCost: 150m);

        await dbContext.Set<InventoryItem>().AddAsync(item, CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        return item;
    }

    private async Task SeedReservationAsync(
        Guid workOrderId,
        Guid inventoryItemId,
        decimal requested,
        decimal available)
    {
        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();

        var reservation = WorkOrderPartReservation.Create(
            workOrderId,
            inventoryItemId,
            requested,
            available,
            DateTime.UtcNow);

        await dbContext.Set<WorkOrderPartReservation>().AddAsync(reservation, CancellationToken.None);
        await dbContext.SaveChangesAsync(CancellationToken.None);
    }

    private async Task<IReadOnlyList<WorkOrderPartReservation>> StoredReservationsAsync(Guid workOrderId)
    {
        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();

        return await dbContext.Set<WorkOrderPartReservation>()
            .AsNoTracking()
            .Where(reservation => reservation.WorkOrderId == workOrderId)
            .OrderBy(reservation => reservation.InventoryItemId)
            .ToListAsync(CancellationToken.None);
    }

    private async Task<decimal> StoredStockAsync(Guid inventoryItemId)
    {
        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();

        return await dbContext.Set<InventoryItem>()
            .AsNoTracking()
            .Where(item => item.Id == inventoryItemId)
            .Select(item => item.Quantity)
            .SingleAsync(CancellationToken.None);
    }
}
