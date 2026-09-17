using System.Reflection;
using HarborOps.Modules.Crew.Application;
using HarborOps.Modules.Crew.Contracts;
using HarborOps.SharedKernel;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace HarborOps.Modules.Crew;

public static class CrewModule
{
    public const string Schema = "crew";

    public static Assembly Assembly { get; } = typeof(CrewModule).Assembly;

    public static IServiceCollection AddCrewModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<RegisterCrewMemberService>();
        services.AddMemoryCache();
        services.AddScoped<SearchCrewMembersQuery>();
        services.AddScoped<ISearchCrewMembersQuery, CachedSearchCrewMembersQuery>();
        services.AddScoped<IOperationsDashboardCrewQuery, OperationsDashboardCrewQuery>();
        services.AddScoped<RegisterCrewAssignmentService>();
        services.AddScoped<ActivateCrewAssignmentService>();
        services.AddScoped<CompleteCrewAssignmentService>();
        services.AddScoped<CancelCrewAssignmentService>();
        services.AddScoped<RegisterCrewCertificateService>();
        services.TryAddSingleton<IDateTimeProvider, SystemDateTimeProvider>();
        services.TryAddSingleton(TimeProvider.System);
        services
            .AddControllers()
            .AddApplicationPart(Assembly);
        services.AddScoped<IGetCrewMemberProfileQuery, GetCrewMemberProfileQuery>();
        services.AddScoped<IExpiryAlertCandidateQuery, ExpiryAlertCandidateQuery>();
        services.AddScoped<ISearchCrewPositionsQuery, SearchCrewPositionsQuery>();
        services.AddScoped<RegisterCrewPositionService>();
        services.AddScoped<UpdateCrewPositionService>();
        services.AddScoped<IGetCrewRotationsQuery, GetCrewRotationsQuery>();
        services.AddScoped<IGetCrewRotationQuery, GetCrewRotationQuery>();
        services.AddScoped<PlanCrewRotationService>();
        services.AddScoped<UpdateCrewRotationService>();
        services.AddScoped<ExecuteCrewRotationService>();
        services.AddScoped<CancelCrewRotationService>();
        services.Configure<MedicalExaminationOptions>(
            configuration.GetSection(MedicalExaminationOptions.SectionName));
        services.AddScoped<IGetMedicalExaminationsQuery, GetMedicalExaminationsQuery>();
        services.AddScoped<IGetMemberMedicalExaminationsQuery, GetMemberMedicalExaminationsQuery>();
        services.AddScoped<RegisterMedicalExaminationService>();
        services.AddScoped<UpdateCrewMemberService>();
        services.AddScoped<ChangeCrewMemberEmploymentStatusService>();
        services.AddScoped<IGetCrewEmploymentStatusHistoryQuery, GetCrewEmploymentStatusHistoryQuery>();
        services.AddScoped<RequestCrewAvailabilityService>();
        services.AddScoped<ApproveCrewAvailabilityRequestService>();
        services.AddScoped<RejectCrewAvailabilityRequestService>();
        services.AddScoped<CancelCrewAvailabilityRequestService>();
        services.AddScoped<IGetCrewAvailabilityQuery, GetCrewAvailabilityQuery>();
        services.AddScoped<IGetCrewMemberAvailabilityQuery, GetCrewMemberAvailabilityQuery>();
        services.AddScoped<IGetCrewAvailabilityConflictsQuery, GetCrewAvailabilityConflictsQuery>();
        return services;
    }
}
