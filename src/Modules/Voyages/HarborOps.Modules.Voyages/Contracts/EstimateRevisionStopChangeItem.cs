namespace HarborOps.Modules.Voyages.Contracts;

public sealed record EstimateRevisionStopChangeItem(
    Guid StopId,
    int StopOrder,
    string PortName,
    DateTime PreviousEta,
    DateTime PreviousEtd,
    DateTime NewEta,
    DateTime NewEtd);
