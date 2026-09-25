using HarborOps.Web.Api.Maintenance;
using Microsoft.AspNetCore.Components;

namespace HarborOps.Web.Components.Pages.Maintenance;

public partial class SupplierReturnEditor : IDisposable
{
    private readonly CancellationTokenSource disposeCancellation = new();
    private readonly Dictionary<Guid, SupplierReturnLineEditor> editors = [];

    /// <summary>
    /// The return to open, or null to start a new draft for <see cref="PurchaseOrderId"/>.
    /// </summary>
    [Parameter]
    public Guid? SupplierReturnId { get; set; }

    [Parameter]
    public Guid PurchaseOrderId { get; set; }

    [Parameter]
    public EventCallback OnClosed { get; set; }

    private (Guid? SupplierReturnId, Guid PurchaseOrderId)? loadedKey;
    private Guid? currentReturnId;
    private PurchaseOrderDetails? purchaseOrder;
    private SupplierReturnDetailsModel? details;
    private SupplierReturnPostingPreviewModel? preview;
    private MaintenanceApiFailure? failure;
    private MaintenanceApiFailure? actionFailure;
    private string? successMessage;
    private string? notes;

    private IReadOnlyDictionary<Guid, string> lineErrors = new Dictionary<Guid, string>();
    private IReadOnlyList<string> formErrors = [];

    private bool isLoading = true;
    private bool isActing;
    private bool isPostConfirmOpen;
    private bool isCancelFormOpen;
    private string? cancelReason;

    private bool CanManage => AuthorizationState.HasPermission("maintenance.manage");

    private bool IsNew => currentReturnId is null;

    private bool IsDraft => IsNew
        || string.Equals(details?.Status, MaintenanceFormat.DraftSupplierReturn, StringComparison.Ordinal);

    private bool CanEdit => CanManage && IsDraft;

    private bool IsOrderReceived => string.Equals(
        purchaseOrder?.Status ?? details?.PurchaseOrderStatus,
        MaintenanceFormat.ReceivedPurchaseOrder,
        StringComparison.Ordinal);

    private IReadOnlyList<SupplierReturnPostingBlockerRow> Blockers =>
        preview?.Blockers ?? details?.Blockers ?? [];

    private bool CanPost => CanEdit && !IsNew && !IsDirty && preview is { CanPost: true };

    private decimal ReturnTotal => preview?.Items.Sum(item => item.ReturnQuantity) ?? 0m;

    /// <summary>
    /// True when the editor holds quantities or notes that were not saved yet. Posting always uses the
    /// saved draft, so the post button waits until the operator saves and checks again.
    /// </summary>
    private bool IsDirty
    {
        get
        {
            if (details is null)
            {
                return editors.Values.Any(editor => editor.Quantity is not null);
            }

            if (!string.Equals(Normalize(notes), Normalize(details.Notes), StringComparison.Ordinal))
            {
                return true;
            }

            foreach (var (lineId, editor) in editors)
            {
                var saved = details.Lines.FirstOrDefault(line => line.PurchaseOrderLineId == lineId);
                var changed = saved is null
                    ? editor.Quantity is not null
                    : editor.Quantity != saved.Quantity
                        || !string.Equals(Normalize(editor.Reason), saved.Reason, StringComparison.Ordinal);

                if (changed)
                {
                    return true;
                }
            }

            return false;
        }
    }

    private IReadOnlyList<ValidationMessage> ValidationMessages =>
    [
        .. lineErrors.Select(entry => new ValidationMessage($"'{PartNumberForLine(entry.Key)}'", entry.Value)),
        .. formErrors.Select(message => new ValidationMessage("Return", message)),
    ];

    protected override Task OnParametersSetAsync()
    {
        // Reload only for a different return or order, so a re-render of the parent keeps unsaved input.
        var key = (SupplierReturnId, PurchaseOrderId);

        if (loadedKey == key)
        {
            return Task.CompletedTask;
        }

        loadedKey = key;
        currentReturnId = SupplierReturnId;

        return ReloadAsync();
    }

    /// <summary>
    /// The rows the editor shows. A draft lists every line of the purchase order so parts can be added;
    /// a posted or cancelled return only lists the lines it carried.
    /// </summary>
    private IReadOnlyList<LineView> Lines
    {
        get
        {
            var saved = (IReadOnlyList<SupplierReturnLineRow>?)preview?.Lines ?? details?.Lines ?? [];

            if (!IsDraft || purchaseOrder is null)
            {
                return saved
                    .Select(line => LineView.FromSaved(line))
                    .ToList();
            }

            return purchaseOrder.Lines
                .Select(orderLine =>
                {
                    var savedLine = saved.FirstOrDefault(line => line.PurchaseOrderLineId == orderLine.LineId);

                    return savedLine is null
                        ? new LineView(
                            orderLine.LineId,
                            orderLine.InventoryItemId,
                            orderLine.InventoryItemName,
                            orderLine.PartNumber,
                            null,
                            orderLine.QuantityOrdered,
                            null,
                            null,
                            null,
                            null,
                            null,
                            null)
                        : LineView.FromSaved(savedLine);
                })
                .ToList();
        }
    }

