using System;
using System.Collections.Generic;
using System.Text;
using HarborOps.Modules.Maintenance.Contracts;
using HarborOps.Modules.Maintenance.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Maintenance.Application;

public sealed class GetPurchaseOrdersQuery(DbContext dbContext, TimeProvider timeProvider)
{
    public async Task<GetPurchaseOrdersResponse> GetPurchaseOrdersAsync(
        GetPurchaseOrdersRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var errors = GetPurchaseOrdersValidator.Validate(request);
        if (errors.Count > 0)
        {
            throw new GetPurchaseOrdersValidationException(errors);
        }

        var pageNumber = request.PageNumber ?? GetPurchaseOrdersValidator.DefaultPageNumber;
        var pageSize = request.PageSize ?? GetPurchaseOrdersValidator.DefaultPageSize;

        var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);

        var lines = dbContext.Set<PurchaseOrderLine>().AsNoTracking();
        var suppliers = dbContext.Set<Supplier>().AsNoTracking();
        var purchaseOrders = dbContext.Set<PurchaseOrder>().AsNoTracking();

        if (request.SupplierId is { } supplierId)
        {
            purchaseOrders = purchaseOrders.Where(order => order.SupplierId == supplierId);
        }

        if (!string.IsNullOrWhiteSpace(request.Status)
            && GetPurchaseOrdersValidator.TryParseStatus(request.Status, out var status))
        {
            purchaseOrders = purchaseOrders.Where(order => order.Status == status);
        }

        if (request.OrderDateFrom is { } orderDateFrom)
        {
            purchaseOrders = purchaseOrders.Where(order => order.OrderDate >= orderDateFrom);
        }

        if (request.OrderDateTo is { } orderDateTo)
        {
            purchaseOrders = purchaseOrders.Where(order => order.OrderDate <= orderDateTo);
        }

        if (request.ExpectedDeliveryFrom is { } deliveryFrom)
        {
            purchaseOrders = purchaseOrders.Where(order =>
                order.ExpectedDelivery != null && order.ExpectedDelivery >= deliveryFrom);
        }

        if (request.ExpectedDeliveryTo is { } deliveryTo)
        {
            purchaseOrders = purchaseOrders.Where(order =>
                order.ExpectedDelivery != null && order.ExpectedDelivery <= deliveryTo);
        }

        if (request.Overdue is { } overdue)
        {
            purchaseOrders = overdue
                ? purchaseOrders.Where(order =>
                    order.ExpectedDelivery != null
                    && order.ExpectedDelivery < today
                    && (order.Status == PurchaseOrderStatus.Draft
                        || order.Status == PurchaseOrderStatus.Ordered
                        || order.Status == PurchaseOrderStatus.PartiallyReceived))
                : purchaseOrders.Where(order =>
                    order.ExpectedDelivery == null
                    || order.ExpectedDelivery >= today
                    || (order.Status != PurchaseOrderStatus.Draft
                        && order.Status != PurchaseOrderStatus.Ordered
                        && order.Status != PurchaseOrderStatus.PartiallyReceived));
        }

        var totalCount = await purchaseOrders.CountAsync(cancellationToken);

        var rows = await purchaseOrders
            .OrderByDescending(order => order.OrderDate)
            .ThenBy(order => order.Id)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .Select(order => new
            {
                order.Id,
                order.SupplierId,
                SupplierName = suppliers
                    .Where(supplier => supplier.Id == order.SupplierId)
                    .Select(supplier => supplier.Name)
                    .FirstOrDefault(),
                order.OrderDate,
                order.ExpectedDelivery,
                order.Status,
                LineCount = lines.Count(line => line.PurchaseOrderId == order.Id),
                TotalCost = lines
                    .Where(line => line.PurchaseOrderId == order.Id)
                    .Sum(line => line.QuantityOrdered * line.UnitCost),
                QuantityOrdered = lines
                    .Where(line => line.PurchaseOrderId == order.Id)
                    .Sum(line => line.QuantityOrdered),
                QuantityReceived = lines
                    .Where(line => line.PurchaseOrderId == order.Id)
                    .Sum(line => line.ReceivedQuantity),
            })
            .ToListAsync(cancellationToken);

        var items = rows
            .Select(row => new PurchaseOrderListItem(
                row.Id,
                row.SupplierId,
                row.SupplierName ?? string.Empty,
                row.OrderDate,
                row.ExpectedDelivery,
                row.Status.ToString(),
                row.LineCount,
                row.TotalCost,
                PurchaseOrder.IsOverdue(row.ExpectedDelivery, row.Status, today),
                DaysUntilDelivery(row.ExpectedDelivery, today),
                row.QuantityOrdered,
                row.QuantityReceived,
                row.QuantityOrdered - row.QuantityReceived))
            .ToList();

        return new GetPurchaseOrdersResponse(items, pageNumber, pageSize, totalCount);
    }

    internal static int? DaysUntilDelivery(DateOnly? expectedDelivery, DateOnly today)
    {
        return expectedDelivery is { } deliveryDate ? deliveryDate.DayNumber - today.DayNumber : null;
    }
}
