using HarborOps.Web.Api.Fleet;
using HarborOps.Web.Api.Maintenance;

namespace HarborOps.Web.Components.Pages.Maintenance;

public partial class Maintenance
{
    private readonly CancellationTokenSource disposeCancellation = new();
    private int loadVersion;

    private IReadOnlyList<MaintenanceWorkOrderRow> rows = [];
    private MaintenanceApiFailure? failure;

    private string? vesselIdFilterText;
    private string statusFilter = string.Empty;
    private string priorityFilter = string.Empty;
    private string overdueFilter = string.Empty;

    private int pageNumber = 1;
    private int pageSize = 25;
    private int totalCount;
    private bool isLoading = true;

    private readonly CreateWorkOrderModel createModel = new();
    private bool isCreating;
    private MaintenanceApiFailure? createFailure;
    private string? createSuccessMessage;

    private Guid? completingWorkOrderId;
    private readonly CompleteWorkOrderModel completionModel = new();
    private bool isCompleting;
    private MaintenanceApiFailure? completionFailure;
    private string? completionSuccessMessage;

    private int TotalPages => totalCount == 0 ? 1 : (totalCount + pageSize - 1) / pageSize;

    private int FirstRowNumber => rows.Count == 0 ? 0 : ((pageNumber - 1) * pageSize) + 1;

    private int LastRowNumber => FirstRowNumber + rows.Count - 1;

    protected override Task OnInitializedAsync()
    {
        return LoadAsync();
    }

    private Task ApplyFiltersAsync()
    {
        pageNumber = 1;

        return LoadAsync();
    }

    private Task ClearFiltersAsync()
    {
        vesselIdFilterText = null;
        statusFilter = string.Empty;
        priorityFilter = string.Empty;
        overdueFilter = string.Empty;
        pageNumber = 1;

        return LoadAsync();
    }

    private Task RetryAsync()
    {
        return LoadAsync();
    }

    private Task PreviousPageAsync()
    {
        if (pageNumber <= 1)
        {
            return Task.CompletedTask;
        }

        pageNumber--;

        return LoadAsync();
    }

    private Task NextPageAsync()
    {
        if (pageNumber >= TotalPages)
        {
            return Task.CompletedTask;
        }

        pageNumber++;

        return LoadAsync();
    }

    private async Task LoadAsync()
    {
        var requestVersion = ++loadVersion;
        var cancellationToken = disposeCancellation.Token;
        isLoading = true;
        failure = null;

        var filter = new MaintenanceWorkOrderFilter(
            Guid.TryParse(vesselIdFilterText, out var vesselId) ? vesselId : null,
            string.IsNullOrEmpty(statusFilter) ? null : statusFilter,
            string.IsNullOrEmpty(priorityFilter) ? null : priorityFilter,
            overdueFilter switch { "true" => true, "false" => false, _ => null },
            pageNumber,
            pageSize);

        MaintenanceApiResult<MaintenanceWorkOrdersPage> result;

        try
        {
            result = await MaintenanceApi.GetWorkOrdersAsync(filter, cancellationToken);
        }
        catch (OperationCanceledException) when (disposeCancellation.IsCancellationRequested)
        {
            return;
        }

        if (disposeCancellation.IsCancellationRequested || requestVersion != loadVersion)
        {
            return;
        }

        isLoading = false;

        if (!result.IsSuccess)
        {
            failure = result.Failure;
            rows = [];
            totalCount = 0;

            return;
        }

        var page = result.Value!;
        pageNumber = page.PageNumber;
        pageSize = page.PageSize;
        totalCount = page.TotalCount;

        await ComposeRowsAsync(page.Items, cancellationToken, requestVersion);
    }