    private SupplierReturnLineEditor EditorFor(Guid purchaseOrderLineId)
    {
        if (!editors.TryGetValue(purchaseOrderLineId, out var editor))
        {
            editor = new SupplierReturnLineEditor();
            editors[purchaseOrderLineId] = editor;
        }

        return editor;
    }

    private bool IsBlocked(LineView line)
    {
        return Blockers.Any(blocker => blocker.PurchaseOrderLineId == line.PurchaseOrderLineId
            || (blocker.PurchaseOrderLineId is null && blocker.InventoryItemId == line.InventoryItemId));
    }

    private string PartNumberForLine(Guid purchaseOrderLineId)
    {
        return Lines.FirstOrDefault(line => line.PurchaseOrderLineId == purchaseOrderLineId)?.PartNumber
            ?? MaintenanceFormat.MissingValue;
    }

    private string PartNumberForMovement(SupplierReturnMovementRow movement)
    {
        return details?.Lines.FirstOrDefault(line => line.SupplierReturnLineId == movement.SupplierReturnLineId)?.PartNumber
            ?? MaintenanceFormat.MissingValue;
    }

    /// <summary>
    /// Turns the server's field keys, such as "lines[0].quantity", back into the purchase-order line
    /// they belong to, so the message shows next to the quantity that caused it.
    /// </summary>
    private void ApplyValidationErrors(MaintenanceApiFailure failure, IReadOnlyList<Guid> submittedLineIds)
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
                && index < submittedLineIds.Count)
            {
                perLine[submittedLineIds[index]] = perLine.TryGetValue(submittedLineIds[index], out var existing)
                    ? $"{existing} {message}"
                    : message;

                continue;
            }

            remaining.Add(message);
        }

        lineErrors = perLine;
        formErrors = remaining;
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

        if (currentReturnId is { } supplierReturnId)
        {
            MaintenanceApiResult<SupplierReturnDetailsModel> detailsResult;

            try
            {
                detailsResult = await MaintenanceApi.GetSupplierReturnAsync(supplierReturnId, disposeCancellation.Token);
            }
            catch (OperationCanceledException) when (disposeCancellation.IsCancellationRequested)
            {
                return;
            }

            if (!detailsResult.IsSuccess)
            {
                isLoading = false;
                failure = detailsResult.Failure;
                details = null;
                preview = null;

                return;
            }

            ApplyDetails(detailsResult.Value!);
        }
        else
        {
            details = null;
            preview = null;
            notes = null;
            editors.Clear();
        }

        // A draft needs every order line so the operator can add parts; history only needs the return itself.
        if (IsDraft)
        {
            MaintenanceApiResult<PurchaseOrderDetails> orderResult;

            try
            {
                orderResult = await MaintenanceApi.GetPurchaseOrderDetailsAsync(
                    details?.PurchaseOrderId ?? PurchaseOrderId,
                    disposeCancellation.Token);
            }
            catch (OperationCanceledException) when (disposeCancellation.IsCancellationRequested)
            {
                return;
            }

            if (!orderResult.IsSuccess)
            {
                isLoading = false;
                failure = orderResult.Failure;
                purchaseOrder = null;

                return;
            }

            purchaseOrder = orderResult.Value;
        }

        isLoading = false;

        if (CanEdit && !IsNew)
        {
            await LoadPreviewAsync();
        }
    }

    private void ApplyDetails(SupplierReturnDetailsModel loaded)
    {
        details = loaded;
        currentReturnId = loaded.SupplierReturnId;
        notes = loaded.Notes;
        lineErrors = new Dictionary<Guid, string>();
        formErrors = [];
        editors.Clear();

        foreach (var line in loaded.Lines)
        {
            editors[line.PurchaseOrderLineId] = new SupplierReturnLineEditor
            {
                Quantity = line.Quantity,
                Reason = line.Reason,
            };
        }
    }

    private async Task LoadPreviewAsync()
    {
        if (currentReturnId is not { } supplierReturnId || !IsDraft)
        {
            return;
        }

        MaintenanceApiResult<SupplierReturnPostingPreviewModel> result;

        try
        {
            result = await MaintenanceApi.GetSupplierReturnPreviewAsync(supplierReturnId, disposeCancellation.Token);
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
        if (isActing || !CanEdit)
        {
            return;
        }

        isActing = true;
        actionFailure = null;
        successMessage = null;
        lineErrors = new Dictionary<Guid, string>();
        formErrors = [];

        // A blank quantity leaves the line out; anything typed is sent so the server can explain it.
        var entries = Lines
            .Where(line => EditorFor(line.PurchaseOrderLineId).Quantity is not null)
            .Select(line => new SupplierReturnLineEntry
            {
                PurchaseOrderLineId = line.PurchaseOrderLineId,
                Quantity = EditorFor(line.PurchaseOrderLineId).Quantity,
                Reason = EditorFor(line.PurchaseOrderLineId).Reason,
            })
            .ToList();

        MaintenanceApiResult<SupplierReturnDetailsModel> result;

        try
        {
            result = currentReturnId is { } supplierReturnId
                ? await MaintenanceApi.UpdateSupplierReturnAsync(
                    supplierReturnId,
                    new UpdateSupplierReturnRequest
                    {
                        ExpectedConcurrencyToken = details!.ConcurrencyToken,
                        Notes = notes,
                        Lines = entries,
                    },
                    disposeCancellation.Token)
                : await MaintenanceApi.CreateSupplierReturnAsync(
                    new CreateSupplierReturnRequest
                    {
                        PurchaseOrderId = PurchaseOrderId,
                        Notes = notes,
                        Lines = entries,
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
                ApplyValidationErrors(result.Failure, entries.Select(entry => entry.PurchaseOrderLineId!.Value).ToList());
            }

            if (result.Failure is { IsConflict: true } && !IsNew)
            {
                await ReloadAsync();
            }

            return;
        }

        var wasNew = IsNew;

        ApplyDetails(result.Value!);

        await LoadPreviewAsync();

        var check = preview is not { } checkedPreview
            ? string.Empty
            : checkedPreview.Blockers.Count == 0
                ? "Nothing blocks posting."
                : $"{checkedPreview.Blockers.Count} blocker(s) to resolve before posting.";

        successMessage = $"{(wasNew ? "Draft return created." : "Draft saved.")} {check}".TrimEnd();
    }

    private async Task PostAsync()
    {
        if (currentReturnId is not { } supplierReturnId || preview is null || isActing)
        {
            return;
        }

        isActing = true;
        actionFailure = null;
        successMessage = null;

        MaintenanceApiResult<PostSupplierReturnResult> result;

        try
        {
            result = await MaintenanceApi.PostSupplierReturnAsync(
                supplierReturnId,
                new PostSupplierReturnRequest
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
        preview = null;
        successMessage = posted.AlreadyPosted
            ? "This return had already been posted. Stock was not changed again."
            : $"Return posted. {posted.Movements.Count} stock movement(s) recorded.";

        await ReloadAsync();
    }

    private async Task CancelReturnAsync()
    {
        if (currentReturnId is not { } supplierReturnId
            || details is null
            || isActing
            || string.IsNullOrWhiteSpace(cancelReason))
        {
            return;
        }

        isActing = true;
        actionFailure = null;
        successMessage = null;

        MaintenanceApiResult<SupplierReturnDetailsModel> result;

        try
        {
            result = await MaintenanceApi.CancelSupplierReturnAsync(
                supplierReturnId,
                new CancelSupplierReturnRequest
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
        successMessage = "Return cancelled. Stock was not changed.";
    }

    private static string? Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static string Optional(decimal? value)
    {
        return value is { } quantity ? MaintenanceFormat.Quantity(quantity) : MaintenanceFormat.MissingValue;
    }

    public void Dispose()
    {
        disposeCancellation.Cancel();
        disposeCancellation.Dispose();
    }

    private sealed record ValidationMessage(string Label, string Message);

    private sealed record LineView(
        Guid PurchaseOrderLineId,
        Guid InventoryItemId,
        string Name,
        string PartNumber,
        string? UnitAbbreviation,
        decimal ReceivedQuantity,
        decimal? PreviouslyReturnedQuantity,
        decimal? RemainingReturnableQuantity,
        decimal? OnHandQuantity,
        decimal? ReservedQuantity,
        decimal? Quantity,
        string? Reason)
    {
        internal static LineView FromSaved(SupplierReturnLineRow line)
        {
            return new LineView(
                line.PurchaseOrderLineId,
                line.InventoryItemId,
                line.Name,
                line.PartNumber,
                line.UnitAbbreviation,
                line.ReceivedQuantity,
                line.PreviouslyReturnedQuantity,
                line.RemainingReturnableQuantity,
                line.OnHandQuantity,
                line.ReservedQuantity,
                line.Quantity,
                line.Reason);
        }
    }

    private sealed class SupplierReturnLineEditor
    {
        public decimal? Quantity { get; set; }

        public string? Reason { get; set; }
    }
}
