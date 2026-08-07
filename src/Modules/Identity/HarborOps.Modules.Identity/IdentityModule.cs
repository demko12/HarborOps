using System.Reflection;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace HarborOps.Modules.Identity;

public static class IdentityModule
{
    public const string Schema = "identity";

    public static Assembly Assembly { get; } = typeof(IdentityModule).Assembly;

    public static IdentityBuilder AddIdentityModule(this IServiceCollection services)
    {
        return services
            .AddIdentityApiEndpoints<IdentityUser<Guid>>(options => options.User.RequireUniqueEmail = true)
            .AddRoles<IdentityRole<Guid>>();
    }
}
