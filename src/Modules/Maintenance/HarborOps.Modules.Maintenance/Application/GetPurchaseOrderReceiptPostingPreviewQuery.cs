using HarborOps.Modules.Maintenance.Contracts;
using HarborOps.Modules.Maintenance.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Maintenance.Application;

// Read-only: evaluates a receipt with the posting rules without changing stock or the order.
public sealed class GetPurchaseOrderReceiptPostingPreviewQuery(DbContext dbContext, TimeProvider timeProvider)
{
    public async Task<PurchaseOrderReceiptPostingPreview> GetPreviewAsync(
        Guid purchaseOrderId,
        PreviewPurchaseOrderReceiptRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var errors = PreviewPurchaseOrderReceiptValidator.Validate(request, timeProvider.GetUtcNow().UtcDateTime);
        if (errors.Count > 0)
        {
            throw new PreviewPurchaseOrderReceiptValidationException(errors);
        }

        var (purchaseOrder, orderLines) = await ReadOrderAsync(purchaseOrderId, cancellationToken);

        var lineErrors = PurchaseOrderReceiptLinesValidator.ValidateAgainstOrder(request.Lines!, orderLines);
        if (lineErrors.Count > 0)
        {
            throw new PreviewPurchaseOrderReceiptValidationException(lineErrors);
        }

        var requestedQuantities = request.Lines!.ToDictionary(
            entry => entry.PurchaseOrderLineId!.Value,
            entry => entry.Quantity!.Value);

        return await EvaluateAsync(purchaseOrder, orderLines, requestedQuantities, cancellationToken);
    }

    public async Task<PurchaseOrderReceiptPostingPreview> GetRemainingPreviewAsync(
        Guid purchaseOrderId,
        CancellationToken cancellationToken)
    {
        var (purchaseOrder, orderLines) = await ReadOrderAsync(purchaseOrderId, cancellationToken);

        return await EvaluateAsync(
            purchaseOrder,
            orderLines,
            PurchaseOrderReceiptPostingEvaluator.Outstanding(orderLines.Values),
            cancellationToken);
    }

    private async Task<(PurchaseOrder PurchaseOrder, Dictionary<Guid, PurchaseOrderLine> Lines)> ReadOrderAsync(
        Guid purchaseOrderId,
        CancellationToken cancellationToken)
    {
        var purchaseOrder = await dbContext.Set<PurchaseOrder>()
            .AsNoTracking()
            .SingleOrDefaultAsync(order => order.Id == purchaseOrderId, cancellationToken);

        if (purchaseOrder is null)
        {
            throw new PurchaseOrderNotFoundException(purchaseOrderId);
        }

        var orderLines = await dbContext.Set<PurchaseOrderLine>()
            .AsNoTracking()
            .Where(line => line.PurchaseOrderId == purchaseOrderId)
            .ToDictionaryAsync(line => line.Id, cancellationToken);

        return (purchaseOrder, orderLines);
    }

    private async Task<PurchaseOrderReceiptPostingPreview> EvaluateAsync(
        PurchaseOrder purchaseOrder,
        Dictionary<Guid, PurchaseOrderLine> orderLines,
        IReadOnlyDictionary<Guid, decimal> requestedQuantities,
        CancellationToken cancellationToken)
    {
        var receivedLines = orderLines.Values.Where(line => requestedQuantities.ContainsKey(line.Id));
        var items = await PurchaseOrderReceiptReader.ReadItemsAsync(dbContext, receivedLines, false, cancellationToken);

        var evaluation = await PurchaseOrderReceiptPostingEvaluator.EvaluateAsync(
            dbContext,
            purchaseOrder,
            orderLines.Values,
            requestedQuantities,
            items,
            cancellationToken);

        return new PurchaseOrderReceiptPostingPreview(
            purchaseOrder.Id,
            purchaseOrder.Status.ToString(),
            purchaseOrder.ConcurrencyToken,
            evaluation.PreviewToken,
            evaluation.Blockers.Count == 0,
            evaluation.ResultingStatus.ToString(),
            evaluation.Lines,
            evaluation.Blockers,
            timeProvider.GetUtcNow().UtcDateTime);
    }
}
