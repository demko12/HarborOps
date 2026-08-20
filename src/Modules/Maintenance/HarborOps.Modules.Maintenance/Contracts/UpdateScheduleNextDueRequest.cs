namespace HarborOps.Modules.Maintenance.Contracts;

public sealed record UpdateScheduleNextDueRequest
{
    public DateOnly? CompletedOn { get; init; }

    public int? NextDueHours { get; init; }
}
