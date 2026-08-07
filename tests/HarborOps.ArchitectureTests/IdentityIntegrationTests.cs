using HarborOps.Infrastructure.Persistence;
using HarborOps.Modules.Identity;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.ArchitectureTests;

public sealed class IdentityIntegrationTests
{
    [Fact]
    public void Identity_module_does_not_reference_infrastructure()
    {
        var referencedAssemblyNames = IdentityModule.Assembly
            .GetReferencedAssemblies()
            .Select(reference => reference.Name)
            .OfType<string>()
            .ToHashSet(StringComparer.Ordinal);

        Assert.DoesNotContain("HarborOps.Infrastructure", referencedAssemblyNames);
    }

    [Fact]
    public void Only_one_DbContext_type_is_declared_in_infrastructure()
    {
        var dbContextTypes = typeof(HarborOpsDbContext)
            .Assembly
            .GetTypes()
            .Where(type => typeof(DbContext).IsAssignableFrom(type) && !type.IsAbstract);

        Assert.Equal([typeof(HarborOpsDbContext)], dbContextTypes);
    }
}
