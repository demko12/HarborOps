namespace HarborOps.Modules.Crew.Domain;

internal sealed class MedicalExamination
{
    internal const int ExamTypeMaxLength = 100;
    internal const int ClinicMaxLength = 200;
    internal const int FitnessResultMaxLength = 50;

    private MedicalExamination()
    {
    }

    private MedicalExamination(
        Guid id,
        Guid crewMemberId,
        string examType,
        string clinic,
        DateOnly examDate,
        DateOnly expiryDate,
        string fitnessResult)
    {
        Id = id;
        CrewMemberId = crewMemberId;
        ExamType = examType;
        Clinic = clinic;
        ExamDate = examDate;
        ExpiryDate = expiryDate;
        FitnessResult = fitnessResult;
    }

    public Guid Id { get; private set; }
    public Guid CrewMemberId { get; private set; }
    public string ExamType { get; private set; } = null!;
    public string Clinic { get; private set; } = null!;
    public DateOnly ExamDate { get; private set; }
    public DateOnly ExpiryDate { get; private set; }
    public string FitnessResult { get; private set; } = null!;

    internal static MedicalExamination Create(
        Guid crewMemberId,
        string examType,
        string clinic,
        DateOnly examDate,
        DateOnly expiryDate,
        string fitnessResult)
    {
        return new MedicalExamination(
            Guid.NewGuid(),
            crewMemberId,
            examType,
            clinic,
            examDate,
            expiryDate,
            fitnessResult);
    }
}
