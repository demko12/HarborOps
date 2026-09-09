namespace HarborOps.Modules.Cargo.Domain;

internal enum ContainerControlAction
{
    SealAssigned,
    SealBroken,
    InspectionRecorded,
    Released,
    Held,
    HoldLifted,
}