    private async Task ComposeRowsAsync(
        IReadOnlyList<MaintenanceWorkOrderListItem> items,
        CancellationToken cancellationToken,
        int requestVersion)
    {
        var vesselIds = items.Select(item => item.VesselId).Distinct().ToArray();
        var vesselNames = await VesselLookupApi.LookupVesselNamesAsync(vesselIds, cancellationToken);

        if (disposeCancellation.IsCancellationRequested || requestVersion != loadVersion)
        {
            return;
        }

        rows = items
            .Select(item => new MaintenanceWorkOrderRow(
                item.WorkOrderId,
                item.VesselId,
                vesselNames.TryGetValue(item.VesselId, out var name)
                    ? name
                    : MaintenanceFormat.ShortVesselReference(item.VesselId),
                item.Title,
                item.Description,
                item.Priority,
                item.Status,
                item.DueUtc,
                item.AssignedEngineerId,
                item.CreatedUtc,
                item.CompletedUtc))
            .ToList();
    }

    private async Task SubmitCreateAsync()
    {
        if (isCreating)
        {
            return;
        }

        isCreating = true;
        createFailure = null;
        createSuccessMessage = null;

        var request = new CreateMaintenanceWorkOrderRequest
        {
            VesselId = Guid.TryParse(createModel.VesselId, out var vesselId) ? vesselId : null,
            Title = createModel.Title,
            Description = createModel.Description,
            Priority = string.IsNullOrEmpty(createModel.Priority) ? null : createModel.Priority,
            DueUtc = createModel.DueUtcLocal is { } dueLocal
                ? DateTime.SpecifyKind(dueLocal, DateTimeKind.Utc)
                : null,
        };

        var result = await MaintenanceApi.CreateWorkOrderAsync(request, CancellationToken.None);

        isCreating = false;

        if (!result.IsSuccess)
        {
            createFailure = result.Failure;

            return;
        }

        createSuccessMessage = "Work order created.";
        createModel.Reset();

        await LoadAsync();
    }

    private void OpenCompletionForm(Guid workOrderId)
    {
        completingWorkOrderId = workOrderId;
        completionModel.Reset();
        completionFailure = null;
        completionSuccessMessage = null;
    }

    private void CloseCompletionForm()
    {
        completingWorkOrderId = null;
        completionFailure = null;
    }

    private async Task SubmitCompletionAsync()
    {
        if (isCompleting || completingWorkOrderId is not { } workOrderId)
        {
            return;
        }

        isCompleting = true;
        completionFailure = null;
        completionSuccessMessage = null;

        var request = new CompleteMaintenanceWorkOrderRequest
        {
            TechnicianId = Guid.TryParse(completionModel.TechnicianId, out var technicianId) ? technicianId : null,
            CompletedUtc = completionModel.CompletedUtcLocal is { } completedLocal
                ? DateTime.SpecifyKind(completedLocal, DateTimeKind.Utc)
                : null,
            Notes = completionModel.Notes,
            Cost = completionModel.Cost,
            LaborHours = completionModel.LaborHours,
        };

        var result = await MaintenanceApi.CompleteWorkOrderAsync(workOrderId, request, CancellationToken.None);

        isCompleting = false;

        if (!result.IsSuccess)
        {
            completionFailure = result.Failure;

            if (result.Failure is { IsConflict: true } or { IsNotFound: true })
            {
                completingWorkOrderId = null;
                await LoadAsync();
            }

            return;
        }

        completionSuccessMessage = "Work order completed.";
        completingWorkOrderId = null;

        await LoadAsync();
    }

    public void Dispose()
    {
        disposeCancellation.Cancel();
        disposeCancellation.Dispose();
    }

    private sealed class CreateWorkOrderModel
    {
        public string? VesselId { get; set; }

        public string? Title { get; set; }

        public string? Description { get; set; }

        public string? Priority { get; set; } = string.Empty;

        public DateTime? DueUtcLocal { get; set; }

        public void Reset()
        {
            VesselId = null;
            Title = null;
            Description = null;
            Priority = string.Empty;
            DueUtcLocal = null;
        }
    }

    private sealed class CompleteWorkOrderModel
    {
        public string? TechnicianId { get; set; }

        public DateTime? CompletedUtcLocal { get; set; } = DateTime.UtcNow;

        public string? Notes { get; set; }

        public decimal? Cost { get; set; }

        public decimal? LaborHours { get; set; }

        public void Reset()
        {
            TechnicianId = null;
            CompletedUtcLocal = DateTime.UtcNow;
            Notes = null;
            Cost = null;
            LaborHours = null;
        }
    }
}
