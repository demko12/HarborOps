namespace HarborOps.Modules.Maintenance.Contracts;

public sealed record UpdateScheduleNextDueResponse(
    Guid ScheduleId,
    string Frequency,
    DateOnly? NextDueDate,
    int? NextDueHours);
