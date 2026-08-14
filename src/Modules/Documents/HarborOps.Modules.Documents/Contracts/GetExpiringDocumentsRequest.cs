namespace HarborOps.Modules.Documents.Contracts;

public sealed record GetExpiringDocumentsRequest
{
    public DateOnly? From { get; init; }

    public DateOnly? To { get; init; }

    public Guid? VesselId { get; init; }

    public int? PageNumber { get; init; }

    public int? PageSize { get; init; }
}
