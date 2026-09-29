using System.Reflection;
using HarborOps.Modules.Fleet.Application;
using HarborOps.Modules.Fleet.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace HarborOps.Modules.Fleet;

public static class FleetModule
{
    public const string Schema = "fleet";

    public static Assembly Assembly { get; } = typeof(FleetModule).Assembly;

    public static IServiceCollection AddFleetModule(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<RegisterVesselService>();
        services.AddScoped<ISearchVesselsQuery, SearchVesselsQuery>();
        services.AddScoped<IOperationsDashboardFleetQuery, OperationsDashboardFleetQuery>();
        services.AddScoped<IVesselLookupService, VesselLookupService>();
        services.AddScoped<IGetVesselDetailsQuery, GetVesselDetailsQuery>();
        services.AddScoped<IGetVesselTypesQuery, GetVesselTypesQuery>();
        services.AddScoped<RecordVesselStatusChangeService>();
        services.AddScoped<UpdateVesselRegistryDetailsService>();
        services.AddScoped<UpdateVesselSpecificationsService>();
        services.AddScoped<IFleetManagementQuery, FleetManagementQuery>();
        services.AddScoped<FleetManagementService>();
        services.AddScoped<IVesselPositionsQuery, VesselPositionsQuery>();
        services.AddScoped<RecordVesselPositionService>();
        services.AddScoped<IVesselPositionHistoryQuery, VesselPositionHistoryQuery>();
        services.AddControllers()
            .AddApplicationPart(Assembly);

        return services;
    }
}
