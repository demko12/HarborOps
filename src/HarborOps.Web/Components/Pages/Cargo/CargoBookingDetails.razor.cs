using System.ComponentModel.DataAnnotations;
using HarborOps.Web.Api.Cargo;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.JSInterop;

using CargoBookingDetailsModel = HarborOps.Web.Api.Cargo.CargoBookingDetails;

namespace HarborOps.Web.Components.Pages.Cargo;

public partial class CargoBookingDetails : IDisposable
{
    private readonly CancellationTokenSource disposeCancellation = new();
    private CargoBookingDetailsModel? details;
    private CargoApiFailure? failure;
    private bool isLoading = true;
    private Guid loadedBookingId;

    [Parameter]
    public Guid BookingId { get; set; }

    [SupplyParameterFromQuery(Name = "from")]
    public string? BoardQuery { get; set; }

    private string CargoHref => string.IsNullOrWhiteSpace(BoardQuery) ? "/cargo" : $"/cargo?{BoardQuery}";

    private string PageHeading => details?.BookingNumber ?? "Cargo booking";

    private bool IsBlockedByUnapprovedSafetyInfo =>
        details is { IsHazardous: true, NextStatus: "Loaded" }
        && details.SafetyInfo?.ReviewStatus != CargoBookingFormat.ApprovedStatus;

    private bool voyageIdCopied;

    private async Task CopyVoyageIdAsync()
    {
        if (details is null)
        {
            return;
        }

        await JS.InvokeVoidAsync("navigator.clipboard.writeText", details.VoyageId.ToString());

        voyageIdCopied = true;
        StateHasChanged();

        try
        {
            await Task.Delay(1500, disposeCancellation.Token);
        }
        catch (OperationCanceledException) when (disposeCancellation.IsCancellationRequested)
        {
            return;
        }

        voyageIdCopied = false;
        StateHasChanged();
    }

    protected override void OnInitialized()
    {
        ResetTrackingForm();
        ResetContainerForm();
        ResetOperationForm();
    }

    protected override Task OnParametersSetAsync()
    {
        if (loadedBookingId == BookingId && !isLoading)
        {
            return Task.CompletedTask;
        }

        loadedBookingId = BookingId;

        return LoadAsync();
    }

    private Task RetryAsync()
    {
        return LoadAsync();
    }

    private async Task LoadAsync()
    {
        var requestedBookingId = BookingId;
        isLoading = true;
        failure = null;

        CargoApiResult<CargoBookingDetailsModel> result;
        try
        {
            result = await CargoApi.GetDetailsAsync(requestedBookingId, disposeCancellation.Token);
        }
        catch (OperationCanceledException) when (disposeCancellation.IsCancellationRequested)
        {
            return;
        }

        if (disposeCancellation.IsCancellationRequested || requestedBookingId != BookingId)
        {
            return;
        }

        isLoading = false;

        if (!result.IsSuccess)
        {
            failure = result.Failure;
            details = null;

            return;
        }

        details = result.Value;

        await LoadExecutionAsync();
    }

    private bool isRecordingTrackingEvent;
    private string? trackingError;
    private TrackingFormModel trackingModel = new();
    private EditContext trackingEditContext = null!;
    private ValidationMessageStore trackingMessages = null!;

    private async Task SubmitTrackingEventAsync()
    {
        if (isRecordingTrackingEvent || details?.NextStatus is not { } nextStatus)
        {
            return;
        }

        trackingMessages.Clear();
        trackingError = null;

        if (!trackingEditContext.Validate())
        {
            return;
        }

        isRecordingTrackingEvent = true;

        try
        {
            var request = new RecordCargoTrackingEventWebRequest(
                nextStatus,
                DateTime.SpecifyKind(trackingModel.OccurredUtc!.Value, DateTimeKind.Utc),
                trackingModel.Location!.Trim());

            var result = await CargoApi.RecordTrackingEventAsync(
                BookingId,
                request,
                disposeCancellation.Token);

            if (!result.IsSuccess)
            {
                CargoFormFailure.Apply(result.Failure!, trackingEditContext, trackingMessages, TrackingFieldMap, message => trackingError = message);

                if (result.Failure!.IsConflict)
                {
                    await LoadAsync();
                }

                return;
            }

            ResetTrackingForm();

            await LoadAsync();
        }
        catch (OperationCanceledException) when (disposeCancellation.IsCancellationRequested)
        {
        }
        finally
        {
            isRecordingTrackingEvent = false;
        }
    }

    private void ResetTrackingForm()
    {
        trackingModel = new TrackingFormModel();
        trackingEditContext = new EditContext(trackingModel);
        trackingMessages = new ValidationMessageStore(trackingEditContext);
    }

