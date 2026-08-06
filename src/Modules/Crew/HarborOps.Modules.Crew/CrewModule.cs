using System.Reflection;
using HarborOps.Modules.Crew.Application;
using Microsoft.Extensions.DependencyInjection;

namespace HarborOps.Modules.Crew;

public static class CrewModule
{
    public const string Schema = "crew";

    public static Assembly Assembly { get; } = typeof(CrewModule).Assembly;

    public static IServiceCollection AddCrewModule(this IServiceCollection services)
    {
        services.AddScoped<RegisterCrewMemberService>();

        services
            .AddControllers()
            .AddApplicationPart(Assembly);

        return services;
    }
}
