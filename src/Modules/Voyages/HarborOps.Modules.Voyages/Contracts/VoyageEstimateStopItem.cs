namespace HarborOps.Modules.Voyages.Contracts;

public sealed record VoyageEstimateStopItem(
    Guid StopId,
    int StopOrder,
    Guid PortId,
    string PortName,
    string Status,
    DateTime PlannedEta,
    DateTime PlannedEtd,
    DateTime? RevisedEta,
    DateTime? RevisedEtd,
    DateTime? Ata,
    DateTime? Atd);
