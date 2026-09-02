namespace HarborOps.Modules.Voyages.Contracts;

public sealed record PlannedVoyageStopItem(
    Guid StopId,
    int StopOrder,
    Guid PortId,
    DateTime Eta,
    DateTime Etd);
