using System.Reflection;

namespace HarborOps.Modules.Identity;

public static class IdentityModule
{
    public const string Schema = "identity";

    public static Assembly Assembly { get; } = typeof(IdentityModule).Assembly;
}
