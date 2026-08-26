using System.Reflection;
using HarborOps.Modules.Crew.Application;
using HarborOps.Modules.Crew.Contracts;
using HarborOps.SharedKernel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace HarborOps.Modules.Crew;

public static class CrewModule
{
    public const string Schema = "crew";

    public static Assembly Assembly { get; } = typeof(CrewModule).Assembly;

    public static IServiceCollection AddCrewModule(this IServiceCollection services)
    {
        services.AddScoped<RegisterCrewMemberService>();
        services.AddScoped<ISearchCrewMembersQuery, SearchCrewMembersQuery>();
        services.AddScoped<IOperationsDashboardCrewQuery, OperationsDashboardCrewQuery>();
        services.AddScoped<RegisterCrewAssignmentService>();
        services.AddScoped<ActivateCrewAssignmentService>();
        services.AddScoped<CompleteCrewAssignmentService>();
        services.AddScoped<CancelCrewAssignmentService>();
        services.AddScoped<RegisterCrewCertificateService>();
        services.TryAddSingleton<IDateTimeProvider, SystemDateTimeProvider>();
        services
            .AddControllers()
            .AddApplicationPart(Assembly);
        services.AddScoped<IGetCrewMemberProfileQuery, GetCrewMemberProfileQuery>();
        services.AddScoped<ISearchCrewPositionsQuery, SearchCrewPositionsQuery>();
        services.AddScoped<IGetCrewRotationsQuery, GetCrewRotationsQuery>();
        services.AddScoped<IGetCrewRotationQuery, GetCrewRotationQuery>();
        services.AddScoped<PlanCrewRotationService>();
        services.AddScoped<UpdateCrewRotationService>();
        services.AddScoped<ExecuteCrewRotationService>();
        services.AddScoped<CancelCrewRotationService>();
        services.TryAddSingleton<TimeProvider>(TimeProvider.System);
        return services;
    }
}
