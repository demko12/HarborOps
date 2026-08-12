using System;
using System.Collections.Generic;
using System.Text;

namespace HarborOps.Modules.Fleet.Application;

internal sealed class VesselLookupValidationException(
    IReadOnlyDictionary<string, string[]> errors)
    : Exception("Vessel lookup validation failed.")
{
    internal IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}
