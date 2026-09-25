using System;
using System.Collections.Generic;
using System.Text;

namespace HarborOps.Modules.Maintenance.Contracts;

public sealed record StocktakeLineItem(
    Guid InventoryItemId,
    string Name,
    string PartNumber,
    string UnitAbbreviation,
    decimal BaselineQuantity,
    DateTime BaselineCapturedUtc,
    decimal? CountedQuantity,
    decimal? Variance,
    string? VarianceReason,
    Guid? CountedByUserId,
    DateTime? CountedUtc);
