using HarborOps.Modules.Crew.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HarborOps.Modules.Crew.Infrastructure;

internal sealed class VesselManningPlanConfiguration : IEntityTypeConfiguration<VesselManningPlan>
{
    public void Configure(EntityTypeBuilder<VesselManningPlan> builder)
    {
        builder.ToTable("VesselManningPlans", CrewModule.Schema);

        builder.HasKey(plan => plan.Id);

        builder.Property(plan => plan.Id)
            .ValueGeneratedNever();

        builder.Property(plan => plan.VesselId)
            .IsRequired();

        builder.Property(plan => plan.EffectiveStartDate)
            .IsRequired();

        builder.Property(plan => plan.EffectiveEndDate)
            .IsRequired();

        builder.Property(plan => plan.CreatedByUserId)
            .IsRequired();

        builder.Property(plan => plan.CreatedAtUtc)
            .IsRequired();

        builder.Property(plan => plan.ConcurrencyToken)
            .IsConcurrencyToken()
            .HasDefaultValueSql("NEWID()");

        builder.HasIndex(plan => new { plan.VesselId, plan.EffectiveStartDate, plan.EffectiveEndDate });
    }
}
