using HarborOps.Modules.Documents.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HarborOps.Modules.Documents.Infrastructure;

internal sealed class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> builder)
    {
        builder.ToTable("Notifications", schema: DocumentsModule.Schema);

        builder.HasKey(n => n.Id);

        builder.Property(n => n.UserId)
            .IsRequired();

        builder.Property(n => n.DaysBeforeExpiry)
            .IsRequired();

        builder.Property(n => n.Channel)
            .HasMaxLength(Notification.ChannelMaxLength)
            .IsRequired();

        builder.Property(n => n.SentAt)
            .IsRequired();

        builder.HasOne(n => n.Document)
            .WithMany()
            .HasForeignKey(n => n.DocumentId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(n => n.UserId);
    }
}
