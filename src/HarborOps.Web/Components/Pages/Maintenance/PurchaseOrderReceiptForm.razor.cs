using System.Globalization;
using HarborOps.Web.Api.Maintenance;
using Microsoft.AspNetCore.Components;

namespace HarborOps.Web.Components.Pages.Maintenance;

public partial class PurchaseOrderReceiptForm : IDisposable
{
    private readonly CancellationTokenSource disposeCancellation = new();
    private readonly Dictionary<Guid, ReceiptLineEditor> editors = [];

    [Parameter, EditorRequired]
    public Guid PurchaseOrderId { get; set; }

    /// <summary>
    /// True to receive every outstanding quantity; false to enter the quantities of one delivery.
    /// </summary>
    [Parameter]
    public bool ReceiveRemaining { get; set; }

    [Parameter]
    public EventCallback<string> OnPosted { get; set; }

    [Parameter]
    public EventCallback OnClosed { get; set; }

    private Guid? loadedPurchaseOrderId;
    private PurchaseOrderDetails? purchaseOrder;
    private MaintenanceApiFailure? loadFailure;
    private bool isLoading = true;
    private DateTime? receivedUtcLocal;
    private string? deliveryReference;
    private string? notes;

    private PurchaseOrderReceiptPostingPreviewModel? preview;
    private string? checkedInput;

    // One id per checked receipt: resending the same confirmation after a lost response is replayed by the
    // server instead of receiving the goods twice.
    private Guid clientRequestId;

    private MaintenanceApiFailure? failure;
    private IReadOnlyDictionary<Guid, string> lineErrors = new Dictionary<Guid, string>();
    private IReadOnlyList<ValidationMessage> formErrors = [];

    private bool isChecking;
    private bool isPosting;
    private bool isConfirmOpen;

    private bool IsBusy => isLoading || isChecking || isPosting;

    // Only read once the order has loaded; the markup renders the form after that.
    private PurchaseOrderDetails Order => purchaseOrder!;

    private bool IsDirty => preview is not null && !string.Equals(checkedInput, CurrentInput(), StringComparison.Ordinal);

    private bool CanPost => preview is { CanPost: true } && !IsDirty && !IsBusy;

    private decimal ReceiveTotal => preview?.Lines.Sum(line => line.QuantityToReceive) ?? 0m;

    private int ReceivePartCount => preview?.Lines.Select(line => line.InventoryItemId).Distinct().Count() ?? 0;

    private IReadOnlyList<PurchaseOrderLineItem> OutstandingLines =>
        Order.Lines.Where(line => line.OutstandingQuantity > 0).ToList();

    private IReadOnlyList<ValidationMessage> ValidationMessages =>
    [
        .. lineErrors.Select(entry => new ValidationMessage(PartNumberForLine(entry.Key), entry.Value)),
        .. formErrors,
    ];

    protected override Task OnParametersSetAsync()
    {
        if (loadedPurchaseOrderId == PurchaseOrderId)
        {
            return Task.CompletedTask;
        }

        loadedPurchaseOrderId = PurchaseOrderId;
        receivedUtcLocal = TruncateToMinute(DateTime.UtcNow);
        deliveryReference = null;
        notes = null;
        editors.Clear();
        ClearCheck();
        failure = null;

        return LoadOrderAsync();
    }

    // Also used after a conflict: the fields keep what the operator typed while the quantities refresh.
    private async Task LoadOrderAsync()
    {
        isLoading = true;
        loadFailure = null;

        MaintenanceApiResult<PurchaseOrderDetails> result;

        try
        {
            result = await MaintenanceApi.GetPurchaseOrderDetailsAsync(PurchaseOrderId, disposeCancellation.Token);
        }
        catch (OperationCanceledException) when (disposeCancellation.IsCancellationRequested)
        {
            return;
        }

        isLoading = false;

        if (!result.IsSuccess)
        {
            loadFailure = result.Failure;
            purchaseOrder = null;

            return;
        }

        purchaseOrder = result.Value;
    }

    private ReceiptLineEditor EditorFor(Guid purchaseOrderLineId)
    {
        if (!editors.TryGetValue(purchaseOrderLineId, out var editor))
        {
            editor = new ReceiptLineEditor();
            editors[purchaseOrderLineId] = editor;
        }

        return editor;
    }

    private bool IsBlocked(Guid purchaseOrderLineId)
    {
        return preview?.Blockers.Any(blocker => blocker.PurchaseOrderLineId == purchaseOrderLineId) == true
            || lineErrors.ContainsKey(purchaseOrderLineId);
    }

