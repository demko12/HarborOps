using System.Reflection;
using HarborOps.Modules.Fleet.Application;
using HarborOps.Modules.Fleet.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace HarborOps.Modules.Fleet;

public static class FleetModule
{
    public const string Schema = "fleet";

    public static Assembly Assembly { get; } = typeof(FleetModule).Assembly;

    public static IServiceCollection AddFleetModule(this IServiceCollection services)
    {
        services.AddScoped<RegisterVesselService>();
        services.AddScoped<ISearchVesselsQuery, SearchVesselsQuery>();
        services.AddControllers()
            .AddApplicationPart(Assembly);

        return services;
    }
}
