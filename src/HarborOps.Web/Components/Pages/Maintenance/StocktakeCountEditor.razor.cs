using HarborOps.Web.Api.Maintenance;
using Microsoft.AspNetCore.Components;

namespace HarborOps.Web.Components.Pages.Maintenance;

public partial class StocktakeCountEditor : IDisposable
{
    private readonly CancellationTokenSource disposeCancellation = new();
    private readonly Dictionary<Guid, StocktakeLineEditor> editors = [];

    [Parameter]
    public Guid StocktakeId { get; set; }

    [Parameter]
    public EventCallback OnClosed { get; set; }

    private Guid loadedStocktakeId;
    private StocktakeDetailsModel? details;
    private StocktakePostingPreviewModel? preview;
    private MaintenanceApiFailure? failure;
    private MaintenanceApiFailure? actionFailure;
    private string? successMessage;

    private bool isLoading = true;
    private bool isActing;
    private bool isPostConfirmOpen;
    private bool isCancelFormOpen;
    private string? cancelReason;

    private bool CanManage => AuthorizationState.HasPermission("maintenance.manage");

    private bool IsDraft => string.Equals(details?.Status, MaintenanceFormat.DraftStocktake, StringComparison.Ordinal);

    private bool CanEdit => CanManage && IsDraft;

    private bool HasRefreshSelection => editors.Values.Any(editor => editor.IsSelectedForRefresh);

    private int VarianceCount => details?.Lines.Count(line => line.Variance is not null and not 0) ?? 0;

    protected override Task OnParametersSetAsync()
    {
        // Reload only for a different stocktake, so a re-render of the parent keeps unsaved counts.
        if (loadedStocktakeId == StocktakeId)
        {
            return Task.CompletedTask;
        }

        loadedStocktakeId = StocktakeId;

        return ReloadAsync();
    }

    private StocktakeLineEditor EditorFor(Guid inventoryItemId)
    {
        if (!editors.TryGetValue(inventoryItemId, out var editor))
        {
            editor = new StocktakeLineEditor();
            editors[inventoryItemId] = editor;
        }

        return editor;
    }

    private StocktakePreviewLineRow? PreviewFor(Guid inventoryItemId)
    {
        return preview?.Lines.FirstOrDefault(line => line.InventoryItemId == inventoryItemId);
    }

    private string PartNumberFor(Guid inventoryItemId)
    {
        return details?.Lines.FirstOrDefault(line => line.InventoryItemId == inventoryItemId)?.PartNumber
            ?? MaintenanceFormat.MissingValue;
    }

    private void ToggleRefreshSelection(Guid inventoryItemId, ChangeEventArgs args)
    {
        if (editors.TryGetValue(inventoryItemId, out var editor))
        {
            editor.IsSelectedForRefresh = args.Value is true;
        }
    }

    private void AskToPost()
    {
        actionFailure = null;
        isPostConfirmOpen = true;
    }

    private void DismissPostConfirmation()
    {
        if (isActing)
        {
            return;
        }

        isPostConfirmOpen = false;
    }

    private void OpenCancelForm()
    {
        actionFailure = null;
        cancelReason = null;
        isCancelFormOpen = true;
    }

    private void DismissCancelForm()
    {
        if (isActing)
        {
            return;
        }

        isCancelFormOpen = false;
    }

    private Task CloseAsync()
    {
        return OnClosed.InvokeAsync();
    }

    private async Task ReloadAsync()
    {
        isLoading = true;
        failure = null;

        MaintenanceApiResult<StocktakeDetailsModel> result;

        try
        {
            result = await MaintenanceApi.GetStocktakeAsync(StocktakeId, disposeCancellation.Token);
        }
        catch (OperationCanceledException) when (disposeCancellation.IsCancellationRequested)
        {
            return;
        }

        isLoading = false;

        if (!result.IsSuccess)
        {
            failure = result.Failure;
            details = null;
            preview = null;

            return;
        }

        ApplyDetails(result.Value!);

        if (CanEdit)
        {
            await LoadPreviewAsync();
        }
    }

    private void ApplyDetails(StocktakeDetailsModel loaded)
    {
        details = loaded;
        editors.Clear();

        foreach (var line in loaded.Lines)
        {
            editors[line.InventoryItemId] = new StocktakeLineEditor
            {
                CountedQuantity = line.CountedQuantity,
                Reason = line.VarianceReason,
            };
        }
    }

    private async Task LoadPreviewAsync()
    {
        if (details is null || !IsDraft)
        {
            return;
        }

        MaintenanceApiResult<StocktakePostingPreviewModel> result;

        try
        {
            result = await MaintenanceApi.GetStocktakePreviewAsync(StocktakeId, disposeCancellation.Token);
        }
        catch (OperationCanceledException) when (disposeCancellation.IsCancellationRequested)
        {
            return;
        }

        if (!result.IsSuccess)
        {
            preview = null;
            actionFailure = result.Failure;

            return;
        }

        preview = result.Value;
    }

