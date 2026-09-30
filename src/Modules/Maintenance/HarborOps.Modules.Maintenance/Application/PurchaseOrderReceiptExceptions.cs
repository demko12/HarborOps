using HarborOps.Modules.Maintenance.Contracts;

namespace HarborOps.Modules.Maintenance.Application;

internal sealed class PreviewPurchaseOrderReceiptValidationException(
    IReadOnlyDictionary<string, string[]> errors)
    : Exception("Preview purchase order receipt validation failed.")
{
    internal IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}

internal sealed class PostPurchaseOrderReceiptValidationException(
    IReadOnlyDictionary<string, string[]> errors)
    : Exception("Post purchase order receipt validation failed.")
{
    internal IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}

internal sealed class ReceiveRemainingPurchaseOrderValidationException(
    IReadOnlyDictionary<string, string[]> errors)
    : Exception("Receive remaining purchase order validation failed.")
{
    internal IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}

internal sealed class GetPurchaseOrderReceiptsValidationException(
    IReadOnlyDictionary<string, string[]> errors)
    : Exception("Get purchase order receipts validation failed.")
{
    internal IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}

internal sealed class PurchaseOrderReceiptNotFoundException(Guid purchaseOrderId, Guid receiptId)
    : Exception($"Receipt '{receiptId}' was not found on purchase order '{purchaseOrderId}'.")
{
    internal Guid ReceiptId { get; } = receiptId;
}

internal sealed class PurchaseOrderReceiptConcurrencyException(Guid purchaseOrderId)
    : Exception($"Purchase order '{purchaseOrderId}' was changed by another request. Reload it and try again.")
{
    internal Guid PurchaseOrderId { get; } = purchaseOrderId;
}

internal sealed class PurchaseOrderReceiptStalePreviewException(Guid purchaseOrderId)
    : Exception($"Purchase order '{purchaseOrderId}' or its inventory changed after the receipt preview. Review a new preview before posting.")
{
    internal Guid PurchaseOrderId { get; } = purchaseOrderId;
}

internal sealed class PurchaseOrderReceiptRequestConflictException(Guid clientRequestId)
    : Exception($"Client request id '{clientRequestId}' was already used for a different receipt. Use a new client request id for a new delivery.")
{
    internal Guid ClientRequestId { get; } = clientRequestId;
}

internal sealed class PurchaseOrderReceiptPostingBlockedException(IReadOnlyList<PurchaseOrderReceiptPostingBlocker> blockers)
    : Exception(string.Join(" ", blockers.Select(blocker => blocker.Message)))
{
    internal IReadOnlyList<PurchaseOrderReceiptPostingBlocker> Blockers { get; } = blockers;
}
