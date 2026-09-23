using HarborOps.Modules.Crew.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HarborOps.Modules.Crew.Infrastructure;

internal sealed class VesselManningPlanLineConfiguration : IEntityTypeConfiguration<VesselManningPlanLine>
{
    public void Configure(EntityTypeBuilder<VesselManningPlanLine> builder)
    {
        builder.ToTable("VesselManningPlanLines", CrewModule.Schema);

        builder.HasKey(line => line.Id);

        builder.Property(line => line.Id)
            .ValueGeneratedNever();

        builder.Property(line => line.VesselManningPlanId)
            .IsRequired();

        builder.Property(line => line.RankId)
            .IsRequired();

        builder.Property(line => line.RequiredHeadcount)
            .IsRequired();

        builder.HasOne<VesselManningPlan>()
            .WithMany()
            .HasForeignKey(line => line.VesselManningPlanId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<CrewPosition>()
            .WithMany()
            .HasForeignKey(line => line.RankId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(line => new { line.VesselManningPlanId, line.RankId }).IsUnique();
    }
}
