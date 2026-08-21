using System;
using System.Collections.Generic;
using System.Text;

namespace HarborOps.Modules.Maintenance.Application;

internal sealed class RegisterSupplierValidationException(
    IReadOnlyDictionary<string, string[]> errors)
    : Exception("Register supplier validation failed.")
{
    internal IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}

internal sealed class GetSuppliersValidationException(
    IReadOnlyDictionary<string, string[]> errors)
    : Exception("Get suppliers validation failed.")
{
    internal IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}

internal sealed class CreatePurchaseOrderValidationException(
    IReadOnlyDictionary<string, string[]> errors)
    : Exception("Create purchase order validation failed.")
{
    internal IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}

internal sealed class GetPurchaseOrdersValidationException(
    IReadOnlyDictionary<string, string[]> errors)
    : Exception("Get purchase orders validation failed.")
{
    internal IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}

internal sealed class SupplierNotFoundException(Guid supplierId)
    : Exception($"Supplier '{supplierId}' was not found.")
{
    internal Guid SupplierId { get; } = supplierId;
}

internal sealed class PurchaseOrderNotFoundException(Guid purchaseOrderId)
    : Exception($"Purchase order '{purchaseOrderId}' was not found.")
{
    internal Guid PurchaseOrderId { get; } = purchaseOrderId;
}

internal sealed class PurchaseOrderItemsNotFoundException(IReadOnlyList<Guid> inventoryItemIds)
    : Exception($"Inventory items {string.Join(", ", inventoryItemIds)} were not found in the maintenance module.")
{
    internal IReadOnlyList<Guid> InventoryItemIds { get; } = inventoryItemIds;
}

internal sealed class PurchaseOrderConflictException(string reason)
    : Exception(reason)
{
}
