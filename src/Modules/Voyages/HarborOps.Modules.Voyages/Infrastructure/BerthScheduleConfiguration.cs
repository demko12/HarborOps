using HarborOps.Modules.Voyages.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HarborOps.Modules.Voyages.Infrastructure;

internal sealed class BerthScheduleConfiguration : IEntityTypeConfiguration<BerthSchedule>
{
    public void Configure(EntityTypeBuilder<BerthSchedule> builder)
    {
        builder.ToTable("BerthSchedules", VoyagesModule.Schema, table => table.IsTemporal());

        builder.HasKey(berthSchedule => berthSchedule.Id);

        builder.Property(berthSchedule => berthSchedule.Id)
            .ValueGeneratedNever();

        builder.Property(berthSchedule => berthSchedule.BerthId)
            .IsRequired();

        builder.Property(berthSchedule => berthSchedule.VoyageStopId)
            .IsRequired();

        builder.Property(berthSchedule => berthSchedule.StartTime)
            .IsRequired();

        builder.Property(berthSchedule => berthSchedule.EndTime)
            .IsRequired();

        builder.Property(berthSchedule => berthSchedule.Status)
            .HasMaxLength(BerthSchedule.StatusMaxLength)
            .IsRequired();

        builder.HasOne<Berth>()
            .WithMany()
            .HasForeignKey(berthSchedule => berthSchedule.BerthId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<VoyageStop>()
            .WithMany()
            .HasForeignKey(berthSchedule => berthSchedule.VoyageStopId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
