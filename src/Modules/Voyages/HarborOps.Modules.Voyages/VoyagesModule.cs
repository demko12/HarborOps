using System.Reflection;
using HarborOps.Modules.Voyages.Application;
using Microsoft.Extensions.DependencyInjection;

namespace HarborOps.Modules.Voyages;

public static class VoyagesModule
{
    public const string Schema = "voyages";

    public static Assembly Assembly { get; } = typeof(VoyagesModule).Assembly;

    public static IServiceCollection AddVoyagesModule(this IServiceCollection services)
    {
        services.AddScoped<ScheduleVoyageService>();
        services.AddControllers()
            .AddApplicationPart(Assembly);

        return services;
    }
}
