namespace HarborOps.Modules.Voyages.Contracts;

public sealed record VoyageScheduleResponse(
    VoyageScheduleSummary Voyage,
    IReadOnlyList<VoyageScheduleChangeItem> Changes);
