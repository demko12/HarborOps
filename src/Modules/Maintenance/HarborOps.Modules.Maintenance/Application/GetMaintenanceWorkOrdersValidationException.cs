using System;
using System.Collections.Generic;
using System.Text;

namespace HarborOps.Modules.Maintenance.Application;

internal sealed class GetMaintenanceWorkOrdersValidationException(
    IReadOnlyDictionary<string, string[]> errors)
    : Exception("Get maintenance work orders validation failed.")
{
    internal IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}
