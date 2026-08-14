using System;
using System.Collections.Generic;
using System.Text;

namespace HarborOps.Modules.Maintenance.Application;

internal sealed class GetInventoryItemsValidationException(
    IReadOnlyDictionary<string, string[]> errors)
    : Exception("Get inventory items validation failed.")
{
    internal IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}
