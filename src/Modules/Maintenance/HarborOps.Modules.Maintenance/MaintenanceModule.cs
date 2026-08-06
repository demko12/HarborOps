using System.Reflection;
using HarborOps.Modules.Maintenance.Application;
using Microsoft.Extensions.DependencyInjection;

namespace HarborOps.Modules.Maintenance;

public static class MaintenanceModule
{
    public const string Schema = "maintenance";

    public static Assembly Assembly { get; } = typeof(MaintenanceModule).Assembly;

    public static IServiceCollection AddMaintenanceModule(this IServiceCollection services)
    {
        services.AddScoped<CreateMaintenanceWorkOrderService>();
        services.AddControllers()
            .AddApplicationPart(Assembly);

        return services;
    }
}
