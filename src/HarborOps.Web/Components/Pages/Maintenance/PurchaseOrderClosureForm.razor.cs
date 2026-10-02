using HarborOps.Web.Api.Maintenance;
using Microsoft.AspNetCore.Components;

namespace HarborOps.Web.Components.Pages.Maintenance;

public partial class PurchaseOrderClosureForm : IDisposable
{
    private readonly CancellationTokenSource disposeCancellation = new();

    [Parameter, EditorRequired]
    public Guid PurchaseOrderId { get; set; }

    [Parameter]
    public EventCallback<string> OnOrderClosed { get; set; }

    [Parameter]
    public EventCallback OnDismissed { get; set; }

    private Guid? loadedPurchaseOrderId;
    private PurchaseOrderClosurePreviewModel? preview;
    private MaintenanceApiFailure? loadFailure;
    private bool isLoading = true;
    private string? reason;

    // One id per confirmed preview: resending the same confirmation after a lost response is replayed by the
    // server instead of failing because the order is already closed.
    private Guid clientRequestId;

    private MaintenanceApiFailure? failure;
    private IReadOnlyList<ValidationMessage> formErrors = [];

    private bool isClosing;
    private bool isConfirmOpen;

    private bool IsBusy => isLoading || isClosing;

    // Only read once the preview has loaded; the markup renders the form after that.
    private PurchaseOrderClosurePreviewModel Preview => preview!;

    private int ReasonLength => reason?.Trim().Length ?? 0;

    private bool CanClose => preview is { CanClose: true } && !IsBusy;

    protected override Task OnParametersSetAsync()
    {
        if (loadedPurchaseOrderId == PurchaseOrderId)
        {
            return Task.CompletedTask;
        }

        loadedPurchaseOrderId = PurchaseOrderId;
        reason = null;
        failure = null;
        formErrors = [];
        preview = null;

        return LoadPreviewAsync();
    }

    private Task RefreshAsync()
    {
        failure = null;
        formErrors = [];

        return LoadPreviewAsync();
    }

    // Also used to refresh after a conflict without a preview: the reason keeps what the operator typed.
    private async Task LoadPreviewAsync()
    {
        isLoading = true;
        loadFailure = null;
        isConfirmOpen = false;

        MaintenanceApiResult<PurchaseOrderClosurePreviewModel> result;

        try
        {
            result = await MaintenanceApi.GetPurchaseOrderClosurePreviewAsync(PurchaseOrderId, disposeCancellation.Token);
        }
        catch (OperationCanceledException) when (disposeCancellation.IsCancellationRequested)
        {
            return;
        }

        isLoading = false;

        if (!result.IsSuccess)
        {
            loadFailure = result.Failure;
            preview = null;

            return;
        }

        UsePreview(result.Value!);
    }

    private void UsePreview(PurchaseOrderClosurePreviewModel checkedPreview)
    {
        preview = checkedPreview;
        clientRequestId = Guid.NewGuid();
    }

    private void AskToClose()
    {
        if (!CanClose)
        {
            return;
        }

        failure = null;
        formErrors = ValidateReason();

        if (formErrors.Count > 0)
        {
            return;
        }

        isConfirmOpen = true;
    }

    private void DismissConfirmation()
    {
        if (isClosing)
        {
            return;
        }

        isConfirmOpen = false;
    }

    private async Task CloseOrderAsync()
    {
        if (preview is not { } confirmed || !CanClose)
        {
            return;
        }

        isClosing = true;
        failure = null;
        formErrors = [];

        ClosePurchaseOrderApiResult result;

        try
        {
            result = await MaintenanceApi.ClosePurchaseOrderAsync(
                PurchaseOrderId,
                new ClosePurchaseOrderRequest
                {
                    ClientRequestId = clientRequestId,
                    ExpectedConcurrencyToken = confirmed.ConcurrencyToken,
                    PreviewToken = confirmed.PreviewToken,
                    Reason = reason?.Trim(),
                },
                disposeCancellation.Token);
        }
        catch (OperationCanceledException) when (disposeCancellation.IsCancellationRequested)
        {
            return;
        }

        isClosing = false;
        isConfirmOpen = false;

        if (!result.IsSuccess)
        {
            await ApplyFailureAsync(result);

            return;
        }

        await OnOrderClosed.InvokeAsync(ClosedMessage(result.Value!));
    }

    private async Task ApplyFailureAsync(ClosePurchaseOrderApiResult result)
    {
        var applied = result.Failure!;
        failure = applied;

        // Without a response the order may or may not have been closed; keeping the preview and its request id
        // lets the operator send the same confirmation again, which the server replays.
        if (applied.StatusCode == 0)
        {
            return;
        }

        if (applied.IsValidation)
        {
            formErrors = applied.Errors
                .Select(entry => new ValidationMessage(ProcurementFormat.FieldLabel(entry.Key), string.Join(" ", entry.Value)))
                .ToList();

            return;
        }

        if (result.RefreshedPreview is { } refreshed)
        {
            UsePreview(refreshed);

            return;
        }

        if (applied.IsConflict || applied.IsNotFound)
        {
            await LoadPreviewAsync();
        }
    }

    private IReadOnlyList<ValidationMessage> ValidateReason()
    {
        if (ReasonLength == 0)
        {
            return [new ValidationMessage("Reason", "Explain why the remaining goods will not arrive.")];
        }

        if (ReasonLength < ProcurementFormat.ClosureReasonMinLength)
        {
            return
            [
                new ValidationMessage(
                    "Reason",
                    $"Use at least {ProcurementFormat.ClosureReasonMinLength} characters so the closure can be understood later."),
            ];
        }

        if (ReasonLength > ProcurementFormat.ClosureReasonMaxLength)
        {
            return
            [
                new ValidationMessage(
                    "Reason",
                    $"Use at most {ProcurementFormat.ClosureReasonMaxLength} characters."),
            ];
        }

        return [];
    }

    private Task DismissAsync()
    {
        return OnDismissed.InvokeAsync();
    }

    private static string ClosedMessage(ClosePurchaseOrderResult closed)
    {
        if (closed.AlreadyClosed)
        {
            return "This order had already been closed by the same request. Nothing changed again.";
        }

        return $"Order closed short. {ProcurementFormat.Quantity(closed.Closure.TotalQuantityReceived)} unit(s) received, "
            + $"{ProcurementFormat.Quantity(closed.Closure.TotalQuantityClosed)} unit(s) closed without delivery.";
    }

    public void Dispose()
    {
        disposeCancellation.Cancel();
        disposeCancellation.Dispose();
    }

    private sealed record ValidationMessage(string Label, string Message);
}
