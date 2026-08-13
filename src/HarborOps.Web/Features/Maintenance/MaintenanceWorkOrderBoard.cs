using HarborOps.Web.Api.Fleet;
using HarborOps.Web.Api.Maintenance;

namespace HarborOps.Web.Features.Maintenance;

internal sealed class MaintenanceWorkOrderBoard(
    MaintenanceApiClient maintenanceApiClient,
    FleetApiClient fleetApiClient,
    TimeProvider timeProvider)
{
    private static readonly string[] CompletableStatuses = ["Open", "InProgress"];

    internal const int PageSize = 25;

    internal Guid? VesselIdFilter { get; set; }
    internal string? StatusFilter { get; set; }
    internal string? PriorityFilter { get; set; }
    internal bool? OverdueFilter { get; set; }

    internal int PageNumber { get; private set; } = 1;
    internal int TotalCount { get; private set; }
    internal int TotalPages => TotalCount == 0 ? 1 : (int)Math.Ceiling(TotalCount / (double)PageSize);

    internal BoardLoadState LoadState { get; private set; } = BoardLoadState.Loading;
    internal string? ErrorMessage { get; private set; }
    internal IReadOnlyDictionary<string, string[]>? FilterValidationErrors { get; private set; }
    internal IReadOnlyList<MaintenanceWorkOrderRow> Rows { get; private set; } = [];

    internal bool IsSubmittingCreate { get; private set; }
    internal IReadOnlyDictionary<string, string[]>? CreateValidationErrors { get; private set; }
    internal string? CreateErrorMessage { get; private set; }
    internal bool CreateSucceeded { get; private set; }

    internal bool IsSubmittingCompletion { get; private set; }
    internal IReadOnlyDictionary<string, string[]>? CompletionValidationErrors { get; private set; }
    internal string? CompletionErrorMessage { get; private set; }
    internal string? CompletionConflictMessage { get; private set; }
    internal bool CompletionSucceeded { get; private set; }

    internal async Task LoadAsync(CancellationToken cancellationToken)
    {
        LoadState = BoardLoadState.Loading;
        ErrorMessage = null;
        FilterValidationErrors = null;

        var filter = new MaintenanceWorkOrderFilter(
            VesselIdFilter, StatusFilter, PriorityFilter, OverdueFilter, PageNumber, PageSize);

        var result = await maintenanceApiClient.GetWorkOrdersAsync(filter, cancellationToken);

        switch (result.Status)
        {
            case MaintenanceRequestStatus.Success:
                await ApplyPageAsync(result.Page!, cancellationToken);
                break;
            case MaintenanceRequestStatus.ValidationFailed:
                LoadState = BoardLoadState.ValidationFailed;
                FilterValidationErrors = result.ValidationErrors;
                Rows = [];
                break;
            default:
                LoadState = BoardLoadState.Error;
                ErrorMessage = result.ErrorMessage;
                Rows = [];
                break;
        }
    }

    internal Task ApplyFiltersAsync(CancellationToken cancellationToken)
    {
        PageNumber = 1;
        return LoadAsync(cancellationToken);
    }

    internal Task ClearFiltersAsync(CancellationToken cancellationToken)
    {
        VesselIdFilter = null;
        StatusFilter = null;
        PriorityFilter = null;
        OverdueFilter = null;
        PageNumber = 1;
        return LoadAsync(cancellationToken);
    }

    internal Task GoToPageAsync(int pageNumber, CancellationToken cancellationToken)
    {
        if (pageNumber < 1 || pageNumber > TotalPages || pageNumber == PageNumber)
        {
            return Task.CompletedTask;
        }

        PageNumber = pageNumber;
        return LoadAsync(cancellationToken);
    }

    internal async Task<bool> CreateWorkOrderAsync(
        CreateMaintenanceWorkOrderRequest request, CancellationToken cancellationToken)
    {
        if (IsSubmittingCreate)
        {
            return false;
        }

        IsSubmittingCreate = true;
        CreateValidationErrors = null;
        CreateErrorMessage = null;
        CreateSucceeded = false;

        try
        {
            var result = await maintenanceApiClient.CreateWorkOrderAsync(request, cancellationToken);

            switch (result.Status)
            {
                case MaintenanceRequestStatus.Success:
                    CreateSucceeded = true;
                    await LoadAsync(cancellationToken);
                    return true;
                case MaintenanceRequestStatus.ValidationFailed:
                    CreateValidationErrors = result.ValidationErrors;
                    return false;
                default:
                    CreateErrorMessage = result.ErrorMessage;
                    return false;
            }
        }
        finally
        {
            IsSubmittingCreate = false;
        }
    }

    internal async Task<bool> CompleteWorkOrderAsync(
        Guid workOrderId,
        CompleteMaintenanceWorkOrderRequest request,
        CancellationToken cancellationToken)
    {
        if (IsSubmittingCompletion)
        {
            return false;
        }

        IsSubmittingCompletion = true;
        CompletionValidationErrors = null;
        CompletionErrorMessage = null;
        CompletionConflictMessage = null;
        CompletionSucceeded = false;

        try
        {
            var result = await maintenanceApiClient.CompleteWorkOrderAsync(workOrderId, request, cancellationToken);

            switch (result.Status)
            {
                case MaintenanceRequestStatus.Success:
                    CompletionSucceeded = true;
                    await LoadAsync(cancellationToken);
                    return true;
                case MaintenanceRequestStatus.ValidationFailed:
                    CompletionValidationErrors = result.ValidationErrors;
                    return false;
                case MaintenanceRequestStatus.Conflict:
                case MaintenanceRequestStatus.NotFound:
                    CompletionConflictMessage = result.ErrorMessage;
                    await LoadAsync(cancellationToken);
                    return false;
                default:
                    CompletionErrorMessage = result.ErrorMessage;
                    return false;
            }
        }
        finally
        {
            IsSubmittingCompletion = false;
        }
    }

    private async Task ApplyPageAsync(MaintenanceWorkOrdersPage page, CancellationToken cancellationToken)
    {
        TotalCount = page.TotalCount;
        PageNumber = page.PageNumber;

        var vesselIds = page.Items.Select(item => item.VesselId).Distinct().ToArray();
        var vesselNames = await fleetApiClient.LookupVesselNamesAsync(vesselIds, cancellationToken);
        var utcNow = timeProvider.GetUtcNow().UtcDateTime;

        Rows = page.Items
            .Select(item => new MaintenanceWorkOrderRow(
                item.WorkOrderId,
                item.VesselId,
                vesselNames.TryGetValue(item.VesselId, out var name)
                    ? name
                    : $"Vessel {item.VesselId.ToString()[..8]}",
                item.Title,
                item.Description,
                item.Priority,
                item.Status,
                item.DueUtc,
                item.AssignedEngineerId,
                item.CreatedUtc,
                item.CompletedUtc,
                item.DueUtc < utcNow && CompletableStatuses.Contains(item.Status),
                string.Equals(item.Priority, "Critical", StringComparison.Ordinal),
                CompletableStatuses.Contains(item.Status)))
            .ToList();

        LoadState = Rows.Count == 0 ? BoardLoadState.Empty : BoardLoadState.Loaded;
    }
}
