using System.Reflection;

namespace HarborOps.Modules.Maintenance;

public static class MaintenanceModule
{
    public const string Schema = "maintenance";

    public static Assembly Assembly { get; } = typeof(MaintenanceModule).Assembly;
}
