namespace HarborOps.Modules.Crew.Contracts;

public sealed record GetCrewRotationsRequest
{
    public Guid? VesselId { get; init; }

    public DateOnly? From { get; init; }

    public DateOnly? To { get; init; }

    public bool? OverdueOnly { get; init; }

    public int? PageNumber { get; init; }

    public int? PageSize { get; init; }
}