    private async Task SaveCountsAsync()
    {
        if (details is null || isActing)
        {
            return;
        }

        isActing = true;
        actionFailure = null;
        successMessage = null;

        var lines = details.Lines
            .Select(line => new StocktakeCountEntry
            {
                InventoryItemId = line.InventoryItemId,
                CountedQuantity = EditorFor(line.InventoryItemId).CountedQuantity,
                Reason = EditorFor(line.InventoryItemId).Reason,
            })
            .ToList();

        MaintenanceApiResult<StocktakeDetailsModel> result;

        try
        {
            result = await MaintenanceApi.UpdateStocktakeCountsAsync(
                StocktakeId,
                new UpdateStocktakeCountsRequest
                {
                    ExpectedConcurrencyToken = details.ConcurrencyToken,
                    Lines = lines,
                },
                disposeCancellation.Token);
        }
        catch (OperationCanceledException) when (disposeCancellation.IsCancellationRequested)
        {
            return;
        }

        isActing = false;

        if (!result.IsSuccess)
        {
            actionFailure = result.Failure;

            if (result.Failure is { IsConflict: true })
            {
                await ReloadAsync();
            }

            return;
        }

        ApplyDetails(result.Value!);
        successMessage = "Counts saved.";

        await LoadPreviewAsync();
    }

    private async Task RefreshSelectedAsync()
    {
        if (details is null || isActing || !HasRefreshSelection)
        {
            return;
        }

        isActing = true;
        actionFailure = null;
        successMessage = null;

        var inventoryItemIds = editors
            .Where(pair => pair.Value.IsSelectedForRefresh)
            .Select(pair => pair.Key)
            .ToList();

        MaintenanceApiResult<StocktakeDetailsModel> result;

        try
        {
            result = await MaintenanceApi.RefreshStocktakeLinesAsync(
                StocktakeId,
                new RefreshStocktakeLinesRequest
                {
                    ExpectedConcurrencyToken = details.ConcurrencyToken,
                    InventoryItemIds = inventoryItemIds,
                },
                disposeCancellation.Token);
        }
        catch (OperationCanceledException) when (disposeCancellation.IsCancellationRequested)
        {
            return;
        }

        isActing = false;

        if (!result.IsSuccess)
        {
            actionFailure = result.Failure;

            if (result.Failure is { IsConflict: true })
            {
                await ReloadAsync();
            }

            return;
        }

        ApplyDetails(result.Value!);
        successMessage = $"{inventoryItemIds.Count} line(s) refreshed. Count them again before posting.";

        await LoadPreviewAsync();
    }

    private async Task PostAsync()
    {
        if (details is null || preview is null || isActing)
        {
            return;
        }

        isActing = true;
        actionFailure = null;
        successMessage = null;

        MaintenanceApiResult<PostStocktakeResult> result;

        try
        {
            result = await MaintenanceApi.PostStocktakeAsync(
                StocktakeId,
                new PostStocktakeRequest
                {
                    ExpectedConcurrencyToken = preview.ConcurrencyToken,
                    PreviewToken = preview.PreviewToken,
                },
                disposeCancellation.Token);
        }
        catch (OperationCanceledException) when (disposeCancellation.IsCancellationRequested)
        {
            return;
        }

        isActing = false;
        isPostConfirmOpen = false;

        if (!result.IsSuccess)
        {
            actionFailure = result.Failure;

            await ReloadAsync();

            return;
        }

        var posted = result.Value!;
        successMessage = $"Stocktake posted with {posted.Adjustments.Count} adjustment(s).";

        await ReloadAsync();
    }

    private async Task CancelStocktakeAsync()
    {
        if (details is null || isActing || string.IsNullOrWhiteSpace(cancelReason))
        {
            return;
        }

        isActing = true;
        actionFailure = null;
        successMessage = null;

        MaintenanceApiResult<StocktakeDetailsModel> result;

        try
        {
            result = await MaintenanceApi.CancelStocktakeAsync(
                StocktakeId,
                new CancelStocktakeRequest
                {
                    ExpectedConcurrencyToken = details.ConcurrencyToken,
                    Reason = cancelReason,
                },
                disposeCancellation.Token);
        }
        catch (OperationCanceledException) when (disposeCancellation.IsCancellationRequested)
        {
            return;
        }

        isActing = false;
        isCancelFormOpen = false;

        if (!result.IsSuccess)
        {
            actionFailure = result.Failure;

            if (result.Failure is { IsConflict: true })
            {
                await ReloadAsync();
            }

            return;
        }

        ApplyDetails(result.Value!);
        preview = null;
        successMessage = "Stocktake cancelled. Inventory was not changed.";
    }

    public void Dispose()
    {
        disposeCancellation.Cancel();
        disposeCancellation.Dispose();
    }

    private sealed class StocktakeLineEditor
    {
        public decimal? CountedQuantity { get; set; }

        public string? Reason { get; set; }

        public bool IsSelectedForRefresh { get; set; }
    }
}
