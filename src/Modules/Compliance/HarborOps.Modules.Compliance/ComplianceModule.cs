using System.Reflection;
using HarborOps.Modules.Compliance.Application;
using Microsoft.Extensions.DependencyInjection;

namespace HarborOps.Modules.Compliance;

public static class ComplianceModule
{
    public const string Schema = "compliance";

    public static Assembly Assembly { get; } = typeof(ComplianceModule).Assembly;

    public static IServiceCollection AddComplianceModule(this IServiceCollection services)
    {
        services.AddScoped<RegisterComplianceCertificateService>();
        services.AddControllers()
            .AddApplicationPart(Assembly);

        return services;
    }
}
