using System.Reflection;
using HarborOps.Modules.Voyages.Application;
using HarborOps.Modules.Voyages.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace HarborOps.Modules.Voyages;

public static class VoyagesModule
{
    public const string Schema = "voyages";

    public static Assembly Assembly { get; } = typeof(VoyagesModule).Assembly;

    public static IServiceCollection AddVoyagesModule(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<ScheduleVoyageService>();
        services.AddScoped<RecordVoyageDepartureService>();
        services.AddScoped<RecordVoyageArrivalService>();
        services.AddScoped<IGetVoyagesQuery, GetVoyagesQuery>();
        services.AddScoped<IOperationsDashboardVoyagesQuery, OperationsDashboardVoyagesQuery>();
        services.AddControllers()
            .AddApplicationPart(Assembly);

        return services;
    }
}
