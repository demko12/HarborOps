using System;
using System.Collections.Generic;
using System.Text;

namespace HarborOps.Modules.Maintenance.Application;

internal sealed class GetVesselMaintenanceOverviewValidationException(
    IReadOnlyDictionary<string, string[]> errors)
    : Exception("Get vessel maintenance overview validation failed.")
{
    internal IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}
