using HarborOps.Modules.Voyages.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HarborOps.Modules.Voyages.Infrastructure;

internal sealed class RoutePortConfiguration : IEntityTypeConfiguration<RoutePort>
{
    public void Configure(EntityTypeBuilder<RoutePort> builder)
    {
        builder.ToTable("RoutePorts", VoyagesModule.Schema, table => table.IsTemporal());

        builder.HasKey(routePort => routePort.Id);

        builder.Property(routePort => routePort.Id)
            .ValueGeneratedNever();

        builder.Property(routePort => routePort.RouteId)
            .IsRequired();

        builder.Property(routePort => routePort.PortId)
            .IsRequired();

        builder.Property(routePort => routePort.StopOrder)
            .IsRequired();

        builder.Property(routePort => routePort.DistanceFromPrevious)
            .HasPrecision(10, 2)
            .IsRequired();

        builder.Property(routePort => routePort.EstimatedDurationFromPrevious)
            .IsRequired();

        builder.HasOne<Route>()
            .WithMany()
            .HasForeignKey(routePort => routePort.RouteId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<Port>()
            .WithMany()
            .HasForeignKey(routePort => routePort.PortId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
