namespace HarborOps.Modules.Crew.Contracts;

public sealed record GetMedicalExaminationsResponse(
    IReadOnlyList<MedicalExaminationListItem> Items,
    int PageNumber,
    int PageSize,
    int TotalCount);
