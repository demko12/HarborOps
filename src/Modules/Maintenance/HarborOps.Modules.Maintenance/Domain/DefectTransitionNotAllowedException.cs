using System;
using System.Collections.Generic;
using System.Text;

namespace HarborOps.Modules.Maintenance.Domain;

internal sealed class DefectTransitionNotAllowedException(
    Guid defectId,
    DefectStatus currentStatus,
    DefectStatus requestedStatus)
    : Exception(
        $"Defect '{defectId}' cannot move from status '{currentStatus}' to '{requestedStatus}'.")
{
    internal Guid DefectId { get; } = defectId;

    internal DefectStatus CurrentStatus { get; } = currentStatus;

    internal DefectStatus RequestedStatus { get; } = requestedStatus;
}
