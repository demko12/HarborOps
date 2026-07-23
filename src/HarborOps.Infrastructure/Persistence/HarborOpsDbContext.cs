using System.Reflection;
using HarborOps.Modules.Cargo;
using HarborOps.Modules.Compliance;
using HarborOps.Modules.Crew;
using HarborOps.Modules.Documents;
using HarborOps.Modules.Fleet;
using HarborOps.Modules.Fleet.Domain;
using HarborOps.Modules.Identity;
using HarborOps.Modules.Maintenance;
using HarborOps.Modules.Voyages;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Infrastructure.Persistence;

public sealed class HarborOpsDbContext(DbContextOptions<HarborOpsDbContext> options)
    : DbContext(options)
{
    internal DbSet<Vessel> Vessels => Set<Vessel>();

    private static readonly Assembly[] ModuleAssemblies =
    [
        CargoModule.Assembly,
        ComplianceModule.Assembly,
        CrewModule.Assembly,
        DocumentsModule.Assembly,
        FleetModule.Assembly,
        IdentityModule.Assembly,
        MaintenanceModule.Assembly,
        VoyagesModule.Assembly,
    ];

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        foreach (var moduleAssembly in ModuleAssemblies)
        {
            modelBuilder.ApplyConfigurationsFromAssembly(moduleAssembly);
        }
    }
}
