using System.Reflection;
using HarborOps.Modules.Cargo.Application;
using HarborOps.Modules.Cargo.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace HarborOps.Modules.Cargo;

public static class CargoModule
{
    public const string Schema = "cargo";

    public static Assembly Assembly { get; } = typeof(CargoModule).Assembly;

    public static IServiceCollection AddCargoModule(this IServiceCollection services)
    {
        services.AddScoped<AddCargoContainerService>();
        services.AddScoped<ExecuteCargoOperationService>();
        services.AddScoped<GetCargoBookingDetailsQuery>();
        services.AddScoped<GetCargoContainersQuery>();
        services.AddScoped<GetCargoOperationsQuery>();
        services.AddScoped<GetCargoSafetyInformationQuery>();
        services.AddScoped<PlanCargoOperationService>();
        services.AddScoped<RecordCargoTrackingEventService>();
        services.AddScoped<RegisterCargoBookingService>();
        services.AddScoped<ReviewCargoSafetyInfoService>();
        services.AddScoped<SearchCargoBookingsQuery>();
        services.AddScoped<SubmitCargoSafetyInfoService>();
        services.AddScoped<UpdateCargoSafetyInfoService>();
        services.AddScoped<IOperationsDashboardCargoQuery, OperationsDashboardCargoQuery>();
        services.TryAddSingleton(TimeProvider.System);
        services.AddControllers()
            .AddApplicationPart(Assembly);

        return services;
    }
}
