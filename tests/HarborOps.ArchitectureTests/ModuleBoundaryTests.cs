using System.Reflection;
using HarborOps.Infrastructure.Persistence;
using HarborOps.Modules.Cargo;
using HarborOps.Modules.Compliance;
using HarborOps.Modules.Crew;
using HarborOps.Modules.Documents;
using HarborOps.Modules.Fleet;
using HarborOps.Modules.Identity;
using HarborOps.Modules.Maintenance;
using HarborOps.Modules.Voyages;

namespace HarborOps.ArchitectureTests;

public sealed class ModuleBoundaryTests
{
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

    [Fact]
    public void Modules_do_not_reference_other_modules()
    {
        var moduleNames = ModuleAssemblies
            .Select(assembly => assembly.GetName().Name)
            .OfType<string>()
            .ToHashSet(StringComparer.Ordinal);

        foreach (var moduleAssembly in ModuleAssemblies)
        {
            var referencedModuleNames = moduleAssembly
                .GetReferencedAssemblies()
                .Select(reference => reference.Name)
                .Where(name => name is not null && moduleNames.Contains(name))
                .ToArray();

            Assert.Empty(referencedModuleNames);
        }
    }

    [Fact]
    public void Infrastructure_discovers_modules_without_referencing_shared_kernel()
    {
        var infrastructureReferences = typeof(HarborOpsDbContext)
            .Assembly
            .GetReferencedAssemblies()
            .Select(reference => reference.Name)
            .OfType<string>()
            .ToHashSet(StringComparer.Ordinal);

        foreach (var moduleAssembly in ModuleAssemblies)
        {
            Assert.Contains(moduleAssembly.GetName().Name!, infrastructureReferences);
        }

        Assert.DoesNotContain("HarborOps.SharedKernel", infrastructureReferences);
    }
}
