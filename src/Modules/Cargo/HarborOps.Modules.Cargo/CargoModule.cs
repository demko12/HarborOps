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
        services.AddScoped<ContainerManifestImportService>();
        services.AddScoped<AssignContainerSealService>();
        services.AddScoped<BreakContainerSealService>();
        services.AddScoped<CargoCompletionBlockersQuery>();
        services.AddScoped<CargoDiscrepancyWorkflowService>();
        services.AddScoped<CorrectReeferTemperatureReadingService>();
        services.AddScoped<CreateBillOfLadingDraftService>();
        services.AddScoped<ExecuteCargoOperationService>();
        services.AddScoped<GetBillOfLadingDetailsQuery>();
        services.AddScoped<GetBillOfLadingPrintQuery>();
        services.AddScoped<GetBillsOfLadingQuery>();
        services.AddScoped<GetCargoBookingDetailsQuery>();
        services.AddScoped<GetCargoContainersQuery>();
        services.AddScoped<GetCargoDiscrepanciesQuery>();
        services.AddScoped<GetCargoManifestExportQuery>();
        services.AddScoped<GetCargoOperationsQuery>();
        services.AddScoped<GetCargoSafetyInformationQuery>();
        services.AddScoped<GetContainerControlQuery>();
        services.AddScoped<GetReeferExcursionsQuery>();
        services.AddScoped<GetReeferMonitoringProfileQuery>();
        services.AddScoped<GetReeferTemperatureReadingsQuery>();
        services.AddScoped<HoldContainerService>();
        services.AddScoped<IssueBillOfLadingService>();
        services.AddScoped<OpenExcursionDiscrepancyService>();
        services.AddScoped<PlanCargoOperationService>();
        services.AddScoped<RecordCargoTrackingEventService>();
        services.AddScoped<RecordContainerInspectionService>();
        services.AddScoped<RecordReeferTemperatureReadingService>();
        services.AddScoped<RegisterCargoBookingService>();
        services.AddScoped<RegisterCargoDiscrepancyService>();
        services.AddScoped<ReleaseContainerService>();
        services.AddScoped<ResolveReeferExcursionService>();
        services.AddScoped<ReviewCargoSafetyInfoService>();
        services.AddScoped<SaveReeferMonitoringProfileService>();
        services.AddScoped<SearchCargoBookingsQuery>();
        services.AddScoped<SubmitCargoSafetyInfoService>();
        services.AddScoped<UpdateBillOfLadingDraftService>();
        services.AddScoped<UpdateCargoContainerService>();
        services.AddScoped<UpdateCargoSafetyInfoService>();
        services.AddScoped<VoidBillOfLadingService>();
        services.AddScoped<VoidReeferTemperatureReadingService>();
        services.AddScoped<IOperationsDashboardCargoQuery, OperationsDashboardCargoQuery>();
        services.TryAddSingleton(TimeProvider.System);
        services.AddControllers()
            .AddApplicationPart(Assembly);

        return services;
    }
}
