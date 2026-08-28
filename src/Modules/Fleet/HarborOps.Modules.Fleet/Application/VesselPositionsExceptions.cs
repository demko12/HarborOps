using System;
using System.Collections.Generic;
using System.Text;

namespace HarborOps.Modules.Fleet.Application;

internal sealed class VesselPositionsValidationException(
    IReadOnlyDictionary<string, string[]> errors)
    : Exception("Vessel positions validation failed.")
{
    internal IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}
