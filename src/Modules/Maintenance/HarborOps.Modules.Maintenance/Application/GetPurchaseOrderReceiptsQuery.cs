using HarborOps.Modules.Maintenance.Contracts;
using HarborOps.Modules.Maintenance.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Maintenance.Application;

public sealed class GetPurchaseOrderReceiptsQuery(DbContext dbContext)
{
    public async Task<GetPurchaseOrderReceiptsResponse> GetReceiptsAsync(
        Guid purchaseOrderId,
        GetPurchaseOrderReceiptsRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var errors = GetPurchaseOrderReceiptsValidator.Validate(request);
        if (errors.Count > 0)
        {
            throw new GetPurchaseOrderReceiptsValidationException(errors);
        }

        var pageNumber = request.PageNumber ?? GetPurchaseOrderReceiptsValidator.DefaultPageNumber;
        var pageSize = request.PageSize ?? GetPurchaseOrderReceiptsValidator.DefaultPageSize;

        await EnsureOrderExistsAsync(purchaseOrderId, cancellationToken);

        var receipts = dbContext.Set<PurchaseOrderReceipt>()
            .AsNoTracking()
            .Where(receipt => receipt.PurchaseOrderId == purchaseOrderId);

        var totalCount = await receipts.CountAsync(cancellationToken);

        var page = await receipts
            .OrderByDescending(receipt => receipt.ReceivedUtc)
            .ThenByDescending(receipt => receipt.RecordedUtc)
            .ThenBy(receipt => receipt.Id)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var items = await PurchaseOrderReceiptReader.ReadDetailsAsync(dbContext, page, cancellationToken);

        return new GetPurchaseOrderReceiptsResponse(items, pageNumber, pageSize, totalCount);
    }

    public async Task<PurchaseOrderReceiptDetails> GetReceiptAsync(
        Guid purchaseOrderId,
        Guid receiptId,
        CancellationToken cancellationToken)
    {
        await EnsureOrderExistsAsync(purchaseOrderId, cancellationToken);

        var receipt = await dbContext.Set<PurchaseOrderReceipt>()
            .AsNoTracking()
            .SingleOrDefaultAsync(
                candidate => candidate.Id == receiptId && candidate.PurchaseOrderId == purchaseOrderId,
                cancellationToken);

        if (receipt is null)
        {
            throw new PurchaseOrderReceiptNotFoundException(purchaseOrderId, receiptId);
        }

        var details = await PurchaseOrderReceiptReader.ReadDetailsAsync(dbContext, [receipt], cancellationToken);

        return details[0];
    }

    private async Task EnsureOrderExistsAsync(Guid purchaseOrderId, CancellationToken cancellationToken)
    {
        var exists = await dbContext.Set<PurchaseOrder>()
            .AsNoTracking()
            .AnyAsync(order => order.Id == purchaseOrderId, cancellationToken);

        if (!exists)
        {
            throw new PurchaseOrderNotFoundException(purchaseOrderId);
        }
    }
}
