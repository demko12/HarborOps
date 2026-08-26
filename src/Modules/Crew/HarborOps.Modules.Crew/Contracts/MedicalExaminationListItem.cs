namespace HarborOps.Modules.Crew.Contracts;

public sealed record MedicalExaminationListItem(
    Guid MedicalExaminationId,
    Guid CrewMemberId,
    string CrewMemberName,
    string EmploymentStatus,
    string ExamType,
    string Clinic,
    DateOnly ExamDate,
    DateOnly ExpiryDate,
    string FitnessResult,
    bool IsExpired,
    bool IsExpiringSoon);
