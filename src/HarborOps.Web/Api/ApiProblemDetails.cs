namespace HarborOps.Web.Api;

internal sealed record ApiProblemDetails
{
    public string? Type { get; init; }

    public string? Title { get; init; }

    public int? Status { get; init; }

    public string? Detail { get; init; }

    public string? Instance { get; init; }

    public IReadOnlyDictionary<string, string[]> Errors { get; init; }
        = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
}
