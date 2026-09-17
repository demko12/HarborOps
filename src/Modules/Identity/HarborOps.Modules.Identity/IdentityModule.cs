using System.Reflection;
using HarborOps.Modules.Identity.Application;
using Microsoft.AspNetCore.Authentication.BearerToken;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace HarborOps.Modules.Identity;

public static class IdentityModule
{
    public const string Schema = "identity";

    public static Assembly Assembly { get; } = typeof(IdentityModule).Assembly;

    public static IdentityBuilder AddIdentityModule(this IServiceCollection services)
    {
        var identityBuilder = services
            .AddIdentityApiEndpoints<IdentityUser<Guid>>(options => options.User.RequireUniqueEmail = true)
            .AddRoles<IdentityRole<Guid>>();


        services.Configure<BearerTokenOptions>(IdentityConstants.BearerScheme, options =>
        {
            options.BearerTokenExpiration = TimeSpan.FromMinutes(15);
            options.RefreshTokenExpiration = TimeSpan.FromDays(7);
        });

        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<LoginAttemptThrottle>();

        services.AddAuthorization(IdentityAuthorizationPolicies.Configure);
        services.AddScoped<IAuthorizationHandler, PermissionAuthorizationHandler>();

        services.AddScoped<UserAdministrationService>();
        services.AddScoped<AccountSecurityService>();
        services.AddScoped<RoleAdministrationService>();
        services.AddScoped<IdentityAdministratorGuard>();
        services.AddSingleton<IAuthorizationMiddlewareResultHandler, IdentityAccountStateAuthorizationResultHandler>();
        services.AddHostedService<IdentityBootstrapSeeder>();

        return identityBuilder;
    }
}
