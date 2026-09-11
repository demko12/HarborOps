using HarborOps.Modules.Crew.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HarborOps.Modules.Crew.Infrastructure;

internal sealed class CrewAvailabilityTransitionConfiguration : IEntityTypeConfiguration<CrewAvailabilityTransition>
{
    public void Configure(EntityTypeBuilder<CrewAvailabilityTransition> builder)
    {
        builder.ToTable("CrewAvailabilityTransitions", CrewModule.Schema);

        builder.HasKey(transition => transition.Id);

        builder.Property(transition => transition.Id)
            .ValueGeneratedNever();

        builder.Property(transition => transition.AvailabilityRequestId)
            .IsRequired();

        builder.Property(transition => transition.EventType)
            .HasMaxLength(CrewAvailabilityTransition.EventTypeMaxLength)
            .IsRequired();

        builder.Property(transition => transition.Reason)
            .HasMaxLength(CrewAvailabilityTransition.ReasonMaxLength);

        builder.Property(transition => transition.ActorUserId)
            .IsRequired();

        builder.Property(transition => transition.OccurredAtUtc)
            .IsRequired();

        builder.Property(transition => transition.PreviousStatus)
            .HasMaxLength(CrewAvailabilityTransition.StatusMaxLength)
            .IsRequired();

        builder.Property(transition => transition.NewStatus)
            .HasMaxLength(CrewAvailabilityTransition.StatusMaxLength)
            .IsRequired();

        builder.HasIndex(transition => new { transition.AvailabilityRequestId, transition.OccurredAtUtc });
    }
}
