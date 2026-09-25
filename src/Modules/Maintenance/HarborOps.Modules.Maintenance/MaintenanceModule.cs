using System.Reflection;
using HarborOps.Modules.Maintenance.Application;
using HarborOps.Modules.Maintenance.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace HarborOps.Modules.Maintenance;

public static class MaintenanceModule
{
    public const string Schema = "maintenance";

    public static Assembly Assembly { get; } = typeof(MaintenanceModule).Assembly;

    public static IServiceCollection AddMaintenanceModule(this IServiceCollection services)
    {
        services.AddScoped<CreateMaintenanceWorkOrderService>();
        services.AddScoped<GetVesselMaintenanceOverviewService>();
        services.AddScoped<CompleteMaintenanceWorkOrderService>();
        services.AddScoped<RecordInventoryItemUsageService>();
        services.AddScoped<GetMaintenanceWorkOrdersQuery>();
        services.AddScoped<IOperationsDashboardMaintenanceQuery, OperationsDashboardMaintenanceQuery>();
        services.AddScoped<IReportsMaintenanceBacklogQuery, ReportsMaintenanceBacklogQuery>();
        services.AddScoped<GetInventoryItemsQuery>();
        services.AddScoped<GetWorkOrderPartUsagesQuery>();
        services.AddScoped<RegisterEquipmentService>();
        services.AddScoped<GetEquipmentQuery>();
        services.AddScoped<RegisterMaintenanceScheduleService>();
        services.AddScoped<GetMaintenanceSchedulesQuery>();
        services.AddScoped<GetMaintenanceScheduleDetailsQuery>();
        services.AddScoped<UpdateScheduleNextDueService>();
        services.AddScoped<ScheduleDryDockService>();
        services.AddScoped<GetDryDocksQuery>();
        services.AddScoped<GetDryDockDetailsQuery>();
        services.AddScoped<StartDryDockService>();
        services.AddScoped<CompleteDryDockService>();
        services.AddScoped<CancelDryDockService>();
        services.AddScoped<RegisterSupplierService>();
        services.AddScoped<GetSuppliersQuery>();
        services.AddScoped<CreatePurchaseOrderService>();
        services.AddScoped<GetPurchaseOrdersQuery>();
        services.AddScoped<GetPurchaseOrderDetailsQuery>();
        services.AddScoped<ChangePurchaseOrderStatusService>();
        services.AddScoped<GetMaintenanceWorkOrderDetailsQuery>();
        services.AddScoped<ReplaceWorkOrderEquipmentService>();
        services.AddScoped<StartMaintenanceWorkOrderService>();
        services.AddScoped<CancelMaintenanceWorkOrderService>();
        services.AddScoped<GetWorkOrderPartReservationsQuery>();
        services.AddScoped<ReplaceWorkOrderPartReservationsService>();
        services.AddScoped<ReleaseWorkOrderPartReservationsService>();
        services.AddScoped<GetInventoryReplenishmentQuery>();
        services.AddScoped<RegisterDefectService>();
        services.AddScoped<TriageDefectService>();
        services.AddScoped<RejectDefectService>();
        services.AddScoped<ConvertDefectToWorkOrderService>();
        services.AddScoped<GetDefectsQuery>();
        services.AddScoped<GetDefectByIdQuery>();
        services.AddScoped<GetPreventiveWorkForecastQuery>();
        services.AddScoped<GeneratePreventiveWorkService>();
        services.AddScoped<GetScheduleOccurrencesQuery>();
        services.AddScoped<GetScheduleWorkOrdersQuery>();
        services.AddScoped<GetStocktakesQuery>();
        services.AddScoped<GetStocktakeDetailsQuery>();
        services.AddScoped<CreateStocktakeService>();
        services.AddScoped<UpdateStocktakeCountsService>();
        services.AddScoped<RefreshStocktakeLinesService>();
        services.AddScoped<GetStocktakePostingPreviewQuery>();
        services.AddScoped<PostStocktakeService>();
        services.AddScoped<CancelStocktakeService>();
        services.AddScoped<GetSupplierReturnsQuery>();
        services.AddScoped<GetSupplierReturnDetailsQuery>();
        services.AddScoped<CreateSupplierReturnService>();
        services.AddScoped<UpdateSupplierReturnService>();
        services.AddScoped<GetSupplierReturnPostingPreviewQuery>();
        services.AddScoped<PostSupplierReturnService>();
        services.AddScoped<CancelSupplierReturnService>();
        services.AddScoped<IVesselMaintenanceOverviewQuery>(serviceProvider =>
            serviceProvider.GetRequiredService<GetVesselMaintenanceOverviewService>());
        services.TryAddSingleton(TimeProvider.System);
        services.AddControllers()
            .AddApplicationPart(Assembly);

        return services;
    }
}
