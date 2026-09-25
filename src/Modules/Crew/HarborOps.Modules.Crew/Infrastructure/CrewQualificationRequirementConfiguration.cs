using HarborOps.Modules.Crew.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HarborOps.Modules.Crew.Infrastructure;

internal sealed class CrewQualificationRequirementConfiguration : IEntityTypeConfiguration<CrewQualificationRequirement>
{
    public void Configure(EntityTypeBuilder<CrewQualificationRequirement> builder)
    {
        builder.ToTable("CrewQualificationRequirements", CrewModule.Schema);

        builder.HasKey(requirement => requirement.Id);

        builder.Property(requirement => requirement.Id)
            .ValueGeneratedNever();

        builder.Property(requirement => requirement.PolicyId)
            .IsRequired();

        builder.Property(requirement => requirement.RequiredType)
            .HasMaxLength(CrewQualificationRequirement.RequiredTypeMaxLength)
            .IsRequired();

        builder.HasOne<CrewQualificationPolicy>()
            .WithMany()
            .HasForeignKey(requirement => requirement.PolicyId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(requirement => new { requirement.PolicyId, requirement.RequiredType }).IsUnique();
    }
}
