using System;
using System.Collections.Generic;
using System.Text;

namespace HarborOps.Modules.Maintenance.Application;

internal sealed class ReplaceWorkOrderPartReservationsValidationException(
    IReadOnlyDictionary<string, string[]> errors)
    : Exception("Replace work order part reservations validation failed.")
{
    internal IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}

internal sealed class GetInventoryReplenishmentValidationException(
    IReadOnlyDictionary<string, string[]> errors)
    : Exception("Get inventory replenishment validation failed.")
{
    internal IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}

internal sealed class WorkOrderPartReservationItemsNotFoundException(
    IReadOnlyList<Guid> inventoryItemIds)
    : Exception(
        $"Inventory items {string.Join(", ", inventoryItemIds)} were not found in the maintenance module.")
{
    internal IReadOnlyList<Guid> InventoryItemIds { get; } = inventoryItemIds;
}

internal sealed class WorkOrderPartReservationConflictException(string reason)
    : Exception(reason)
{
}

internal sealed class WorkOrderPartReservationShortageException(
    Guid workOrderId,
    IReadOnlyList<Guid> inventoryItemIds)
    : Exception(
        $"Work order '{workOrderId}' cannot start because items {string.Join(", ", inventoryItemIds)} are not fully reserved.")
{
    internal Guid WorkOrderId { get; } = workOrderId;

    internal IReadOnlyList<Guid> InventoryItemIds { get; } = inventoryItemIds;
}
