using System.Reflection;

namespace HarborOps.Modules.Compliance;

public static class ComplianceModule
{
    public const string Schema = "compliance";

    public static Assembly Assembly { get; } = typeof(ComplianceModule).Assembly;
}
