using HarborOps.Modules.Fleet.Contracts;
using HarborOps.Modules.Maintenance.Contracts;

namespace HarborOps.Api.Composition.Operations.FleetVessel;

public sealed class FleetVesselOverviewService(
    ISearchVesselsQuery searchVesselsQuery,
    IVesselMaintenanceOverviewQuery vesselMaintenanceOverviewQuery)
{
    public async Task<FleetVesselOverviewPage> QueryAsync(
        Guid fleetId,
        int pageNumber,
        int pageSize,
        string? search,
        CancellationToken cancellationToken)
    {
        var errors = FleetVesselOverviewValidator.Validate(fleetId, pageNumber, pageSize);

        if (errors.Count > 0)
        {
            throw new FleetVesselOverviewValidationException(errors);
        }

        var vesselPage = await searchVesselsQuery.SearchAsync(
            new SearchVesselsRequest
            {
                FleetId = fleetId,
                PageNumber = pageNumber,
                PageSize = pageSize,
                Search = search,
            },
            cancellationToken);

        if (vesselPage.Items.Count == 0)
        {
            return new FleetVesselOverviewPage(
                [],
                vesselPage.PageNumber,
                vesselPage.PageSize,
                vesselPage.TotalCount);
        }

        var vesselIds = vesselPage.Items
            .Select(vessel => vessel.Id)
            .ToArray();

        var overview = await vesselMaintenanceOverviewQuery.GetVesselMaintenanceOverviewAsync(
            new VesselMaintenanceOverviewRequest { VesselIds = vesselIds },
            cancellationToken);

        var summariesByVesselId = overview.Summaries
            .ToDictionary(summary => summary.VesselId);

        var rows = vesselPage.Items
            .Select(vessel => Compose(
                vessel,
                summariesByVesselId.GetValueOrDefault(vessel.Id)))
            .ToArray();

        return new FleetVesselOverviewPage(
            rows,
            vesselPage.PageNumber,
            vesselPage.PageSize,
            vesselPage.TotalCount);
    }

    private static FleetVesselOverviewRow Compose(
        VesselListItem vessel,
        VesselMaintenanceOverviewSummary? summary)
    {
        return new FleetVesselOverviewRow(
            vessel.Id,
            vessel.Name,
            vessel.ImoNumber,
            vessel.FlagState,
            vessel.CurrentStatus,
            vessel.LifecycleStage,
            vessel.VesselTypeId,
            vessel.VesselTypeName,
            vessel.FleetId,
            vessel.FleetName,
            summary?.OpenWorkOrderCount ?? 0,
            summary?.OverdueWorkOrderCount ?? 0,
            summary?.CriticalOpenWorkOrderCount ?? 0,
            summary?.NextWorkOrderDueUtc);
    }
}
