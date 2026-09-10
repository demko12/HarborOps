namespace HarborOps.Modules.Documents.Contracts;

public sealed record GetRetentionQueueRequest
{
    public string? State { get; init; }

    public DateOnly? DueBefore { get; init; }

    public int? PageNumber { get; init; }

    public int? PageSize { get; init; }
}
