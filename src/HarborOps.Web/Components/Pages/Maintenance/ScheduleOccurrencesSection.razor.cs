using HarborOps.Web.Api.Maintenance;
using Microsoft.AspNetCore.Components;

namespace HarborOps.Web.Components.Pages.Maintenance;

public partial class ScheduleOccurrencesSection : IDisposable
{
    private readonly CancellationTokenSource disposeCancellation = new();

    [Parameter]
    public Guid ScheduleId { get; set; }

    [Parameter]
    public bool CanManage { get; set; }

    /// <summary>
    /// Raised after a replacement so the parent board can refresh its due state.
    /// </summary>
    [Parameter]
    public EventCallback OnChanged { get; set; }

    private Guid loadedScheduleId;
    private int loadVersion;

    private IReadOnlyList<OccurrenceRow> rows = [];
    private MaintenanceApiFailure? failure;
    private bool isLoading = true;

    private OccurrenceRow? pendingReplacement;
    private bool isReplacing;
    private MaintenanceApiFailure? replacementFailure;
    private string? successMessage;
    private Guid? successWorkOrderId;

    protected override Task OnParametersSetAsync()
    {
        if (loadedScheduleId == ScheduleId)
        {
            return Task.CompletedTask;
        }

        loadedScheduleId = ScheduleId;
        pendingReplacement = null;
        replacementFailure = null;
        successMessage = null;
        successWorkOrderId = null;

        return LoadAsync();
    }

    private static string WorkOrderHref(Guid workOrderId)
    {
        return $"/maintenance/work-orders/{workOrderId}";
    }

    private Task RetryAsync()
    {
        return LoadAsync();
    }

    private async Task LoadAsync()
    {
        var requestVersion = ++loadVersion;
        var scheduleId = ScheduleId;
        var cancellationToken = disposeCancellation.Token;
        isLoading = true;
        failure = null;

        MaintenanceApiResult<ScheduleOccurrencesResult> occurrences;
        MaintenanceApiResult<ScheduleWorkOrdersResult> workOrders;

        try
        {
            var occurrencesTask = MaintenanceApi.GetScheduleOccurrencesAsync(scheduleId, cancellationToken);
            var workOrdersTask = MaintenanceApi.GetScheduleWorkOrdersAsync(scheduleId, cancellationToken);

            occurrences = await occurrencesTask;
            workOrders = await workOrdersTask;
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

        if (!occurrences.IsSuccess || !workOrders.IsSuccess)
        {
            failure = occurrences.Failure ?? workOrders.Failure;
            rows = [];

            return;
        }

        var workOrdersByOccurrence = workOrders.Value!.Items.ToDictionary(item => item.OccurrenceId);

        rows = occurrences.Value!.Items
            .Select(item => new OccurrenceRow(
                item.OccurrenceId,
                item.DueDate,
                item.State,
                item.WorkOrderId,
                workOrdersByOccurrence.TryGetValue(item.OccurrenceId, out var workOrder) ? workOrder.Status : null,
                item.ReplacedWorkOrderId,
                item.ResolvedUtc))
            .ToList();
    }

    private void AskToReplace(OccurrenceRow row)
    {
        replacementFailure = null;
        successMessage = null;
        pendingReplacement = row;
    }

    private void DismissReplacement()
    {
        if (isReplacing)
        {
            return;
        }

        pendingReplacement = null;
        replacementFailure = null;
    }

    private async Task ConfirmReplacementAsync()
    {
        if (isReplacing || pendingReplacement is not { } row)
        {
            return;
        }

        isReplacing = true;
        replacementFailure = null;

        MaintenanceApiResult<PreventiveWorkReplacement> result;

        try
        {
            result = await MaintenanceApi.ReplacePreventiveWorkOrderAsync(
                row.OccurrenceId,
                new GeneratePreventiveWorkWebRequest(),
                disposeCancellation.Token);
        }
        catch (OperationCanceledException) when (disposeCancellation.IsCancellationRequested)
        {
            return;
        }

        isReplacing = false;

        if (!result.IsSuccess)
        {
            replacementFailure = result.Failure;

            if (result.Failure is { IsConflict: true } or { IsNotFound: true })
            {
                await LoadAsync();
            }

            return;
        }

        pendingReplacement = null;
        successMessage = "A replacement work order was created.";
        successWorkOrderId = result.Value!.WorkOrderId;

        await LoadAsync();
        await OnChanged.InvokeAsync();
    }

    public void Dispose()
    {
        disposeCancellation.Cancel();
        disposeCancellation.Dispose();
    }

    private sealed record OccurrenceRow(
        Guid OccurrenceId,
        DateOnly DueDate,
        string State,
        Guid WorkOrderId,
        string? WorkOrderStatus,
        Guid? ReplacedWorkOrderId,
        DateTime? ResolvedUtc);
}
