using HarborOps.Modules.Voyages.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HarborOps.Modules.Voyages.Infrastructure;

internal sealed class RouteConfiguration : IEntityTypeConfiguration<Route>
{
    public void Configure(EntityTypeBuilder<Route> builder)
    {
        builder.ToTable("Routes", VoyagesModule.Schema);

        builder.HasKey(route => route.Id);

        builder.Property(route => route.Id)
            .ValueGeneratedNever();

        builder.Property(route => route.Name)
            .HasMaxLength(Route.NameMaxLength)
            .IsRequired();

        builder.Property(route => route.Description)
            .HasMaxLength(Route.DescriptionMaxLength);

        builder.Property(route => route.EstimatedDistance)
            .HasPrecision(10, 2)
            .IsRequired();

        builder.Property(route => route.EstimatedDuration)
            .IsRequired();
    }
}
