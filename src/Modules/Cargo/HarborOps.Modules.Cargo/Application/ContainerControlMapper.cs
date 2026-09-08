using HarborOps.Modules.Cargo.Contracts;
using HarborOps.Modules.Cargo.Domain;

namespace HarborOps.Modules.Cargo.Application;

internal static class ContainerControlMapper
{
    internal static ContainerControlDetails ToDetails(Container container, ContainerControl? control)
    {
        if (control is null)
        {
            return new ContainerControlDetails(
                container.Id,
                container.ContainerNumber,
                container.ContainerType,
                container.IsReefer,
                null,
                ContainerSealStatus.None.ToString(),
                ContainerControlState.Pending.ToString(),
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                false,
                ContainerControlTransitionNotAllowedException.SealNotIntactReason);
        }

        return new ContainerControlDetails(
            container.Id,
            container.ContainerNumber,
            container.ContainerType,
            container.IsReefer,
            control.SealNumber,
            control.SealStatus.ToString(),
            control.State.ToString(),
            control.LatestInspectionResult?.ToString(),
            control.LatestInspectionNotes,
            control.LatestInspectionTemperatureCelsius,
            control.LatestInspectedAtUtc,
            control.LatestInspectedByUserId,
            control.StateReason,
            control.StateChangedByUserId,
            control.StateChangedAtUtc,
            control.CanRelease,
            control.ReleaseBlocker);
    }

    internal static ContainerControlEventListItem ToListItem(ContainerControlEvent entry)
    {
        return new ContainerControlEventListItem(
            entry.Id,
            entry.Action.ToString(),
            entry.FromState.ToString(),
            entry.ToState.ToString(),
            entry.SealNumber,
            entry.InspectionResult?.ToString(),
            entry.TemperatureCelsius,
            entry.Notes,
            entry.ActorUserId,
            entry.OccurredUtc);
    }
}
