namespace HarborOps.Modules.Voyages.Contracts;

public sealed record GetPortCallActivitiesRequest
{
    public int? PageNumber { get; init; }

    public int? PageSize { get; init; }
}
