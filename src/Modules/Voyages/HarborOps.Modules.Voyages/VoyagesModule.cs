using System.Reflection;

namespace HarborOps.Modules.Voyages;

public static class VoyagesModule
{
    public const string Schema = "voyages";

    public static Assembly Assembly { get; } = typeof(VoyagesModule).Assembly;
}
