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
    private int containerPageNumber = 1;
    private int containerTotalCount;
    private int loadedContainerCount;
    private const int ContainersPerPage = 15;
    private CargoApiFailure? containersFailure;
    private bool isLoadingContainers;
    private Guid? selectedControlContainerId;

    private IReadOnlyList<CargoOperationRow> operations = [];
    private CargoApiFailure? operationsFailure;
    private bool isLoadingOperations;

    private int LoadedContainerCount => loadedContainerCount;

    private int PlannedOperationCount => operations.Count(operation =>
        string.Equals(operation.OperationState, "Planned", StringComparison.OrdinalIgnoreCase));

    private bool CanManageCargo => AuthorizationState.CanManageCargo;

    private bool IsBookingClosed => details?.Status is "Completed";

    private bool CanAddContainers => CanManageCargo && !IsBookingClosed;

    private int LoadedPercent => containerTotalCount == 0
        ? 0
        : (int)Math.Round(LoadedContainerCount * 100d / containerTotalCount);

    private IReadOnlyList<ContainerControlRow> ContainerControls =>
        details?.ContainerControls ?? [];

    private CargoContainerRow? SelectedControlContainer =>
        selectedControlContainerId is { } containerId
            ? containers.FirstOrDefault(container => container.ContainerId == containerId)
            : null;

    private string? ControlStateFor(Guid containerId)
    {
        return ContainerControls
            .FirstOrDefault(control => control.ContainerId == containerId)?
            .State;
    }

    private void ToggleControl(Guid containerId)
    {
        selectedControlContainerId = selectedControlContainerId == containerId
            ? null
            : containerId;
    }

    private async Task RefreshAfterControlChangeAsync()
    {
        var requestedBookingId = BookingId;

        CargoApiResult<CargoBookingDetailsModel> result;
        try
        {
            result = await CargoApi.GetDetailsAsync(requestedBookingId, disposeCancellation.Token);
        }
        catch (OperationCanceledException) when (disposeCancellation.IsCancellationRequested)
        {
            return;
        }

        if (disposeCancellation.IsCancellationRequested
            || requestedBookingId != BookingId
            || !result.IsSuccess)
        {
            return;
        }

        details = result.Value;
    }

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
            result = await CargoApi.GetContainersAsync(requestedBookingId, containerPageNumber, disposeCancellation.Token);
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
        var isLegacyResponse = result.Value?.TotalCount is 0 && containers.Count > 0;
        containerTotalCount = isLegacyResponse ? containers.Count : result.Value?.TotalCount ?? 0;
        loadedContainerCount = isLegacyResponse
            ? containers.Count(container => string.Equals(container.Status, LoadedStatus, StringComparison.OrdinalIgnoreCase))
            : result.Value?.LoadedCount ?? 0;
        containerPageNumber = result.Value?.PageNumber ?? 1;
    }

    private int ContainerPageCount => Math.Max(1, (int)Math.Ceiling(containerTotalCount / (double)ContainersPerPage));

    private async Task ChangeContainerPageAsync(int pageNumber)
    {
        if (pageNumber < 1 || pageNumber > ContainerPageCount || pageNumber == containerPageNumber) return;
        containerPageNumber = pageNumber;
        selectedControlContainerId = null;
        CancelContainerEdit();
        await LoadContainersAsync();
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
    private Guid? editingContainerId;
    private bool isUpdatingContainer;
    private string? editContainerError;
    private ContainerFormModel editContainerModel = new();
    private EditContext editContainerEditContext = null!;
    private ValidationMessageStore editContainerMessages = null!;
    private ContainerFormModel containerModel = new();
    private EditContext containerEditContext = null!;
    private ValidationMessageStore containerMessages = null!;
    private IBrowserFile? manifestFile;
    private ContainerManifestPreview? manifestPreview;
    private CargoApiFailure? manifestImportFailure;
    private bool isPreviewingManifest;
    private bool isImportingManifest;

    private void SelectManifestFile(InputFileChangeEventArgs args)
    {
        manifestFile = args.File;
        manifestPreview = null;
        manifestImportFailure = null;
    }

    private async Task PreviewManifestAsync()
    {
        if (manifestFile is null || isPreviewingManifest) return;
        isPreviewingManifest = true;
        manifestImportFailure = null;
        try
        {
            await using var stream = manifestFile.OpenReadStream(5 * 1024 * 1024, disposeCancellation.Token);
            var result = await CargoApi.PreviewContainerManifestAsync(BookingId, stream, manifestFile.Name, disposeCancellation.Token);
            if (result.IsSuccess) manifestPreview = result.Value;
            else manifestImportFailure = result.Failure;
        }
        catch (OperationCanceledException) when (disposeCancellation.IsCancellationRequested) { }
        finally { isPreviewingManifest = false; }
    }

    private async Task ImportManifestAsync()
    {
        if (manifestFile is null || manifestPreview is null || manifestPreview.InvalidCount > 0 || isImportingManifest) return;
        isImportingManifest = true;
        manifestImportFailure = null;
        try
        {
            await using var stream = manifestFile.OpenReadStream(5 * 1024 * 1024, disposeCancellation.Token);
            var result = await CargoApi.ImportContainerManifestAsync(BookingId, stream, manifestFile.Name, disposeCancellation.Token);
            if (!result.IsSuccess) { manifestImportFailure = result.Failure; return; }
            manifestFile = null;
            manifestPreview = null;
            await LoadContainersAsync();
        }
        catch (OperationCanceledException) when (disposeCancellation.IsCancellationRequested) { }
        finally { isImportingManifest = false; }
    }

    private static readonly IReadOnlyList<ContainerTypeOption> StandardContainerTypes =
    [
        new("22G1", "20 ft dry general purpose (22G1)"),
        new("42G1", "40 ft dry general purpose (42G1)"),
        new("45G1", "40 ft high-cube dry (45G1)"),
        new("22R1", "20 ft refrigerated (22R1)"),
        new("42R1", "40 ft refrigerated (42R1)"),
        new("22U1", "20 ft open top (22U1)"),
        new("42U1", "40 ft open top (42U1)"),
        new("22P1", "20 ft flat rack (22P1)"),
        new("42P1", "40 ft flat rack (42P1)"),
        new("22T1", "20 ft tank (22T1)"),
    ];

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
                containerModel.ContainerType!.Trim(),
                containerModel.PortOfLoadingCode!.Trim(),
                containerModel.PortOfDischargeCode!.Trim(),
                containerModel.WeightKg!.Value);

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

    private bool CanEdit(CargoContainerRow container) => CanAddContainers && string.Equals(container.Status, "Registered", StringComparison.OrdinalIgnoreCase);

    private void BeginContainerEdit(CargoContainerRow container)
    {
        editingContainerId = container.ContainerId;
        editContainerError = null;
        editContainerModel = new ContainerFormModel
        {
            ContainerNumber = container.ContainerNumber,
            ContainerType = container.ContainerType,
            PortOfLoadingCode = container.PortOfLoadingCode,
            PortOfDischargeCode = container.PortOfDischargeCode,
            WeightKg = container.WeightKg,
        };
        editContainerEditContext = new EditContext(editContainerModel);
        editContainerMessages = new ValidationMessageStore(editContainerEditContext);
    }

    private void CancelContainerEdit()
    {
        editingContainerId = null;
        editContainerError = null;
    }

    private async Task SubmitContainerUpdateAsync()
    {
        if (editingContainerId is not { } containerId || isUpdatingContainer) return;
        editContainerMessages.Clear();
        editContainerError = null;
        if (!editContainerEditContext.Validate()) return;
        isUpdatingContainer = true;
        try
        {
            var request = new UpdateCargoContainerWebRequest(editContainerModel.ContainerNumber!.Trim(), editContainerModel.ContainerType!.Trim(), editContainerModel.PortOfLoadingCode!.Trim(), editContainerModel.PortOfDischargeCode!.Trim(), editContainerModel.WeightKg!.Value);
            var result = await CargoApi.UpdateContainerAsync(BookingId, containerId, request, disposeCancellation.Token);
            if (!result.IsSuccess)
            {
                CargoFormFailure.Apply(result.Failure!, editContainerEditContext, editContainerMessages, ContainerFieldMap, message => editContainerError = message);
                if (result.Failure!.IsConflict) await LoadContainersAsync();
                return;
            }

            CancelContainerEdit();
            await LoadContainersAsync();
        }
        catch (OperationCanceledException) when (disposeCancellation.IsCancellationRequested) { }
        finally { isUpdatingContainer = false; }
    }

    private void ResetContainerForm()
    {
        containerModel = new ContainerFormModel();
        containerEditContext = new EditContext(containerModel);
        containerMessages = new ValidationMessageStore(containerEditContext);
        editContainerEditContext = new EditContext(editContainerModel);
        editContainerMessages = new ValidationMessageStore(editContainerEditContext);
    }

    private static readonly IReadOnlyDictionary<string, string> ContainerFieldMap =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["containerNumber"] = nameof(ContainerFormModel.ContainerNumber),
            ["containerType"] = nameof(ContainerFormModel.ContainerType),
            ["portOfLoadingCode"] = nameof(ContainerFormModel.PortOfLoadingCode),
            ["portOfDischargeCode"] = nameof(ContainerFormModel.PortOfDischargeCode),
            ["weightKg"] = nameof(ContainerFormModel.WeightKg),
        };

    private sealed class ContainerFormModel
    {
        [Required(ErrorMessage = "Container number is required.")]
        [StringLength(20, ErrorMessage = "Container number must not exceed 20 characters.")]
        public string? ContainerNumber { get; set; }

        [Required(ErrorMessage = "Container type is required.")]
        [StringLength(50, ErrorMessage = "Container type must not exceed 50 characters.")]
        public string? ContainerType { get; set; }

        [Required(ErrorMessage = "POL is required.")]
        [StringLength(5, MinimumLength = 5, ErrorMessage = "POL must contain five characters.")]
        public string? PortOfLoadingCode { get; set; }

        [Required(ErrorMessage = "POD is required.")]
        [StringLength(5, MinimumLength = 5, ErrorMessage = "POD must contain five characters.")]
        public string? PortOfDischargeCode { get; set; }

        [Required(ErrorMessage = "Weight is required.")]
        [Range(typeof(decimal), "0.001", "999999999999999.999", ErrorMessage = "Weight must be greater than zero.")]
        public decimal? WeightKg { get; set; }
    }

    private sealed record ContainerTypeOption(string Code, string Label);

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
