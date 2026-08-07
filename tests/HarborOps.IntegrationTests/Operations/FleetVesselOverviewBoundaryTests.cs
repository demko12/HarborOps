using HarborOps.Modules.Fleet;
using HarborOps.Modules.Maintenance;

namespace HarborOps.IntegrationTests.Operations;

public sealed class FleetVesselOverviewBoundaryTests
{
    [Fact]
    public void Fleet_and_maintenance_do_not_reference_each_other()
    {
        var fleetReferences = FleetModule.Assembly
            .GetReferencedAssemblies()
            .Select(reference => reference.Name)
            .ToArray();
        var maintenanceReferences = MaintenanceModule.Assembly
            .GetReferencedAssemblies()
            .Select(reference => reference.Name)
            .ToArray();

        Assert.DoesNotContain(MaintenanceModule.Assembly.GetName().Name, fleetReferences);
        Assert.DoesNotContain(FleetModule.Assembly.GetName().Name, maintenanceReferences);
    }
}
