namespace HarborOps.Modules.Voyages.Contracts;

public sealed record DelayImpactStopItem(
    Guid StopId,
    int StopOrder,
    Guid PortId,
    string PortName,
    string Status,
    DateTime CurrentEta,
    DateTime CurrentEtd,
    DateTime ProposedEta,
    DateTime ProposedEtd,
    int EtaDeltaMinutes,
    int EtdDeltaMinutes);
