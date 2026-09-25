using HarborOps.Modules.Maintenance.Contracts;
using HarborOps.Modules.Maintenance.Domain;

namespace HarborOps.Modules.Maintenance.Application;

internal static class UpdateStocktakeCountsValidator
{
    internal const string ExpectedConcurrencyTokenField = "expectedConcurrencyToken";
    internal const string LinesField = "lines";

    internal static IReadOnlyDictionary<string, string[]> Validate(UpdateStocktakeCountsRequest request)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        if (request.ExpectedConcurrencyToken is null || request.ExpectedConcurrencyToken == Guid.Empty)
        {
            errors[ExpectedConcurrencyTokenField] = ["Expected concurrency token is required."];
        }

        if (request.Lines is null || request.Lines.Count == 0)
        {
            errors[LinesField] = ["At least one line is required."];
            return errors;
        }

        if (request.Lines.Count > Stocktake.MaxLines)
        {
            errors[LinesField] = [$"At most {Stocktake.MaxLines} lines can be submitted."];
            return errors;
        }

        var seenItemIds = new HashSet<Guid>();

        for (var index = 0; index < request.Lines.Count; index++)
        {
            var line = request.Lines[index];
            var prefix = $"{LinesField}[{index}]";

            if (line is null)
            {
                errors[prefix] = ["Line is required."];
                continue;
            }

            if (line.InventoryItemId is not { } itemId || itemId == Guid.Empty)
            {
                errors[$"{prefix}.inventoryItemId"] = ["Inventory item id is required."];
            }
            else if (!seenItemIds.Add(itemId))
            {
                errors[$"{prefix}.inventoryItemId"] = ["Each inventory item can be submitted only once."];
            }

            if (line.CountedQuantity is { } counted)
            {
                if (counted < 0 || counted > StocktakeLine.MaxQuantity)
                {
                    errors[$"{prefix}.countedQuantity"] = ["Counted quantity must be zero or greater."];
                }
                else if (!StocktakeLine.HasSupportedPrecision(counted))
                {
                    errors[$"{prefix}.countedQuantity"] =
                        [$"Counted quantity supports at most {StocktakeLine.QuantityScale} decimal places."];
                }
            }

            if (line.Reason is { Length: > Stocktake.ReasonMaxLength })
            {
                errors[$"{prefix}.reason"] = [$"Reason must not exceed {Stocktake.ReasonMaxLength} characters."];
            }
        }

        return errors;
    }
}
