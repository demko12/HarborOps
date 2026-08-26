namespace HarborOps.Modules.Crew.Application;

public sealed class MedicalExaminationOptions
{
    public const string SectionName = "Crew:MedicalExaminations";

    public int ExpiringSoonWithinDays { get; set; } = 30;
}
