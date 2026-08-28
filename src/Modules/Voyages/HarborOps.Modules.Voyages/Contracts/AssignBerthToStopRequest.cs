namespace HarborOps.Modules.Voyages.Contracts;

public sealed record AssignBerthToStopRequest
{
    public Guid? BerthId { get; init; }
}
