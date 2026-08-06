using System.Reflection;
using HarborOps.Modules.Cargo.Application;
using Microsoft.Extensions.DependencyInjection;

namespace HarborOps.Modules.Cargo;

public static class CargoModule
{
    public const string Schema = "cargo";

    public static Assembly Assembly { get; } = typeof(CargoModule).Assembly;

    public static IServiceCollection AddCargoModule(this IServiceCollection services)
    {
        services.AddScoped<RegisterCargoBookingService>();
        services.AddControllers()
            .AddApplicationPart(Assembly);

        return services;
    }
}
