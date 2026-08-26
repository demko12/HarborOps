namespace HarborOps.Modules.Crew.Contracts;

public sealed record GetMedicalExaminationsRequest
{
    public string? Search { get; init; }

    public string? FitnessResult { get; init; }

    public bool? Expired { get; init; }

    public int? ExpiringWithinDays { get; init; }

    public int? PageNumber { get; init; }

    public int? PageSize { get; init; }
}
