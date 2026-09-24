using System;
using System.Collections.Generic;
using System.Text;

namespace HarborOps.Modules.Maintenance.Contracts;

public sealed record StocktakeDetails(
    Guid StocktakeId,
    string Status,
    string? Notes,
    Guid ConcurrencyToken,
    Guid CreatedByUserId,
    DateTime CreatedUtc,
    Guid? LastModifiedByUserId,
    DateTime? LastModifiedUtc,
    Guid? PostedByUserId,
    DateTime? PostedUtc,
    Guid? CancelledByUserId,
    DateTime? CancelledUtc,
    string? CancellationReason,
    IReadOnlyList<StocktakeLineItem> Lines,
    IReadOnlyList<InventoryAdjustmentItem> Adjustments);