    private async Task CheckAsync()
    {
        if (IsBusy)
        {
            return;
        }

        failure = null;
        lineErrors = new Dictionary<Guid, string>();
        formErrors = [];
        isConfirmOpen = false;

        if (ReceivedUtc() is not { } receivedUtc)
        {
            formErrors = [new ValidationMessage("Received at", "Enter when the delivery arrived.")];
            return;
        }

        if (receivedUtc > DateTime.UtcNow)
        {
            formErrors = [new ValidationMessage("Received at", "A delivery cannot be received in the future.")];
            return;
        }

        isChecking = true;

        var submittedLineIds = SubmittedLineIds();
        MaintenanceApiResult<PurchaseOrderReceiptPostingPreviewModel> result;

        try
        {
            result = ReceiveRemaining
                ? await MaintenanceApi.GetRemainingReceiptPreviewAsync(PurchaseOrderId, disposeCancellation.Token)
                : await MaintenanceApi.GetPurchaseOrderReceiptPreviewAsync(
                    PurchaseOrderId,
                    new PreviewPurchaseOrderReceiptRequest
                    {
                        ReceivedUtc = receivedUtc,
                        Lines = Entries(),
                    },
                    disposeCancellation.Token);
        }
        catch (OperationCanceledException) when (disposeCancellation.IsCancellationRequested)
        {
            return;
        }

        isChecking = false;

        if (!result.IsSuccess)
        {
            ClearCheck();
            await ApplyFailureAsync(result.Failure!, submittedLineIds);

            return;
        }

        preview = result.Value;
        checkedInput = CurrentInput();
        clientRequestId = Guid.NewGuid();
    }

    private void AskToPost()
    {
        if (!CanPost)
        {
            return;
        }

        failure = null;
        isConfirmOpen = true;
    }

    private void DismissConfirmation()
    {
        if (isPosting)
        {
            return;
        }

        isConfirmOpen = false;
    }

    private async Task PostAsync()
    {
        if (preview is not { } confirmed || !CanPost)
        {
            return;
        }

        isPosting = true;
        failure = null;
        lineErrors = new Dictionary<Guid, string>();
        formErrors = [];

        var submittedLineIds = SubmittedLineIds();
        string message;

        try
        {
            if (ReceiveRemaining)
            {
                var result = await MaintenanceApi.ReceivePurchaseOrderAsync(
                    PurchaseOrderId,
                    new ReceiveRemainingPurchaseOrderRequest
                    {
                        ClientRequestId = clientRequestId,
                        ExpectedConcurrencyToken = confirmed.ConcurrencyToken,
                        PreviewToken = confirmed.PreviewToken,
                        ReceivedUtc = ReceivedUtc(),
                        DeliveryReference = deliveryReference,
                        Notes = notes,
                    },
                    disposeCancellation.Token);

                if (!result.IsSuccess)
                {
                    await FinishFailedPostAsync(result.Failure!, submittedLineIds);

                    return;
                }

                message = PostedMessage(result.Value!.Receipt, result.Value.AlreadyPosted, result.Value.Status);
            }
            else
            {
                var result = await MaintenanceApi.PostPurchaseOrderReceiptAsync(
                    PurchaseOrderId,
                    new PostPurchaseOrderReceiptRequest
                    {
                        ClientRequestId = clientRequestId,
                        ExpectedConcurrencyToken = confirmed.ConcurrencyToken,
                        PreviewToken = confirmed.PreviewToken,
                        ReceivedUtc = ReceivedUtc(),
                        DeliveryReference = deliveryReference,
                        Notes = notes,
                        Lines = Entries(),
                    },
                    disposeCancellation.Token);

                if (!result.IsSuccess)
                {
                    await FinishFailedPostAsync(result.Failure!, submittedLineIds);

                    return;
                }

                message = PostedMessage(result.Value!.Receipt, result.Value.AlreadyPosted, result.Value.OrderStatus);
            }
        }
        catch (OperationCanceledException) when (disposeCancellation.IsCancellationRequested)
        {
            return;
        }

        isPosting = false;
        isConfirmOpen = false;

        await OnPosted.InvokeAsync(message);
    }

    private async Task FinishFailedPostAsync(MaintenanceApiFailure postFailure, IReadOnlyList<Guid> submittedLineIds)
    {
        isPosting = false;
        isConfirmOpen = false;

        // Without a response the receipt may or may not have been recorded; keeping the check lets the operator
        // send the same confirmation again, which the server replays instead of receiving twice.
        if (postFailure.StatusCode == 0)
        {
            failure = postFailure;

            return;
        }

        ClearCheck();
        await ApplyFailureAsync(postFailure, submittedLineIds);
    }

