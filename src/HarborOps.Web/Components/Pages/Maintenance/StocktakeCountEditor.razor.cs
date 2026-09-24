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

    private IReadOnlyDictionary<Guid, string> lineErrors = new Dictionary<Guid, string>();
    private IReadOnlyList<string> formErrors = [];

    private bool isLoading = true;
    private bool isActing;
    private bool isPostConfirmOpen;
    private bool isCancelFormOpen;
    private string? cancelReason;

    private bool CanManage => AuthorizationState.HasPermission("maintenance.manage");

    private bool IsDraft => string.Equals(details?.Status, MaintenanceFormat.DraftStocktake, StringComparison.Ordinal);

    private bool CanEdit => CanManage && IsDraft;

    private string CheckSummary => preview is not { } checkedPreview
        ? string.Empty
        : checkedPreview.Blockers.Count == 0
            ? $"Checked at {MaintenanceFormat.CountedUtc(checkedPreview.GeneratedUtc)}, nothing blocks posting."
            : $"Checked at {MaintenanceFormat.CountedUtc(checkedPreview.GeneratedUtc)}, "
                + $"{checkedPreview.Blockers.Count} blocker(s) to resolve.";

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

    private IReadOnlyList<ValidationMessage> ValidationMessages =>
    [
        .. lineErrors.Select(entry => new ValidationMessage(
            $"'{PartNumberFor(entry.Key)}'",
            entry.Value)),
        .. formErrors.Select(message => new ValidationMessage("Stocktake", message)),
    ];

    /// <summary>
    /// Turns the server's field keys, such as "lines[0].reason", back into the line they belong to,
    /// so the operator reads the message next to the count that caused it.
    /// </summary>
    private void ApplyValidationErrors(MaintenanceApiFailure failure, IReadOnlyList<Guid> submittedItemIds)
    {
        var perLine = new Dictionary<Guid, string>();
        var remaining = new List<string>();

        foreach (var (key, messages) in failure.Errors)
        {
            var message = string.Join(" ", messages);
            var start = key.IndexOf('[', StringComparison.Ordinal);
            var end = key.IndexOf(']', StringComparison.Ordinal);

            if (key.StartsWith("lines[", StringComparison.Ordinal)
                && end > start
                && int.TryParse(key.AsSpan(start + 1, end - start - 1), out var index)
                && index >= 0
                && index < submittedItemIds.Count)
            {
                perLine[submittedItemIds[index]] = message;

                continue;
            }

            remaining.Add(message);
        }

        lineErrors = perLine;
        formErrors = remaining;
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
        lineErrors = new Dictionary<Guid, string>();
        formErrors = [];
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

    private async Task SaveAndCheckAsync()
    {
        if (details is null || isActing)
        {
            return;
        }

        isActing = true;
        actionFailure = null;
        successMessage = null;
        lineErrors = new Dictionary<Guid, string>();
        formErrors = [];

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

            if (result.Failure is { IsValidation: true })
            {
                ApplyValidationErrors(result.Failure, lines.Select(line => line.InventoryItemId!.Value).ToList());
            }

            if (result.Failure is { IsConflict: true })
            {
                await ReloadAsync();
            }

            return;
        }

        ApplyDetails(result.Value!);

        await LoadPreviewAsync();

        successMessage = $"Counts saved. {CheckSummary}".TrimEnd();
    }

    private async Task RecountAsync(Guid inventoryItemId)
    {
        if (details is null || isActing)
        {
            return;
        }

        isActing = true;
        actionFailure = null;
        successMessage = null;

        var partNumber = details.Lines
            .FirstOrDefault(line => line.InventoryItemId == inventoryItemId)?.PartNumber;

        MaintenanceApiResult<StocktakeDetailsModel> result;

        try
        {
            result = await MaintenanceApi.RefreshStocktakeLinesAsync(
                StocktakeId,
                new RefreshStocktakeLinesRequest
                {
                    ExpectedConcurrencyToken = details.ConcurrencyToken,
                    InventoryItemIds = [inventoryItemId],
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

        await LoadPreviewAsync();

        successMessage = $"'{partNumber}' now carries the current baseline. Count it again before posting.";
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

    private sealed record ValidationMessage(string Label, string Message);

    private sealed class StocktakeLineEditor
    {
        public decimal? CountedQuantity { get; set; }

        public string? Reason { get; set; }
    }
}
