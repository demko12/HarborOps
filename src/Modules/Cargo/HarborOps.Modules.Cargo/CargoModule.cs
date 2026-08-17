using System.Reflection;
using HarborOps.Modules.Cargo.Application;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace HarborOps.Modules.Cargo;

public static class CargoModule
{
    public const string Schema = "cargo";

    public static Assembly Assembly { get; } = typeof(CargoModule).Assembly;

    public static IServiceCollection AddCargoModule(this IServiceCollection services)
    {
        services.AddScoped<DeclareCargoSafetyInfoService>();
        services.AddScoped<GetCargoBookingDetailsQuery>();
        services.AddScoped<RecordCargoTrackingEventService>();
        services.AddScoped<RegisterCargoBookingService>();
        services.AddScoped<SearchCargoBookingsQuery>();
        services.TryAddSingleton(TimeProvider.System);
        services.AddControllers()
            .AddApplicationPart(Assembly);

        return services;
    }
}
