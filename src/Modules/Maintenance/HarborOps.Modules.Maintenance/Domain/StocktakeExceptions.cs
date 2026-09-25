using System;
using System.Collections.Generic;
using System.Text;

namespace HarborOps.Modules.Maintenance.Domain;

internal sealed class StocktakeInvariantException(string reason)
    : Exception(reason)
{
}

internal sealed class StocktakeNotDraftException(Guid stocktakeId, StocktakeStatus currentStatus)
    : Exception($"Stocktake '{stocktakeId}' is {currentStatus} and can no longer be changed.")
{
    internal Guid StocktakeId { get; } = stocktakeId;

    internal StocktakeStatus CurrentStatus { get; } = currentStatus;
}
