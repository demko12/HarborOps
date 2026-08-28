namespace HarborOps.Modules.Voyages.Contracts;

public sealed record AssignBerthToStopResponse(
    Guid StopId,
    Guid BerthId,
    DateTime StartTime,
    DateTime EndTime,
    string ScheduleStatus);
