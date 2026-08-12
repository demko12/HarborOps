using System;
using System.Collections.Generic;
using System.Text;

namespace HarborOps.Modules.Fleet.Contracts;

public sealed record VesselLookupResponse(IReadOnlyList<VesselLookupItem> Vessels);
