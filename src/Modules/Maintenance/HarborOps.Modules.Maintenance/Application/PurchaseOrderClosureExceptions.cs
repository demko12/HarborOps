using HarborOps.Modules.Maintenance.Contracts;

namespace HarborOps.Modules.Maintenance.Application;

internal sealed class ClosePurchaseOrderValidationException(
    IReadOnlyDictionary<string, string[]> errors)
    : Exception("Close purchase order validation failed.")
{
    internal IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}

internal sealed class PurchaseOrderClosureNotFoundException(Guid purchaseOrderId)
    : Exception($"Purchase order '{purchaseOrderId}' has not been closed short.")
{
    internal Guid PurchaseOrderId { get; } = purchaseOrderId;
}

// Carries a freshly evaluated preview so the client can confirm the current quantities.
internal sealed class PurchaseOrderClosureConcurrencyException(
    Guid purchaseOrderId,
    PurchaseOrderClosurePreview refreshedPreview)
    : Exception($"Purchase order '{purchaseOrderId}' was changed by another request. Review the refreshed preview and try again.")
{
    internal Guid PurchaseOrderId { get; } = purchaseOrderId;

    internal PurchaseOrderClosurePreview RefreshedPreview { get; } = refreshedPreview;
}

internal sealed class PurchaseOrderClosureStalePreviewException(
    Guid purchaseOrderId,
    PurchaseOrderClosurePreview refreshedPreview)
    : Exception($"The quantities of purchase order '{purchaseOrderId}' changed after the closure preview. Review the refreshed preview before closing.")
{
    internal Guid PurchaseOrderId { get; } = purchaseOrderId;

    internal PurchaseOrderClosurePreview RefreshedPreview { get; } = refreshedPreview;
}

internal sealed class PurchaseOrderClosureRequestConflictException(Guid clientRequestId)
    : Exception($"Client request id '{clientRequestId}' was already used for a different closure. Use a new client request id for a new request.")
{
    internal Guid ClientRequestId { get; } = clientRequestId;
}

internal sealed class PurchaseOrderClosureBlockedException(IReadOnlyList<PurchaseOrderClosureBlocker> blockers)
    : Exception(string.Join(" ", blockers.Select(blocker => blocker.Message)))
{
    internal IReadOnlyList<PurchaseOrderClosureBlocker> Blockers { get; } = blockers;
}
