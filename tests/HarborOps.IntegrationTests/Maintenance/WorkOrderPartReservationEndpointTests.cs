using System.Net;
using System.Net.Http.Json;
using HarborOps.Infrastructure.Persistence;
using HarborOps.IntegrationTests.Authentication;
using HarborOps.Modules.Identity.Contracts;
using HarborOps.Modules.Maintenance.Contracts;
using HarborOps.Modules.Maintenance.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HarborOps.IntegrationTests.Maintenance;

public sealed class WorkOrderPartReservationEndpointTests : IntegrationTestBase
{
    private readonly HarborOpsApiFactory _application;

    public WorkOrderPartReservationEndpointTests(HarborOpsApiFactory application)
        : base(application)
    {
        _application = application;
    }

    [Fact]
    public async Task Put_returns_200_and_persists_the_reservation()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Maintenance.Manage);
        var workOrder = await SeedWorkOrderAsync();
        var filter = await SeedItemAsync("Main engine filter", "FILTER-001", quantity: 10m);

        using var response = await client.PutAsJsonAsync(
            ReservationsUri(workOrder.Id),
            new { reservations = new[] { new { inventoryItemId = filter.Id, quantity = 4m } } },
            CancellationToken.None);
        var payload = await response.Content.ReadFromJsonAsync<ReplaceWorkOrderPartReservationsResponse>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(payload);
        Assert.Equal(workOrder.Id, payload.WorkOrderId);
        Assert.Equal("Open", payload.Status);
        Assert.False(payload.HasShortages);

        var item = Assert.Single(payload.Items);

        Assert.Equal(filter.Id, item.InventoryItemId);
        Assert.Equal(4m, item.QuantityRequested);
        Assert.Equal(4m, item.QuantityReserved);
        Assert.Equal(0m, item.QuantityConsumed);
        Assert.Equal(10m, item.StockOnHand);
        Assert.Equal(6m, item.AvailableQuantity);
        Assert.Equal([filter.Id], await StoredItemIdsAsync(workOrder.Id));
    }

    [Fact]
    public async Task Put_leaves_stock_on_hand_untouched()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Maintenance.Manage);
        var workOrder = await SeedWorkOrderAsync();
        var filter = await SeedItemAsync("Main engine filter", "FILTER-001", quantity: 10m);

        using var response = await client.PutAsJsonAsync(
            ReservationsUri(workOrder.Id),
            new { reservations = new[] { new { inventoryItemId = filter.Id, quantity = 4m } } },
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(10m, await StoredStockAsync(filter.Id));
    }

    [Fact]
    public async Task Put_replaces_the_previous_set()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Maintenance.Manage);
        var workOrder = await SeedWorkOrderAsync();
        var kept = await SeedItemAsync("Bearing", "PN-1", quantity: 10m);
        var dropped = await SeedItemAsync("Filter", "PN-2", quantity: 10m);
        var added = await SeedItemAsync("Gasket", "PN-3", quantity: 10m);

        using var seed = await client.PutAsJsonAsync(
            ReservationsUri(workOrder.Id),
            new
            {
                reservations = new[]
                {
                    new { inventoryItemId = kept.Id, quantity = 2m },
                    new { inventoryItemId = dropped.Id, quantity = 3m },
                },
            },
            CancellationToken.None);
        seed.EnsureSuccessStatusCode();

        using var response = await client.PutAsJsonAsync(
            ReservationsUri(workOrder.Id),
            new
            {
                reservations = new[]
                {
                    new { inventoryItemId = kept.Id, quantity = 5m },
                    new { inventoryItemId = added.Id, quantity = 1m },
                },
            },
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal([kept.Id, added.Id], await StoredItemIdsAsync(workOrder.Id));
    }

    [Fact]
    public async Task Put_accepts_an_empty_selection_and_clears_the_reservations()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Maintenance.Manage);
        var workOrder = await SeedWorkOrderAsync();
        var filter = await SeedItemAsync("Main engine filter", "FILTER-001", quantity: 10m);
        await SeedReservationAsync(workOrder.Id, filter.Id, requested: 4m, available: 10m);

        using var response = await client.PutAsJsonAsync(
            ReservationsUri(workOrder.Id),
            new { reservations = Array.Empty<object>() },
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(await StoredItemIdsAsync(workOrder.Id));
    }

    [Fact]
    public async Task Put_reports_a_shortage_when_stock_falls_short()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Maintenance.Manage);
        var workOrder = await SeedWorkOrderAsync();
        var filter = await SeedItemAsync("Main engine filter", "FILTER-001", quantity: 3m);

        using var response = await client.PutAsJsonAsync(
            ReservationsUri(workOrder.Id),
            new { reservations = new[] { new { inventoryItemId = filter.Id, quantity = 5m } } },
            CancellationToken.None);
        var payload = await response.Content.ReadFromJsonAsync<ReplaceWorkOrderPartReservationsResponse>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(payload);
        Assert.True(payload.HasShortages);
        Assert.Equal(3m, payload.Items[0].QuantityReserved);
        Assert.Equal(2m, payload.Items[0].ShortageQuantity);
    }

    [Fact]
    public async Task Concurrent_reservations_never_exceed_the_stock_on_hand()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Maintenance.Manage);
        var first = await SeedWorkOrderAsync();
        var second = await SeedWorkOrderAsync();
        var filter = await SeedItemAsync("Main engine filter", "FILTER-001", quantity: 10m);

        using var firstResponse = await client.PutAsJsonAsync(
            ReservationsUri(first.Id),
            new { reservations = new[] { new { inventoryItemId = filter.Id, quantity = 8m } } },
            CancellationToken.None);
        firstResponse.EnsureSuccessStatusCode();

        using var response = await client.PutAsJsonAsync(
            ReservationsUri(second.Id),
            new { reservations = new[] { new { inventoryItemId = filter.Id, quantity = 5m } } },
            CancellationToken.None);
        var payload = await response.Content.ReadFromJsonAsync<ReplaceWorkOrderPartReservationsResponse>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(payload);
        Assert.Equal(2m, payload.Items[0].QuantityReserved);
        Assert.Equal(3m, payload.Items[0].ShortageQuantity);
        Assert.Equal(10m, await TotalHeldAsync(filter.Id));
    }

    [Fact]
    public async Task Put_returns_400_for_a_duplicated_item()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Maintenance.Manage);
        var workOrder = await SeedWorkOrderAsync();
        var filter = await SeedItemAsync("Main engine filter", "FILTER-001", quantity: 10m);

        using var response = await client.PutAsJsonAsync(
            ReservationsUri(workOrder.Id),
            new
            {
                reservations = new[]
                {
                    new { inventoryItemId = filter.Id, quantity = 1m },
                    new { inventoryItemId = filter.Id, quantity = 2m },
                },
            },
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(
            "reservations",
            await response.Content.ReadAsStringAsync(CancellationToken.None),
            StringComparison.Ordinal);
        Assert.Empty(await StoredItemIdsAsync(workOrder.Id));
    }

    [Fact]
    public async Task Put_returns_400_for_a_quantity_that_is_not_positive()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Maintenance.Manage);
        var workOrder = await SeedWorkOrderAsync();
        var filter = await SeedItemAsync("Main engine filter", "FILTER-001", quantity: 10m);

        using var response = await client.PutAsJsonAsync(
            ReservationsUri(workOrder.Id),
            new { reservations = new[] { new { inventoryItemId = filter.Id, quantity = 0m } } },
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(await StoredItemIdsAsync(workOrder.Id));
    }

    [Fact]
    public async Task Put_returns_400_when_the_selection_is_missing()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Maintenance.Manage);
        var workOrder = await SeedWorkOrderAsync();

        using var response = await client.PutAsJsonAsync(
            ReservationsUri(workOrder.Id),
            new { },
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Put_returns_404_for_an_unknown_work_order()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Maintenance.Manage);
        var filter = await SeedItemAsync("Main engine filter", "FILTER-001", quantity: 10m);

        using var response = await client.PutAsJsonAsync(
            ReservationsUri(Guid.NewGuid()),
            new { reservations = new[] { new { inventoryItemId = filter.Id, quantity = 1m } } },
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Put_returns_404_for_an_unknown_inventory_item()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Maintenance.Manage);
        var workOrder = await SeedWorkOrderAsync();
        var known = await SeedItemAsync("Main engine filter", "FILTER-001", quantity: 10m);

        using var response = await client.PutAsJsonAsync(
            ReservationsUri(workOrder.Id),
            new
            {
                reservations = new[]
                {
                    new { inventoryItemId = known.Id, quantity = 1m },
                    new { inventoryItemId = Guid.NewGuid(), quantity = 1m },
                },
            },
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(await StoredItemIdsAsync(workOrder.Id));
    }

    [Theory]
    [InlineData(WorkOrderStatus.InProgress)]
    [InlineData(WorkOrderStatus.Completed)]
    [InlineData(WorkOrderStatus.Canceled)]
    public async Task Put_returns_409_for_a_work_order_that_is_not_open(WorkOrderStatus status)
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Maintenance.Manage);
        var workOrder = await SeedWorkOrderAsync(status);
        var filter = await SeedItemAsync("Main engine filter", "FILTER-001", quantity: 10m);

        using var response = await client.PutAsJsonAsync(
            ReservationsUri(workOrder.Id),
            new { reservations = new[] { new { inventoryItemId = filter.Id, quantity = 1m } } },
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Empty(await StoredItemIdsAsync(workOrder.Id));
    }

    [Fact]
    public async Task Put_returns_409_when_a_consumed_reservation_would_be_removed()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Maintenance.Manage);
        var workOrder = await SeedWorkOrderAsync();
        var filter = await SeedItemAsync("Main engine filter", "FILTER-001", quantity: 10m);
        var gasket = await SeedItemAsync("Gasket", "PN-2", quantity: 10m);
        await SeedReservationAsync(workOrder.Id, filter.Id, requested: 4m, available: 10m);

        using var usage = await client.PostAsJsonAsync(
            UsageUri(workOrder.Id, filter.Id),
            new { quantityUsed = 2m },
            CancellationToken.None);
        usage.EnsureSuccessStatusCode();

        using var response = await client.PutAsJsonAsync(
            ReservationsUri(workOrder.Id),
            new { reservations = new[] { new { inventoryItemId = gasket.Id, quantity = 1m } } },
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal([filter.Id], await StoredItemIdsAsync(workOrder.Id));
    }

    [Fact]
    public async Task Put_returns_401_for_an_anonymous_client()
    {
        using var client = _application.CreateClient();

        using var response = await client.PutAsJsonAsync(
            ReservationsUri(Guid.NewGuid()),
            new { reservations = Array.Empty<object>() },
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Put_returns_403_with_only_the_read_permission()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Maintenance.Read);
        var workOrder = await SeedWorkOrderAsync();
        var filter = await SeedItemAsync("Main engine filter", "FILTER-001", quantity: 10m);

        using var response = await client.PutAsJsonAsync(
            ReservationsUri(workOrder.Id),
            new { reservations = new[] { new { inventoryItemId = filter.Id, quantity = 1m } } },
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(await StoredItemIdsAsync(workOrder.Id));
    }

    [Fact]
    public async Task Get_returns_200_with_the_reservation_details()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Maintenance.Read);
        var workOrder = await SeedWorkOrderAsync();
        var filter = await SeedItemAsync("Main engine filter", "FILTER-001", quantity: 10m);
        await SeedReservationAsync(workOrder.Id, filter.Id, requested: 4m, available: 10m);

        using var response = await client.GetAsync(ReservationsUri(workOrder.Id), CancellationToken.None);
        var payload = await response.Content.ReadFromJsonAsync<GetWorkOrderPartReservationsResponse>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(payload);
        Assert.Equal(workOrder.Id, payload.WorkOrderId);
        Assert.Equal("Open", payload.Status);
        Assert.Equal(["Start", "Cancel", "ReplaceEquipment"], payload.AllowedActions);
        Assert.False(payload.HasShortages);

        var item = Assert.Single(payload.Items);

        Assert.Equal("Main engine filter", item.Name);
        Assert.Equal("FILTER-001", item.PartNumber);
        Assert.Equal(4m, item.QuantityReserved);
        Assert.Equal(4m, item.QuantityRemaining);
        Assert.Equal(6m, item.AvailableQuantity);
    }

    [Fact]
    public async Task Get_returns_an_empty_list_for_a_work_order_without_reservations()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Maintenance.Read);
        var workOrder = await SeedWorkOrderAsync();

        using var response = await client.GetAsync(ReservationsUri(workOrder.Id), CancellationToken.None);
        var payload = await response.Content.ReadFromJsonAsync<GetWorkOrderPartReservationsResponse>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(payload);
        Assert.Empty(payload.Items);
        Assert.False(payload.HasShortages);
    }

    [Fact]
    public async Task Get_flags_a_shortage()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Maintenance.Read);
        var workOrder = await SeedWorkOrderAsync();
        var filter = await SeedItemAsync("Main engine filter", "FILTER-001", quantity: 1m);
        await SeedReservationAsync(workOrder.Id, filter.Id, requested: 4m, available: 1m);

        using var response = await client.GetAsync(ReservationsUri(workOrder.Id), CancellationToken.None);
        var payload = await response.Content.ReadFromJsonAsync<GetWorkOrderPartReservationsResponse>(
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(payload);
        Assert.True(payload.HasShortages);
        Assert.True(payload.Items[0].HasShortage);
        Assert.Equal(3m, payload.Items[0].ShortageQuantity);
    }

    [Fact]
    public async Task Get_returns_404_for_an_unknown_work_order()
    {
        using var client = await _application.CreateAuthenticatedClientWithPermissionsAsync(
            Permissions.Maintenance.Read);

        using var response = await client.GetAsync(ReservationsUri(Guid.NewGuid()), CancellationToken.None);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Get_returns_401_for_an_anonymous_client()
    {
        using var client = _application.CreateClient();

        using var response = await client.GetAsync(ReservationsUri(Guid.NewGuid()), CancellationToken.None);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_returns_403_without_a_maintenance_permission()
    {
        using var client = await _application.CreateAuthenticatedClientAsync(roleNames: []);
        var workOrder = await SeedWorkOrderAsync();

        using var response = await client.GetAsync(ReservationsUri(workOrder.Id), CancellationToken.None);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private static Uri ReservationsUri(Guid workOrderId)
    {
        return new Uri(
            $"/api/maintenance/work-orders/{workOrderId}/part-reservations",
            UriKind.Relative);
    }

    private static Uri UsageUri(Guid workOrderId, Guid partId)
    {
        return new Uri(
            $"/api/maintenance/work-orders/{workOrderId}/parts/{partId}/usage",
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
            "Filter is due for replacement",
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

    private async Task<IReadOnlyList<Guid>> StoredItemIdsAsync(Guid workOrderId)
    {
        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();

        return await (
            from reservation in dbContext.Set<WorkOrderPartReservation>().AsNoTracking()
            join item in dbContext.Set<InventoryItem>().AsNoTracking()
                on reservation.InventoryItemId equals item.Id
            where reservation.WorkOrderId == workOrderId
            orderby item.Name, item.PartNumber, item.Id
            select reservation.InventoryItemId)
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

    private async Task<decimal> TotalHeldAsync(Guid inventoryItemId)
    {
        await using var scope = _application.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<HarborOpsDbContext>();

        return await dbContext.Set<WorkOrderPartReservation>()
            .AsNoTracking()
            .Where(reservation => reservation.InventoryItemId == inventoryItemId)
            .SumAsync(
                reservation => reservation.QuantityReserved - reservation.QuantityConsumed,
                CancellationToken.None);
    }
}
