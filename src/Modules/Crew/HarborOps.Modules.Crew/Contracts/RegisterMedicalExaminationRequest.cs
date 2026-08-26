namespace HarborOps.Modules.Crew.Contracts;

public sealed record RegisterMedicalExaminationRequest
{
    public string? ExamType { get; init; }

    public string? Clinic { get; init; }

    public DateOnly? ExamDate { get; init; }

    public DateOnly? ExpiryDate { get; init; }

    public string? FitnessResult { get; init; }
}
