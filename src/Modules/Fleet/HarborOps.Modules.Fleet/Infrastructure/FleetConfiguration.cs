using HarborOps.Modules.Fleet.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HarborOps.Modules.Fleet.Infrastructure;

internal sealed class FleetConfiguration : IEntityTypeConfiguration<Domain.Fleet>
{
    public void Configure(EntityTypeBuilder<Domain.Fleet> builder)
    {
        builder.ToTable("Fleets", FleetModule.Schema);

        builder.HasKey(fleet => fleet.Id);

        builder.Property(fleet => fleet.Id)
            .ValueGeneratedNever();

        builder.Property(fleet => fleet.Name)
            .HasMaxLength(Domain.Fleet.NameMaxLength)
            .IsRequired();

        builder.Property(fleet => fleet.Description)
            .HasMaxLength(Domain.Fleet.DescriptionMaxLength)
            .IsRequired();

        builder.Property(fleet => fleet.CreatedAtUtc)
            .IsRequired();
    }
}
