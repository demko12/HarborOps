using HarborOps.Modules.Cargo.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HarborOps.Modules.Cargo.Infrastructure;

internal sealed class ContainerConfiguration : IEntityTypeConfiguration<Container>
{
    public void Configure(EntityTypeBuilder<Container> builder)
    {
        builder.ToTable("Containers", CargoModule.Schema, table => table.IsTemporal());

        builder.HasKey(container => container.Id);

        builder.Property(container => container.Id)
            .ValueGeneratedNever();

        builder.Property(container => container.ContainerNumber)
            .HasMaxLength(Container.ContainerNumberMaxLength)
            .IsUnicode(false)
            .IsRequired();

        builder.Property(container => container.ContainerType)
            .HasMaxLength(Container.ContainerTypeMaxLength)
            .IsRequired();

        builder.Property(container => container.Status)
            .HasConversion<string>()
            .HasMaxLength(Container.StatusMaxLength)
            .IsRequired();

        builder.Property(container => container.VesselId);

        builder.Property(container => container.CargoBookingId);

        builder.HasOne<CargoBooking>()
            .WithMany()
            .HasForeignKey(container => container.CargoBookingId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
