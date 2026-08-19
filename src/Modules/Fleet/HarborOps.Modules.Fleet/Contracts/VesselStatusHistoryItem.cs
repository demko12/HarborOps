using System;
using System.Collections.Generic;
using System.Text;

namespace HarborOps.Modules.Fleet.Contracts;

public sealed record VesselStatusHistoryItem(
    Guid Id,
    string Status,
    string ChangedBy,
    string? Notes,
    DateTime ChangedAtUtc);
