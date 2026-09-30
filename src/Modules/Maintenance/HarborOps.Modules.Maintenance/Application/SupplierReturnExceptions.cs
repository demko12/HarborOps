using HarborOps.Modules.Maintenance.Contracts;

namespace HarborOps.Modules.Maintenance.Application;

internal sealed class CreateSupplierReturnValidationException(
    IReadOnlyDictionary<string, string[]> errors)
    : Exception("Create supplier return validation failed.")
{
    internal IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}

internal sealed class UpdateSupplierReturnValidationException(
    IReadOnlyDictionary<string, string[]> errors)
    : Exception("Update supplier return validation failed.")
{
    internal IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}

internal sealed class GetSupplierReturnsValidationException(
    IReadOnlyDictionary<string, string[]> errors)
    : Exception("Get supplier returns validation failed.")
{
    internal IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}

internal sealed class CancelSupplierReturnValidationException(
    IReadOnlyDictionary<string, string[]> errors)
    : Exception("Cancel supplier return validation failed.")
{
    internal IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}

internal sealed class PostSupplierReturnValidationException(
    IReadOnlyDictionary<string, string[]> errors)
    : Exception("Post supplier return validation failed.")
{
    internal IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}

internal sealed class SupplierReturnNotFoundException(Guid supplierReturnId)
    : Exception($"Supplier return '{supplierReturnId}' was not found.")
{
    internal Guid SupplierReturnId { get; } = supplierReturnId;
}

internal sealed class SupplierReturnPurchaseOrderNotReceivedException(Guid purchaseOrderId, string currentStatus)
    : Exception($"Purchase order '{purchaseOrderId}' is {currentStatus}; only partially or fully received orders can be returned.")
{
    internal Guid PurchaseOrderId { get; } = purchaseOrderId;
}

internal sealed class SupplierReturnConcurrencyException(Guid supplierReturnId)
    : Exception($"Supplier return '{supplierReturnId}' was changed by another request. Reload it and try again.")
{
    internal Guid SupplierReturnId { get; } = supplierReturnId;
}

internal sealed class SupplierReturnStalePreviewException(Guid supplierReturnId)
    : Exception($"Inventory or returns for supplier return '{supplierReturnId}' changed after the posting preview. Review a new preview before posting.")
{
    internal Guid SupplierReturnId { get; } = supplierReturnId;
}

internal sealed class SupplierReturnPostingBlockedException(IReadOnlyList<SupplierReturnPostingBlocker> blockers)
    : Exception(string.Join(" ", blockers.Select(blocker => blocker.Message)))
{
    internal IReadOnlyList<SupplierReturnPostingBlocker> Blockers { get; } = blockers;
}
