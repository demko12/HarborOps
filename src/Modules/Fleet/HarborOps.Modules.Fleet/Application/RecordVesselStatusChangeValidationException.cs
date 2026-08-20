using System;
using System.Collections.Generic;
using System.Text;

namespace HarborOps.Modules.Fleet.Application;

internal sealed class RecordVesselStatusChangeValidationException(
    IReadOnlyDictionary<string, string[]> errors)
    : Exception("Record vessel status change validation failed.")
{
    internal IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}

internal sealed class VesselNotFoundException(Guid vesselId)
    : Exception($"Vessel '{vesselId}' was not found.")
{
    internal Guid VesselId { get; } = vesselId;
}

internal sealed class VesselStatusUnchangedException(Guid vesselId, string status)
    : Exception($"Vessel '{vesselId}' already has status '{status}'.")
{
    internal Guid VesselId { get; } = vesselId;

    internal string Status { get; } = status;
}
