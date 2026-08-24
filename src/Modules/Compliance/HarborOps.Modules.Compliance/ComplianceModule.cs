using System.Reflection;
using HarborOps.Modules.Compliance.Application;
using HarborOps.Modules.Compliance.Contracts;
using HarborOps.SharedKernel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace HarborOps.Modules.Compliance;

public static class ComplianceModule
{
    public const string Schema = "compliance";

    public static Assembly Assembly { get; } = typeof(ComplianceModule).Assembly;

    public static IServiceCollection AddComplianceModule(this IServiceCollection services)
    {
        services.AddScoped<RegisterComplianceCertificateService>();
        services.AddScoped<RegisterPscInspectionService>();
        services.AddScoped<RegisterNonConformityService>();
        services.AddScoped<InsurancePolicyService>();
        services.AddScoped<SurveyAuditService>();
        services.AddScoped<IGetVesselComplianceOverviewQuery, GetVesselComplianceOverviewQuery>();
        services.AddScoped<IOperationsDashboardComplianceQuery, OperationsDashboardComplianceQuery>();
        services.TryAddSingleton<IDateTimeProvider, SystemDateTimeProvider>();
        services.AddScoped<IReportsComplianceExposureQuery, ReportsComplianceExposureQuery>();
        services.AddControllers()
            .AddApplicationPart(Assembly);

        return services;
    }
}
