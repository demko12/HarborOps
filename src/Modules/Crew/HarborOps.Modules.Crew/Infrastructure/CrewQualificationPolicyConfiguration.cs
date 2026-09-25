using HarborOps.Modules.Crew.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HarborOps.Modules.Crew.Infrastructure;

internal sealed class CrewQualificationPolicyConfiguration : IEntityTypeConfiguration<CrewQualificationPolicy>
{
    public void Configure(EntityTypeBuilder<CrewQualificationPolicy> builder)
    {
        builder.ToTable("CrewQualificationPolicies", CrewModule.Schema);

        builder.HasKey(policy => policy.Id);

        builder.Property(policy => policy.Id)
            .ValueGeneratedNever();

        builder.Property(policy => policy.CrewPositionId)
            .IsRequired();

        builder.Property(policy => policy.Enabled)
            .IsRequired();

        builder.Property(policy => policy.CreatedByUserId)
            .IsRequired();

        builder.Property(policy => policy.CreatedAtUtc)
            .IsRequired();

        builder.Property(policy => policy.ConcurrencyToken)
            .IsConcurrencyToken()
            .HasDefaultValueSql("NEWID()");

        builder.HasIndex(policy => policy.CrewPositionId).IsUnique();
    }
}
