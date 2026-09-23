using System.Reflection;
using HarborOps.Modules.Voyages.Application;
using HarborOps.Modules.Voyages.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace HarborOps.Modules.Voyages;

public static class VoyagesModule
{
    public const string Schema = "voyages";

    public static Assembly Assembly { get; } = typeof(VoyagesModule).Assembly;

    public static IServiceCollection AddVoyagesModule(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<ScheduleVoyageService>();
        services.AddScoped<RecordVoyageDepartureService>();
        services.AddScoped<RecordVoyageArrivalService>();
        services.AddScoped<IGetVoyagesQuery, GetVoyagesQuery>();
        services.AddScoped<IOperationsDashboardVoyagesQuery, OperationsDashboardVoyagesQuery>();
        services.AddScoped<RegisterPortService>();
        services.AddScoped<ISearchPortsQuery, SearchPortsQuery>();
        services.AddScoped<IPortLookupQuery, PortLookupQuery>();
        services.AddScoped<IPortNamesQuery, PortNamesQuery>();
        services.AddScoped<RegisterBerthService>();
        services.AddScoped<ISearchBerthsQuery, SearchBerthsQuery>();
        services.AddScoped<IBerthLookupQuery, BerthLookupQuery>();
        services.AddScoped<RegisterRouteService>();
        services.AddScoped<ISearchRoutesQuery, SearchRoutesQuery>();
        services.AddScoped<IGetRouteDetailsQuery, GetRouteDetailsQuery>();
        services.AddScoped<IRouteLookupQuery, RouteLookupQuery>();
        services.AddScoped<PlanVoyageItineraryService>();
        services.AddScoped<IGetVoyageItineraryQuery, GetVoyageItineraryQuery>();
        services.AddScoped<AssignBerthService>();
        services.AddScoped<IGetBerthAvailabilityQuery, GetBerthAvailabilityQuery>();
        services.AddScoped<ReplaceBerthAllocationService>();
        services.AddScoped<IGetBerthAllocationHistoryQuery, GetBerthAllocationHistoryQuery>();
        services.AddScoped<RecordStopArrivalService>();
        services.AddScoped<RecordStopDepartureService>();
        services.AddScoped<IGetPortCallClearanceQuery, GetPortCallClearanceQuery>();
        services.AddScoped<AddPortCallClearanceItemService>();
        services.AddScoped<UpdatePortCallClearanceItemService>();
        services.AddScoped<ClearPortCallClearanceItemService>();
        services.AddScoped<WaivePortCallClearanceItemService>();
        services.AddScoped<CompletePortCallClearanceService>();
        services.AddScoped<ReopenPortCallClearanceService>();
        services.AddScoped<IGetPortCallActivitiesQuery, GetPortCallActivitiesQuery>();
        services.AddScoped<RecordPortCallActivityService>();
        services.AddScoped<CorrectPortCallActivityService>();
        services.AddScoped<VoidPortCallActivityService>();
        services.AddScoped<IGetPortCallStatementQuery, GetPortCallStatementQuery>();
        services.AddScoped<CreatePortCallStatementService>();
        services.AddScoped<FinalizePortCallStatementService>();
        services.AddScoped<RecordDelayService>();
        services.AddScoped<DelayImpactPreviewService>();
        services.AddScoped<ApplyDelayImpactService>();
        services.AddScoped<IGetVoyageEstimateRevisionsQuery, GetVoyageEstimateRevisionsQuery>();
        services.AddScoped<AmendVoyageScheduleService>();
        services.AddScoped<CancelVoyageService>();
        services.AddScoped<IGetVoyageScheduleChangesQuery, GetVoyageScheduleChangesQuery>();
        services.AddControllers()
            .AddApplicationPart(Assembly);

        return services;
    }
}
