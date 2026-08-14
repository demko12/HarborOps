using System;
using System.Collections.Generic;
using System.Text;
using HarborOps.Modules.Maintenance.Contracts;
using HarborOps.Modules.Maintenance.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Maintenance.Application;

public sealed class GetInventoryItemsQuery(DbContext dbContext)
{
    public async Task<GetInventoryItemsResponse> GetInventoryItemsAsync(
        GetInventoryItemsRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var errors = GetInventoryItemsValidator.Validate(request);
        if (errors.Count > 0)
        {
            throw new GetInventoryItemsValidationException(errors);
        }

        var pageNumber = request.PageNumber ?? GetInventoryItemsValidator.DefaultPageNumber;
        var pageSize = request.PageSize ?? GetInventoryItemsValidator.DefaultPageSize;

        var inventoryItems = dbContext.Set<InventoryItem>().AsNoTracking();

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var search = request.Search.Trim();
            inventoryItems = inventoryItems.Where(item =>
                item.Name.Contains(search)
                || item.PartNumber.Contains(search));
        }

        if (request.LowStock.HasValue)
        {
            if (request.LowStock.Value)
            {
                inventoryItems = inventoryItems.Where(item => item.Quantity <= item.MinimumQuantity);
            }
            else
            {
                inventoryItems = inventoryItems.Where(item => item.Quantity > item.MinimumQuantity);
            }
        }

        var totalCount = await inventoryItems.CountAsync(cancellationToken);

        var rows = await inventoryItems
            .OrderBy(item => item.Name)
            .ThenBy(item => item.PartNumber)
            .ThenBy(item => item.Id)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .Select(item => new InventoryItemListItem(
                item.Id,
                item.Name,
                item.PartNumber,
                item.Quantity,
                item.MinimumQuantity,
                item.UnitCost,
                item.CategoryId,
                item.UnitId,
                item.SupplierId))
            .ToListAsync(cancellationToken);

        return new GetInventoryItemsResponse(rows, pageNumber, pageSize, totalCount);

    }
}
