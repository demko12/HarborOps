using System;
using System.Collections.Generic;
using System.Text;

namespace HarborOps.Modules.Maintenance.Contracts;

public sealed record StocktakeListItem(
    Guid StocktakeId,
    string Status,
    int LineCount,
    int CountedLineCount,
    int VarianceLineCount,
    Guid CreatedByUserId,
    DateTime CreatedUtc,
    DateTime? PostedUtc,
    DateTime? CancelledUtc,
    Guid ConcurrencyToken);
