using System;
using System.Collections.Generic;
using System.Text;

namespace HarborOps.Modules.Fleet.Contracts;

public sealed record RecordVesselPositionResponse(
    Guid VesselId,
    decimal Latitude,
    decimal Longitude,
    DateTime ReportedAtUtc);