    private static readonly IReadOnlyDictionary<string, string> TrackingFieldMap =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["occurredUtc"] = nameof(TrackingFormModel.OccurredUtc),
            ["location"] = nameof(TrackingFormModel.Location),
        };

    private sealed class TrackingFormModel
    {
        [Required(ErrorMessage = "Occurred UTC is required.")]
        public DateTime? OccurredUtc { get; set; } = DateTime.UtcNow;

        [Required(ErrorMessage = "Location is required.")]
        [StringLength(200, ErrorMessage = "Location must not exceed 200 characters.")]
        public string? Location { get; set; }
    }

    private const string StartAction = "start";
    private const string CompleteAction = "complete";
    private const string LoadedStatus = "Loaded";

    private IReadOnlyList<CargoContainerRow> containers = [];
    private CargoApiFailure? containersFailure;
    private bool isLoadingContainers;

    private IReadOnlyList<CargoOperationRow> operations = [];
    private CargoApiFailure? operationsFailure;
    private bool isLoadingOperations;

    private int LoadedContainerCount => containers.Count(container =>
        string.Equals(container.Status, LoadedStatus, StringComparison.OrdinalIgnoreCase));

    private bool CanManageCargo => AuthorizationState.CanManageCargo;

    private bool IsBookingClosed => details?.Status is "Completed";

    private bool CanAddContainers => CanManageCargo && !IsBookingClosed;

    private int LoadedPercent => containers.Count == 0
        ? 0
        : (int)Math.Round(LoadedContainerCount * 100d / containers.Count);

    private async Task LoadExecutionAsync()
    {
        await Task.WhenAll(LoadContainersAsync(), LoadOperationsAsync());
    }

    private async Task LoadContainersAsync()
    {
        var requestedBookingId = BookingId;
        isLoadingContainers = true;
        containersFailure = null;

        CargoApiResult<CargoContainerList> result;
        try
        {
            result = await CargoApi.GetContainersAsync(requestedBookingId, disposeCancellation.Token);
        }
        catch (OperationCanceledException) when (disposeCancellation.IsCancellationRequested)
        {
            return;
        }

        if (disposeCancellation.IsCancellationRequested || requestedBookingId != BookingId)
        {
            return;
        }

        isLoadingContainers = false;

        if (!result.IsSuccess)
        {
            containersFailure = result.Failure;
            containers = [];

            return;
        }

        containers = result.Value?.Items ?? [];
    }

    private async Task LoadOperationsAsync()
    {
        var requestedBookingId = BookingId;
        isLoadingOperations = true;
        operationsFailure = null;

        CargoApiResult<CargoOperationList> result;
        try
        {
            result = await CargoApi.GetOperationsAsync(requestedBookingId, disposeCancellation.Token);
        }
        catch (OperationCanceledException) when (disposeCancellation.IsCancellationRequested)
        {
            return;
        }

        if (disposeCancellation.IsCancellationRequested || requestedBookingId != BookingId)
        {
            return;
        }

        isLoadingOperations = false;

        if (!result.IsSuccess)
        {
            operationsFailure = result.Failure;
            operations = [];

            return;
        }

        operations = result.Value?.Items ?? [];
    }

    private InputText? containerNumberInput;
    private bool isAddingContainer;
    private string? containerError;
    private ContainerFormModel containerModel = new();
    private EditContext containerEditContext = null!;
    private ValidationMessageStore containerMessages = null!;

    private async Task SubmitContainerAsync()
    {
        if (isAddingContainer)
        {
            return;
        }

        containerMessages.Clear();
        containerError = null;

        if (!containerEditContext.Validate())
        {
            return;
        }

        isAddingContainer = true;

        try
        {
            var request = new AddCargoContainerWebRequest(
                containerModel.ContainerNumber!.Trim(),
                containerModel.ContainerType!.Trim());

            var result = await CargoApi.AddContainerAsync(
                BookingId,
                request,
                disposeCancellation.Token);

            if (!result.IsSuccess)
            {
                CargoFormFailure.Apply(
                    result.Failure!,
                    containerEditContext,
                    containerMessages,
                    ContainerFieldMap,
                    message => containerError = message);

                return;
            }

            ResetContainerForm();

            await LoadContainersAsync();

            if (containerNumberInput?.Element is { } element)
            {
                await element.FocusAsync();
            }
        }
        catch (OperationCanceledException) when (disposeCancellation.IsCancellationRequested)
        {
        }
        finally
        {
            isAddingContainer = false;
        }
    }

    private void ResetContainerForm()
    {
        containerModel = new ContainerFormModel();
        containerEditContext = new EditContext(containerModel);
        containerMessages = new ValidationMessageStore(containerEditContext);
    }

    private static readonly IReadOnlyDictionary<string, string> ContainerFieldMap =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["containerNumber"] = nameof(ContainerFormModel.ContainerNumber),
            ["containerType"] = nameof(ContainerFormModel.ContainerType),
        };

    private sealed class ContainerFormModel
    {
        [Required(ErrorMessage = "Container number is required.")]
        [StringLength(20, ErrorMessage = "Container number must not exceed 20 characters.")]
        public string? ContainerNumber { get; set; }

        [Required(ErrorMessage = "Container type is required.")]
        [StringLength(50, ErrorMessage = "Container type must not exceed 50 characters.")]
        public string? ContainerType { get; set; }
    }

    private bool isPlanFormOpen;
    private bool isPlanningOperation;
    private string? operationError;
    private OperationFormModel operationModel = new();
    private EditContext operationEditContext = null!;
    private ValidationMessageStore operationMessages = null!;

    private async Task SubmitOperationAsync()
    {
        if (isPlanningOperation)
        {
            return;
        }

        operationMessages.Clear();
        operationError = null;

        if (!operationEditContext.Validate())
        {
            return;
        }

        isPlanningOperation = true;

        try
        {
            var request = new PlanCargoOperationWebRequest(
                operationModel.OperationType!,
                DateTime.SpecifyKind(operationModel.PlannedAtUtc!.Value, DateTimeKind.Utc));

            var result = await CargoApi.PlanOperationAsync(
                BookingId,
                request,
                disposeCancellation.Token);

            if (!result.IsSuccess)
            {
                CargoFormFailure.Apply(
                    result.Failure!,
                    operationEditContext,
                    operationMessages,
                    OperationFieldMap,
                    message => operationError = message);

                return;
            }

            ResetOperationForm();
            isPlanFormOpen = false;

            await LoadOperationsAsync();
        }
        catch (OperationCanceledException) when (disposeCancellation.IsCancellationRequested)
        {
        }
        finally
        {
            isPlanningOperation = false;
        }
    }

    private void TogglePlanForm()
    {
        isPlanFormOpen = !isPlanFormOpen;
        operationError = null;

        if (!isPlanFormOpen)
        {
            ResetOperationForm();
        }
    }

    private void ResetOperationForm()
    {
        operationModel = new OperationFormModel();
        operationEditContext = new EditContext(operationModel);
        operationMessages = new ValidationMessageStore(operationEditContext);
    }

    private static readonly IReadOnlyDictionary<string, string> OperationFieldMap =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["operationType"] = nameof(OperationFormModel.OperationType),
            ["plannedAtUtc"] = nameof(OperationFormModel.PlannedAtUtc),
        };

    private sealed class OperationFormModel
    {
        [Required(ErrorMessage = "Operation type is required.")]
        public string? OperationType { get; set; } = "Load";

        [Required(ErrorMessage = "Planned at UTC is required.")]
        public DateTime? PlannedAtUtc { get; set; } = DateTime.UtcNow;
    }

    private CargoOperationRow? pendingOperation;
    private string? pendingAction;
    private bool isTransitioning;
    private string? transitionError;

    private static string ConfirmationQuestion(CargoOperationRow operation, string action)
    {
        return action == StartAction
            ? $"Start the {operation.OperationType.ToLowerInvariant()} operation planned for {CargoBookingFormat.CreatedUtc(operation.PlannedAtUtc)} UTC?"
            : $"Complete the {operation.OperationType.ToLowerInvariant()} operation and set every container on this booking to {(operation.OperationType == "Load" ? "Loaded" : "Unloaded")}?";
    }

    private void AskToConfirm(CargoOperationRow operation, string action)
    {
        pendingOperation = operation;
        pendingAction = action;
        transitionError = null;
    }

    private void DismissConfirmation()
    {
        if (isTransitioning)
        {
            return;
        }

        CloseConfirmation();
    }

    private void CloseConfirmation()
    {
        pendingOperation = null;
        pendingAction = null;
    }

    private async Task ConfirmPendingActionAsync()
    {
        if (isTransitioning || pendingOperation is not { } operation || pendingAction is not { } action)
        {
            return;
        }

        isTransitioning = true;
        transitionError = null;

        try
        {
            var result = action == StartAction
                ? await CargoApi.StartOperationAsync(operation.OperationId, disposeCancellation.Token)
                : await CargoApi.CompleteOperationAsync(operation.OperationId, disposeCancellation.Token);

            if (!result.IsSuccess)
            {
                var failure = result.Failure!;
                transitionError = $"{failure.Title} {failure.Message}".Trim();

                if (failure.IsConflict || failure.IsNotFound)
                {
                    await LoadExecutionAsync();
                }

                return;
            }

            CloseConfirmation();

            await LoadExecutionAsync();
        }
        catch (OperationCanceledException) when (disposeCancellation.IsCancellationRequested)
        {
        }
        finally
        {
            isTransitioning = false;
        }
    }

    public void Dispose()
    {
        disposeCancellation.Cancel();
        disposeCancellation.Dispose();
    }
}
