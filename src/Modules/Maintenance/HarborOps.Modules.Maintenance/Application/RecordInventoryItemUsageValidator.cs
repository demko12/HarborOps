using System;
using System.Collections.Generic;
using System.Text;
using HarborOps.Modules.Maintenance.Contracts;

namespace HarborOps.Modules.Maintenance.Application;

internal static class RecordInventoryItemUsageValidator
{
    internal const string QuantityUsedField = "quantityUsed";

    internal static IReadOnlyDictionary<string, string[]> Validate(RecordInventoryItemUsageRequest request)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        if (request.QuantityUsed is null || request.QuantityUsed.Value <= 0)
        {
            errors[QuantityUsedField] = ["QuantityUsed must be greater than zero."];
        }

        return errors;
    }
}
