using System;
using System.Collections.Generic;
using System.Text;

namespace HarborOps.Modules.Fleet.Application;

internal sealed class RecordVesselPositionValidationException(
    IReadOnlyDictionary<string, string[]> errors)
    : Exception("Record vessel position validation failed.")
{
    internal IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}