    private async Task ApplyFailureAsync(MaintenanceApiFailure applied, IReadOnlyList<Guid> submittedLineIds)
    {
        failure = applied;

        if (applied.IsValidation)
        {
            ApplyValidationErrors(applied, submittedLineIds);
        }

        if (applied.IsConflict || applied.IsNotFound)
        {
            await LoadOrderAsync();
        }
    }

    /// <summary>
    /// Turns the server's field keys, such as "lines[0].quantity", back into the order line they belong to,
    /// so the message shows next to the quantity that caused it.
    /// </summary>
    private void ApplyValidationErrors(MaintenanceApiFailure validation, IReadOnlyList<Guid> submittedLineIds)
    {
        var perLine = new Dictionary<Guid, string>();
        var remaining = new List<ValidationMessage>();

        foreach (var (key, messages) in validation.Errors)
        {
            var message = string.Join(" ", messages);
            var start = key.IndexOf('[', StringComparison.Ordinal);
            var end = key.IndexOf(']', StringComparison.Ordinal);

            if (key.StartsWith("lines[", StringComparison.Ordinal)
                && end > start
                && int.TryParse(key.AsSpan(start + 1, end - start - 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out var index)
                && index >= 0
                && index < submittedLineIds.Count)
            {
                perLine[submittedLineIds[index]] = perLine.TryGetValue(submittedLineIds[index], out var existing)
                    ? $"{existing} {message}"
                    : message;

                continue;
            }

            remaining.Add(new ValidationMessage(ProcurementFormat.FieldLabel(key), message));
        }

        lineErrors = perLine;
        formErrors = remaining;
    }

    private Task CloseAsync()
    {
        return OnClosed.InvokeAsync();
    }

    private void ClearCheck()
    {
        preview = null;
        checkedInput = null;
        isConfirmOpen = false;
    }

    // A blank quantity leaves the line out; anything typed is sent so the server can explain it. A line that
    // became fulfilled after a reload no longer shows its input, so it is left out too.
    private List<PurchaseOrderReceiptLineEntry> Entries()
    {
        return Order.Lines
            .Where(line => line.OutstandingQuantity > 0 && EditorFor(line.LineId).Quantity is not null)
            .Select(line => new PurchaseOrderReceiptLineEntry
            {
                PurchaseOrderLineId = line.LineId,
                Quantity = EditorFor(line.LineId).Quantity,
            })
            .ToList();
    }

    private List<Guid> SubmittedLineIds()
    {
        return ReceiveRemaining ? [] : Entries().Select(entry => entry.PurchaseOrderLineId!.Value).ToList();
    }

    private DateTime? ReceivedUtc()
    {
        return receivedUtcLocal is { } local ? DateTime.SpecifyKind(local, DateTimeKind.Utc) : null;
    }

    // Everything the confirmation sends; any change after a check needs a new check before posting.
    private string CurrentInput()
    {
        var lines = string.Join(
            ';',
            editors
                .Where(entry => entry.Value.Quantity is not null)
                .OrderBy(entry => entry.Key)
                .Select(entry => $"{entry.Key}={entry.Value.Quantity!.Value.ToString(CultureInfo.InvariantCulture)}"));

        return string.Join(
            '|',
            receivedUtcLocal?.ToString("O", CultureInfo.InvariantCulture),
            Normalize(deliveryReference),
            Normalize(notes),
            ReceiveRemaining ? "remaining" : lines);
    }

    private string PartNumberForLine(Guid purchaseOrderLineId)
    {
        return purchaseOrder?.Lines.FirstOrDefault(line => line.LineId == purchaseOrderLineId)?.PartNumber
            ?? MaintenanceFormat.MissingValue;
    }

    private static string PostedMessage(PurchaseOrderReceiptModel? receipt, bool alreadyPosted, string orderStatus)
    {
        if (alreadyPosted)
        {
            return "This delivery had already been posted. Stock was not changed again.";
        }

        var received = receipt is null ? string.Empty : $"{ProcurementFormat.Quantity(receipt.TotalQuantity)} unit(s) received. ";

        return $"Receipt posted. {received}The order is now {ProcurementFormat.StatusLabel(orderStatus).ToLowerInvariant()}.";
    }

    private static DateTime TruncateToMinute(DateTime value)
    {
        return new DateTime(value.Year, value.Month, value.Day, value.Hour, value.Minute, 0, DateTimeKind.Unspecified);
    }

    private static string? Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    public void Dispose()
    {
        disposeCancellation.Cancel();
        disposeCancellation.Dispose();
    }

    private sealed record ValidationMessage(string Label, string Message);

    private sealed class ReceiptLineEditor
    {
        public decimal? Quantity { get; set; }
    }
}
