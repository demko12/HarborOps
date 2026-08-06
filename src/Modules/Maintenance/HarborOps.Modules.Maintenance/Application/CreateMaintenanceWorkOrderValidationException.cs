using System;
using System.Collections.Generic;
using System.Text;

namespace HarborOps.Modules.Maintenance.Application;

internal sealed class CreateMaintenanceWorkOrderValidationException(
    IReadOnlyDictionary<string, string[]> errors)
    : Exception("Create maintenance work order validation failed.")
{
    internal IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}
