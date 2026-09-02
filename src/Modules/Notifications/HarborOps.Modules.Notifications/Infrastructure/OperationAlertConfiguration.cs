using HarborOps.Modules.Notifications.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HarborOps.Modules.Notifications.Infrastructure;

internal sealed class OperationAlertConfiguration : IEntityTypeConfiguration<OperationAlert>
{
    public void Configure(EntityTypeBuilder<OperationAlert> builder)
    {
        builder.ToTable("OperationAlerts", NotificationsModule.Schema);
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Capability).HasMaxLength(50).IsRequired();
        builder.Property(x => x.SourceType).HasMaxLength(80).IsRequired();
        builder.Property(x => x.Severity).HasMaxLength(20).IsRequired();
        builder.HasIndex(x => new { x.SourceId, x.ExpiryDate, x.ThresholdDays }).IsUnique();
        builder.HasIndex(x => new { x.Capability, x.ExpiryDate });
    }
}
