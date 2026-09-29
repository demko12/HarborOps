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
        services.AddScoped<RenewComplianceCertificateService>();
        services.AddScoped<RevokeComplianceCertificateService>();
        services.AddScoped<IGetComplianceCertificateDetailQuery, GetComplianceCertificateDetailQuery>();
        services.AddScoped<RegisterPscInspectionService>();
        services.AddScoped<PscInspectionWorkflowService>();
        services.AddScoped<IGetPscInspectionDetailQuery, GetPscInspectionDetailQuery>();
        services.AddScoped<IGetPscDeficienciesQuery, GetPscDeficienciesQuery>();
        services.AddScoped<IGetPscDetentionHistoryQuery, GetPscDetentionHistoryQuery>();
        services.AddScoped<RegisterNonConformityService>();
        services.AddScoped<InsurancePolicyService>();
        services.AddScoped<SurveyAuditService>();
        services.AddScoped<IGetVesselComplianceOverviewQuery, GetVesselComplianceOverviewQuery>();
        services.AddScoped<IExpiryAlertCandidateQuery, ExpiryAlertCandidateQuery>();
        services.AddScoped<IOperationsDashboardComplianceQuery, OperationsDashboardComplianceQuery>();
        services.TryAddSingleton<IDateTimeProvider, SystemDateTimeProvider>();
        services.AddScoped<IReportsComplianceExposureQuery, ReportsComplianceExposureQuery>();
        services.AddScoped<IGetNonConformitiesQuery, GetNonConformitiesQuery>();
        services.AddScoped<IGetNonConformityDetailQuery, GetNonConformityDetailQuery>();
        services.AddScoped<RecordCorrectiveActionService>();
        services.AddScoped<CloseNonConformityService>();
        services.AddScoped<IGetAuditPlansQuery, GetAuditPlansQuery>();
        services.AddScoped<IGetAuditPlanDetailQuery, GetAuditPlanDetailQuery>();
        services.AddScoped<RegisterAuditPlanService>();
        services.AddScoped<RescheduleAuditPlanService>();
        services.AddScoped<AssignAuditPlanInspectorService>();
        services.AddScoped<StartAuditPlanService>();
        services.AddScoped<CompleteAuditPlanService>();
        services.AddScoped<CancelAuditPlanService>();
        services.TryAddSingleton(TimeProvider.System);
        services.AddControllers()
            .AddApplicationPart(Assembly);

        return services;
    }
}
