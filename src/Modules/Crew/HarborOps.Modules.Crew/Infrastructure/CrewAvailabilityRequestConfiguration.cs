using HarborOps.Modules.Crew.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HarborOps.Modules.Crew.Infrastructure;

internal sealed class CrewAvailabilityRequestConfiguration : IEntityTypeConfiguration<CrewAvailabilityRequest>
{
    public void Configure(EntityTypeBuilder<CrewAvailabilityRequest> builder)
    {
        builder.ToTable("CrewAvailabilityRequests", CrewModule.Schema);

        builder.HasKey(request => request.Id);

        builder.Property(request => request.Id)
            .ValueGeneratedNever();

        builder.Property(request => request.CrewMemberId)
            .IsRequired();

        builder.Property(request => request.Type)
            .HasMaxLength(CrewAvailabilityRequest.TypeMaxLength)
            .IsRequired();

        builder.Property(request => request.Status)
            .HasMaxLength(CrewAvailabilityRequest.TypeMaxLength)
            .IsRequired();

        builder.Property(request => request.StartDate)
            .IsRequired();

        builder.Property(request => request.EndDate)
            .IsRequired();

        builder.Property(request => request.Reason)
            .HasMaxLength(CrewAvailabilityRequest.ReasonMaxLength)
            .IsRequired();

        builder.Property(request => request.Notes)
            .HasMaxLength(CrewAvailabilityRequest.NotesMaxLength);

        builder.Property(request => request.RequestedByUserId)
            .IsRequired();

        builder.Property(request => request.RequestedAtUtc)
            .IsRequired();

        builder.Property(request => request.ReviewedByUserId);

        builder.Property(request => request.ReviewedAtUtc);

        builder.Property(request => request.DecisionReason)
            .HasMaxLength(CrewAvailabilityRequest.ReasonMaxLength);

        builder.HasIndex(request => new { request.CrewMemberId, request.Status });

        builder.HasIndex(request => new { request.CrewMemberId, request.StartDate, request.EndDate });
    }
}
