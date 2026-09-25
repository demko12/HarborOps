using HarborOps.Modules.Maintenance.Contracts;
using HarborOps.Modules.Maintenance.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Maintenance.Application;

public sealed class GetSupplierReturnsQuery(DbContext dbContext)
{
    public async Task<GetSupplierReturnsResponse> GetSupplierReturnsAsync(
        GetSupplierReturnsRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var errors = GetSupplierReturnsValidator.Validate(request);
        if (errors.Count > 0)
        {
            throw new GetSupplierReturnsValidationException(errors);
        }

        var pageNumber = request.PageNumber ?? GetSupplierReturnsValidator.DefaultPageNumber;
        var pageSize = request.PageSize ?? GetSupplierReturnsValidator.DefaultPageSize;

        var lines = dbContext.Set<SupplierReturnLine>().AsNoTracking();
        var suppliers = dbContext.Set<Supplier>().AsNoTracking();
        var supplierReturns = dbContext.Set<SupplierReturn>().AsNoTracking();

        if (!string.IsNullOrWhiteSpace(request.Status)
            && GetSupplierReturnsValidator.TryParseStatus(request.Status, out var status))
        {
            supplierReturns = supplierReturns.Where(supplierReturn => supplierReturn.Status == status);
        }

        if (request.PurchaseOrderId is { } purchaseOrderId)
        {
            supplierReturns = supplierReturns.Where(supplierReturn => supplierReturn.PurchaseOrderId == purchaseOrderId);
        }

        if (request.SupplierId is { } supplierId)
        {
            supplierReturns = supplierReturns.Where(supplierReturn => supplierReturn.SupplierId == supplierId);
        }

        var totalCount = await supplierReturns.CountAsync(cancellationToken);

        var rows = await supplierReturns
            .OrderByDescending(supplierReturn => supplierReturn.CreatedUtc)
            .ThenBy(supplierReturn => supplierReturn.Id)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .Select(supplierReturn => new
            {
                supplierReturn.Id,
                supplierReturn.PurchaseOrderId,
                supplierReturn.SupplierId,
                SupplierName = suppliers
                    .Where(supplier => supplier.Id == supplierReturn.SupplierId)
                    .Select(supplier => supplier.Name)
                    .FirstOrDefault(),
                supplierReturn.Status,
                LineCount = lines.Count(line => line.SupplierReturnId == supplierReturn.Id),
                TotalQuantity = lines
                    .Where(line => line.SupplierReturnId == supplierReturn.Id)
                    .Sum(line => (decimal?)line.Quantity),
                supplierReturn.CreatedByUserId,
                supplierReturn.CreatedUtc,
                supplierReturn.PostedUtc,
                supplierReturn.CancelledUtc,
                supplierReturn.ConcurrencyToken,
            })
            .ToListAsync(cancellationToken);

        var items = rows
            .Select(row => new SupplierReturnListItem(
                row.Id,
                row.PurchaseOrderId,
                row.SupplierId,
                row.SupplierName ?? string.Empty,
                row.Status.ToString(),
                row.LineCount,
                row.TotalQuantity ?? 0m,
                row.CreatedByUserId,
                row.CreatedUtc,
                row.PostedUtc,
                row.CancelledUtc,
                row.ConcurrencyToken))
            .ToList();

        return new GetSupplierReturnsResponse(items, pageNumber, pageSize, totalCount);
    }
}
