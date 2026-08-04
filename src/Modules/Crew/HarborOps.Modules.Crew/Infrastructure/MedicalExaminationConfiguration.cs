using HarborOps.Modules.Crew.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HarborOps.Modules.Crew.Infrastructure;

internal sealed class MedicalExaminationConfiguration : IEntityTypeConfiguration<MedicalExamination>
{
    public void Configure(EntityTypeBuilder<MedicalExamination> builder)
    {
        builder.ToTable("MedicalExaminations", CrewModule.Schema);

        builder.HasKey(medicalExamination => medicalExamination.Id);

        builder.Property(medicalExamination => medicalExamination.Id)
            .ValueGeneratedNever();

        builder.Property(medicalExamination => medicalExamination.CrewMemberId)
            .IsRequired();

        builder.Property(medicalExamination => medicalExamination.ExamType)
            .HasMaxLength(MedicalExamination.ExamTypeMaxLength)
            .IsRequired();

        builder.Property(medicalExamination => medicalExamination.Clinic)
            .HasMaxLength(MedicalExamination.ClinicMaxLength)
            .IsRequired();

        builder.Property(medicalExamination => medicalExamination.ExamDate)
            .IsRequired();

        builder.Property(medicalExamination => medicalExamination.ExpiryDate)
            .IsRequired();

        builder.Property(medicalExamination => medicalExamination.FitnessResult)
            .HasMaxLength(MedicalExamination.FitnessResultMaxLength)
            .IsRequired();
    }
}
