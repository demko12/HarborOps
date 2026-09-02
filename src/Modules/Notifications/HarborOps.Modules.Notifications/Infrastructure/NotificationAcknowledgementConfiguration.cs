using HarborOps.Modules.Notifications.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HarborOps.Modules.Notifications.Infrastructure;

internal sealed class NotificationAcknowledgementConfiguration : IEntityTypeConfiguration<NotificationAcknowledgement>
{
    public void Configure(EntityTypeBuilder<NotificationAcknowledgement> builder)
    {
        builder.ToTable("Acknowledgements", NotificationsModule.Schema);
        builder.HasKey(x => new { x.AlertId, x.UserId });
        builder.HasIndex(x => x.UserId);
        builder.HasOne<OperationAlert>().WithMany().HasForeignKey(x => x.AlertId).OnDelete(DeleteBehavior.Cascade);
    }
}
