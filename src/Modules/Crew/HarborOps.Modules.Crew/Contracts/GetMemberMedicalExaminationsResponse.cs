namespace HarborOps.Modules.Crew.Contracts;

public sealed record GetMemberMedicalExaminationsResponse(
    IReadOnlyList<MedicalExaminationListItem> Items,
    string? CurrentFitnessResult);
