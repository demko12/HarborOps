using System;
using System.Collections.Generic;
using System.Text;

namespace HarborOps.Modules.Maintenance.Contracts;

public sealed record ReplaceWorkOrderEquipmentRequest
{
    public IReadOnlyList<Guid>? EquipmentIds { get; init; }
}
