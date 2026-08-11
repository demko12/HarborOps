using System;
using System.Collections.Generic;
using System.Text;

namespace HarborOps.Modules.Maintenance.Application;

internal sealed class RecordInventoryItemUsageValidationException(
    IReadOnlyDictionary<string, string[]> errors)
    : Exception("Record inventory item usage validation failed.")
{
    internal IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}

internal sealed class InventoryItemNotFoundException(Guid partId)
    : Exception($"Inventory item '{partId}' was not found.")
{
    internal Guid PartId { get; } = partId;
}

internal sealed class RecordInventoryItemUsageConflictException(string reason)
    : Exception(reason)
{
}
