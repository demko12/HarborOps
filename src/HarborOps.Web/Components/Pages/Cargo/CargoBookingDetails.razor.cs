using System.ComponentModel.DataAnnotations;
using System.Globalization;
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

    [SupplyParameterFromQuery(Name = BoardQueryKey)]
    public string? BoardQuery { get; set; }

    [SupplyParameterFromQuery(Name = ManifestSearchQueryKey)]
    public string? ManifestSearchFromQuery { get; set; }

    [SupplyParameterFromQuery(Name = ManifestStatusQueryKey)]
    public string? ManifestStatusFromQuery { get; set; }

    [SupplyParameterFromQuery(Name = ManifestTypeQueryKey)]
    public string? ManifestTypeFromQuery { get; set; }

    [SupplyParameterFromQuery(Name = ManifestPolQueryKey)]
    public string? ManifestPolFromQuery { get; set; }

    [SupplyParameterFromQuery(Name = ManifestPodQueryKey)]
    public string? ManifestPodFromQuery { get; set; }

    [SupplyParameterFromQuery(Name = ManifestPageQueryKey)]
    public int? ManifestPageFromQuery { get; set; }

    [SupplyParameterFromQuery(Name = TabQueryKey)]
    public string? TabFromQuery { get; set; }

    internal const string OverviewTab = "overview";
    internal const string ManifestTab = "manifest";
    internal const string SafetyTab = "safety";
    internal const string BillsTab = "bills";
    internal const string DiscrepanciesTab = "discrepancies";
    internal const string OperationsTab = "operations";
    internal const string TrackingTab = "tracking";
    private const string TabQueryKey = "tab";

    private static readonly IReadOnlyList<string> TabOrder =
        [OverviewTab, ManifestTab, SafetyTab, BillsTab, DiscrepanciesTab, OperationsTab, TrackingTab];

    private string activeTab = OverviewTab;
    private int? billCount;
    private int openDiscrepancyCount;

    private sealed record BookingTab(string Id, string Label, string Icon, string? Badge, string Tone);

    private IEnumerable<BookingTab> BookingTabs
    {
        get
        {
            yield return new BookingTab(OverviewTab, "Overview", "bi-box-seam", null, "neutral");
            yield return new BookingTab(
                ManifestTab,
                "Manifest",
                "bi-stack",
                hasLoadedContainers ? containerTotalCount.ToString(CultureInfo.InvariantCulture) : null,
                "neutral");
            if (details is { IsHazardous: true })
            {
                yield return new BookingTab(
                    SafetyTab,
                    "Safety",
                    "bi-shield-check",
                    CargoBookingFormat.SafetyStatusLabel(details.SafetyInfo?.ReviewStatus),
                    details.SafetyInfo?.ReviewStatus == CargoBookingFormat.ApprovedStatus ? "success" : "warning");
            }

            yield return new BookingTab(
                BillsTab,
                "Bills of lading",
                "bi-file-earmark-text",
                billCount is > 0 ? billCount.Value.ToString(CultureInfo.InvariantCulture) : null,
                "neutral");
            yield return new BookingTab(
                DiscrepanciesTab,
                "Discrepancies",
                "bi-exclamation-diamond",
                openDiscrepancyCount > 0 ? $"{openDiscrepancyCount.ToString(CultureInfo.InvariantCulture)} open" : null,
                "danger");
            yield return new BookingTab(
                OperationsTab,
                "Operations",
                "bi-arrow-down-up",
                OperationsBadge,
                operations.Any(IsOperationInProgress) ? "warning" : "neutral");
            yield return new BookingTab(TrackingTab, "Tracking", "bi-clock-history", TrackingBadge, TrackingTone);
        }
    }

    private string? OperationsBadge => operations.Any(IsOperationInProgress)
        ? "In progress"
        : operations.Count > 0 ? operations.Count.ToString(CultureInfo.InvariantCulture) : null;

    private static bool IsOperationInProgress(CargoOperationRow operation) =>
        operation.StartedAtUtc is not null && operation.FinishedAtUtc is null;

    private string? TrackingBadge => details switch
    {
        null => null,
        { NextStatus: null } => "Complete",
        _ when IsCompletionBlocked || IsBlockedByUnapprovedSafetyInfo => "Blocked",
        _ => null,
    };

    private string TrackingTone => details?.NextStatus is null ? "success" : "warning";

    private static string TabButtonId(string tab) => $"booking-tab-{tab}";

    private static string TabPanelId(string tab) => $"booking-panel-{tab}";

    private static string NormalizeTab(string? tab)
    {
        return TabOrder.FirstOrDefault(candidate => string.Equals(candidate, tab, StringComparison.OrdinalIgnoreCase))
            ?? OverviewTab;
    }

    private void SelectTab(string tab)
    {
        if (activeTab == tab)
        {
            return;
        }

        activeTab = tab;
        SyncManifestAddress();
    }

    internal const string BoardQueryKey = "from";
    private const string ManifestSearchQueryKey = "manifestSearch";
    private const string ManifestStatusQueryKey = "manifestStatus";
    private const string ManifestTypeQueryKey = "manifestType";
    private const string ManifestPolQueryKey = "manifestPol";
    private const string ManifestPodQueryKey = "manifestPod";
    private const string ManifestPageQueryKey = "manifestPage";

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
        hasLoadedContainers = false;
        activeTab = NormalizeTab(TabFromQuery);
        ApplyAddressToContainerFilters();

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

        if (activeTab == SafetyTab && details is { IsHazardous: false })
        {
            activeTab = OverviewTab;
        }

        await Task.WhenAll(LoadExecutionAsync(), LoadCompletionBlockersAsync());
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

                if (result.Failure!.CompletionBlockers is { } blockers)
                {
                    completionBlockers = blockers;

                    return;
                }

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

    private const string CompletedStatus = "Completed";

    private CargoDiscrepanciesPanel? discrepanciesPanel;
    private CargoCompletionBlockersView? completionBlockers;
    private CargoContainerRow? navigatedControlContainer;

    private static string BlockerLabel(CargoCompletionBlockerRow blocker)
    {
        var scope = blocker.ContainerNumber is { } number ? $"on {number}" : "on the whole booking";

        return $"{CargoDiscrepanciesPanel.TypeLabel(blocker.Type)} {scope}";
    }

    private bool IsCompletionBlocked =>
        details?.NextStatus == CompletedStatus && completionBlockers is { OpenCount: > 0 };

    private Task HandleDiscrepanciesChanged()
    {
        trackingError = null;

        return LoadCompletionBlockersAsync();
    }

    private async Task LoadCompletionBlockersAsync()
    {
        var requestedBookingId = BookingId;

        if (!AuthorizationState.CanManageCargo || details?.NextStatus != CompletedStatus)
        {
            completionBlockers = null;

            return;
        }

        CargoApiResult<CargoCompletionBlockersView> result;
        try
        {
            result = await CargoApi.GetCompletionBlockersAsync(requestedBookingId, disposeCancellation.Token);
        }
        catch (OperationCanceledException) when (disposeCancellation.IsCancellationRequested)
        {
            return;
        }

        if (disposeCancellation.IsCancellationRequested || requestedBookingId != BookingId)
        {
            return;
        }

        completionBlockers = result.IsSuccess ? result.Value : null;
    }

    private async Task OpenBlockingDiscrepancyAsync(Guid discrepancyId)
    {
        SelectTab(DiscrepanciesTab);

        if (discrepanciesPanel is not null)
        {
            await discrepanciesPanel.OpenDiscrepancyAsync(discrepancyId);
        }
    }

    private async Task OpenDiscrepancyFromExcursionAsync(Guid discrepancyId)
    {
        selectedControlContainerId = null;
        await OpenBlockingDiscrepancyAsync(discrepancyId);
    }

    private void OpenDiscrepancyContainer((Guid ContainerId, string? ContainerNumber) container)
    {
        var containerId = container.ContainerId;

        navigatedControlContainer = new CargoContainerRow(
            containerId,
            container.ContainerNumber ?? string.Empty,
            string.Empty,
            string.Empty,
            BookingId,
            null);
        selectedControlContainerId = containerId;
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
    private int containerMatchingCount;
    private int loadedContainerCount;
    private const int ContainersPerPage = 15;
    private const int ContainerSearchMaxLength = 20;
    private const int PortCodeLength = 5;
    private static readonly TimeSpan ContainerSearchDebounce = TimeSpan.FromMilliseconds(300);
    private CargoApiFailure? containersFailure;
    private bool isLoadingContainers;
    private bool hasLoadedContainers;
    private int containerLoadVersion;
    private CancellationTokenSource? containerLoadCancellation;
    private CancellationTokenSource? containerSearchDebounce;
    private Guid? selectedControlContainerId;

    private string? containerSearchInput;
    private string containerStatusInput = string.Empty;
    private string containerTypeInput = string.Empty;
    private string? containerPolInput;
    private string? containerPodInput;
    private string? containerFilterError;
    private string? containerFilterHint;
    private CargoContainerFilter appliedContainerFilter = CargoContainerFilter.None;

    private static readonly IReadOnlyList<string> ContainerStatusOptions = ["Registered", "Loaded", "Unloaded"];

    private bool IsContainerFilterActive => !appliedContainerFilter.IsEmpty;

    private bool HasContainerFilterInput =>
        !string.IsNullOrWhiteSpace(containerSearchInput)
        || !string.IsNullOrEmpty(containerStatusInput)
        || !string.IsNullOrEmpty(containerTypeInput)
        || !string.IsNullOrWhiteSpace(containerPolInput)
        || !string.IsNullOrWhiteSpace(containerPodInput)
        || IsContainerFilterActive;

    private IEnumerable<(string Label, string Value)> ActiveContainerFilters
    {
        get
        {
            if (appliedContainerFilter.Search is { } search) yield return ("Number contains", search);
            if (appliedContainerFilter.Status is { } status) yield return ("Status", status);
            if (appliedContainerFilter.ContainerType is { } containerType) yield return ("Type", containerType);
            if (appliedContainerFilter.PortOfLoadingCode is { } portOfLoading) yield return ("POL", portOfLoading);
            if (appliedContainerFilter.PortOfDischargeCode is { } portOfDischarge) yield return ("POD", portOfDischarge);
        }
    }

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
                ?? (navigatedControlContainer?.ContainerId == containerId ? navigatedControlContainer : null)
            : null;

    private string? ControlStateFor(Guid containerId)
    {
        return ContainerControls
            .FirstOrDefault(control => control.ContainerId == containerId)?
            .State;
    }

    private bool IsReeferContainer(Guid containerId)
    {
        return ContainerControls
            .FirstOrDefault(control => control.ContainerId == containerId)?
            .IsReefer is true;
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
        var requestVersion = ++containerLoadVersion;
        var filter = appliedContainerFilter;
        var pageNumber = containerPageNumber;

        containerLoadCancellation?.Cancel();
        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(disposeCancellation.Token);
        containerLoadCancellation = cancellation;

        isLoadingContainers = true;
        containersFailure = null;
        StateHasChanged();

        CargoApiResult<CargoContainerList> result;
        try
        {
            result = await CargoApi.GetContainersAsync(requestedBookingId, filter, pageNumber, cancellation.Token);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            return;
        }
        finally
        {
            if (ReferenceEquals(containerLoadCancellation, cancellation))
            {
                containerLoadCancellation = null;
            }

            cancellation.Dispose();
        }

        if (disposeCancellation.IsCancellationRequested
            || requestVersion != containerLoadVersion
            || requestedBookingId != BookingId)
        {
            return;
        }

        isLoadingContainers = false;

        if (!result.IsSuccess)
        {
            containersFailure = result.Failure;

            if (!hasLoadedContainers)
            {
                containers = [];
            }

            return;
        }

        var page = result.Value!;
        containers = page.Items ?? [];
        var isLegacyResponse = page.TotalCount is 0 && containers.Count > 0;
        containerTotalCount = isLegacyResponse ? containers.Count : page.TotalCount;
        loadedContainerCount = isLegacyResponse
            ? containers.Count(container => string.Equals(container.Status, LoadedStatus, StringComparison.OrdinalIgnoreCase))
            : page.LoadedCount;
        containerMatchingCount = page.MatchingCount ?? (filter.IsEmpty ? containerTotalCount : containers.Count);
        containerPageNumber = page.PageNumber;
        hasLoadedContainers = true;

        if (containers.Count == 0 && containerMatchingCount > 0 && ContainerPageCount < containerPageNumber)
        {
            containerPageNumber = ContainerPageCount;
            await LoadContainersAsync();

            return;
        }

        SyncManifestAddress();
    }

    private int ContainerPageCount => Math.Max(1, (int)Math.Ceiling(containerMatchingCount / (double)ContainersPerPage));

    private int FirstContainerRowNumber => containers.Count == 0 ? 0 : ((containerPageNumber - 1) * ContainersPerPage) + 1;

    private int LastContainerRowNumber => FirstContainerRowNumber + containers.Count - 1;

    private async Task ChangeContainerPageAsync(int pageNumber)
    {
        if (pageNumber < 1 || pageNumber > ContainerPageCount || pageNumber == containerPageNumber) return;
        containerPageNumber = pageNumber;
        selectedControlContainerId = null;
        CancelContainerEdit();
        await LoadContainersAsync();
    }

    private async Task ScheduleContainerSearchAsync()
    {
        containerSearchDebounce?.Cancel();
        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(disposeCancellation.Token);
        containerSearchDebounce = cancellation;

        try
        {
            await Task.Delay(ContainerSearchDebounce, cancellation.Token);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            return;
        }
        finally
        {
            if (ReferenceEquals(containerSearchDebounce, cancellation))
            {
                containerSearchDebounce = null;
            }

            cancellation.Dispose();
        }

        await InvokeAsync(ApplyTypedContainerFiltersAsync);
    }

    private Task ApplyTypedContainerFiltersAsync()
    {
        containerFilterHint = IncompletePortHint();

        if (containerFilterHint is not null)
        {
            containerFilterError = null;

            return Task.CompletedTask;
        }

        return ApplyContainerFiltersAsync();
    }

    private string? IncompletePortHint()
    {
        var incompletePorts = new[]
        {
            IsIncompletePortCode(containerPolInput) ? "POL" : null,
            IsIncompletePortCode(containerPodInput) ? "POD" : null,
        }.OfType<string>().ToList();

        return incompletePorts.Count == 0
            ? null
            : $"Enter all {PortCodeLength} characters of the {string.Join(" and ", incompletePorts)} code (e.g. NLRTM). The filters and search apply once it is complete.";
    }

    private static bool IsIncompletePortCode(string? value)
    {
        return !string.IsNullOrWhiteSpace(value)
            && value.Trim().Length < PortCodeLength
            && value.Trim().All(char.IsLetterOrDigit);
    }

    private Task ApplyContainerFiltersNowAsync()
    {
        containerSearchDebounce?.Cancel();

        return ApplyContainerFiltersAsync();
    }

    private Task ApplyContainerFiltersAsync()
    {
        containerFilterHint = null;
        containerFilterError = ValidateContainerFilterInputs();

        if (containerFilterError is not null)
        {
            return Task.CompletedTask;
        }

        var filter = CargoContainerFilter.Create(
            containerSearchInput,
            containerStatusInput,
            containerTypeInput,
            containerPolInput,
            containerPodInput);

        if (filter == appliedContainerFilter && containersFailure is null)
        {
            return Task.CompletedTask;
        }

        appliedContainerFilter = filter;
        containerPageNumber = 1;
        manifestExportError = null;
        manifestExportNotice = null;

        return LoadContainersAsync();
    }

    private Task ResetContainerFiltersAsync()
    {
        containerSearchDebounce?.Cancel();
        containerSearchInput = null;
        containerStatusInput = string.Empty;
        containerTypeInput = string.Empty;
        containerPolInput = null;
        containerPodInput = null;
        containerFilterError = null;
        containerFilterHint = null;

        return ApplyContainerFiltersAsync();
    }

    private string? ValidateContainerFilterInputs()
    {
        if (containerSearchInput?.Trim().Length > ContainerSearchMaxLength)
        {
            return $"Search must not exceed {ContainerSearchMaxLength} characters.";
        }

        if (!IsBlankOrPortCode(containerPolInput))
        {
            return "POL must be a five-character UN/LOCODE.";
        }

        if (!IsBlankOrPortCode(containerPodInput))
        {
            return "POD must be a five-character UN/LOCODE.";
        }

        return null;
    }

    private static bool IsBlankOrPortCode(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            || (value.Trim().Length == PortCodeLength && value.Trim().All(char.IsLetterOrDigit));
    }

    private void ApplyAddressToContainerFilters()
    {
        containerSearchInput = ManifestSearchFromQuery;
        containerStatusInput = ManifestStatusFromQuery ?? string.Empty;
        containerTypeInput = ManifestTypeFromQuery?.Trim().ToUpperInvariant() ?? string.Empty;
        containerPolInput = ManifestPolFromQuery;
        containerPodInput = ManifestPodFromQuery;
        containerFilterError = ValidateContainerFilterInputs();
        appliedContainerFilter = containerFilterError is null
            ? CargoContainerFilter.Create(containerSearchInput, containerStatusInput, containerTypeInput, containerPolInput, containerPodInput)
            : CargoContainerFilter.None;
        containerPageNumber = ManifestPageFromQuery is > 0 ? ManifestPageFromQuery.Value : 1;
    }

    private string ManifestAddress =>
        BuildManifestAddress(BookingId, BoardQuery, appliedContainerFilter, containerPageNumber, activeTab);

    internal static string BuildManifestAddress(
        Guid bookingId,
        string? boardQuery,
        CargoContainerFilter filter,
        int pageNumber,
        string? tab = null)
    {
        var parts = new List<string>();

        AddAddressPart(parts, BoardQueryKey, boardQuery);
        AddAddressPart(parts, ManifestSearchQueryKey, filter.Search);
        AddAddressPart(parts, ManifestStatusQueryKey, filter.Status);
        AddAddressPart(parts, ManifestTypeQueryKey, filter.ContainerType);
        AddAddressPart(parts, ManifestPolQueryKey, filter.PortOfLoadingCode);
        AddAddressPart(parts, ManifestPodQueryKey, filter.PortOfDischargeCode);

        if (pageNumber > 1)
        {
            parts.Add($"{ManifestPageQueryKey}={pageNumber}");
        }

        if (!string.IsNullOrEmpty(tab) && tab != OverviewTab)
        {
            parts.Add($"{TabQueryKey}={tab}");
        }

        return parts.Count == 0
            ? $"cargo/{bookingId}"
            : $"cargo/{bookingId}?{string.Join('&', parts)}";
    }

    private string ManifestOutputQuery(bool includeReturnState)
    {
        var parts = new List<string>(appliedContainerFilter.QueryParts());

        if (includeReturnState)
        {
            AddAddressPart(parts, BoardQueryKey, BoardQuery);

            if (containerPageNumber > 1)
            {
                parts.Add($"returnPage={containerPageNumber}");
            }
        }

        return parts.Count == 0 ? string.Empty : $"?{string.Join('&', parts)}";
    }

    private string ManifestExportUrl => $"/cargo/{BookingId}/manifest/export{ManifestOutputQuery(includeReturnState: false)}";

    private string ManifestPrintHref => $"/cargo/{BookingId}/manifest/print{ManifestOutputQuery(includeReturnState: true)}";

    private const int ManifestOutputRowLimit = 10_000;

    private bool ManifestOutputExceedsLimit => containerMatchingCount > ManifestOutputRowLimit;

    private string ManifestOutputLimitMessage => string.Create(
        CultureInfo.InvariantCulture,
        $"{containerMatchingCount:N0} containers match, more than the {ManifestOutputRowLimit:N0} that can be exported or printed at once. Narrow the filters first.");

    private bool CanProduceManifestOutput =>
        hasLoadedContainers
        && !isLoadingContainers
        && !ManifestOutputExceedsLimit
        && containersFailure is null
        && containerFilterError is null
        && containerFilterHint is null;

    private bool isExportingManifest;
    private string? manifestExportError;
    private string? manifestExportNotice;

    private async Task ExportManifestCsvAsync()
    {
        if (isExportingManifest || !CanProduceManifestOutput)
        {
            return;
        }

        isExportingManifest = true;
        manifestExportError = null;
        manifestExportNotice = null;

        try
        {
            var result = await JS.InvokeAsync<CargoFileDownloadResult>(
                "harborOpsFiles.download",
                disposeCancellation.Token,
                ManifestExportUrl,
                "text/csv");

            if (result.Ok)
            {
                manifestExportNotice = result.Count switch
                {
                    null => "Exported the matching containers.",
                    0 => "No containers match, so the CSV contains only the column headers.",
                    var count => string.Create(
                        CultureInfo.InvariantCulture,
                        $"Exported {count:N0} matching {(count == 1 ? "container" : "containers")}."),
                };
            }
            else
            {
                manifestExportError = result.Message ?? "The manifest could not be exported. Please try again.";
            }
        }
        catch (OperationCanceledException) when (disposeCancellation.IsCancellationRequested)
        {
        }
        catch (JSException)
        {
            manifestExportError = "The manifest could not be exported. Please try again.";
        }
        finally
        {
            isExportingManifest = false;
        }
    }

    private static void AddAddressPart(List<string> parts, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            parts.Add($"{key}={Uri.EscapeDataString(value)}");
        }
    }

    private void SyncManifestAddress()
    {
        var target = ManifestAddress;

        if (string.Equals(
            Navigation.ToBaseRelativePath(Navigation.Uri).TrimStart('/'),
            target,
            StringComparison.Ordinal))
        {
            return;
        }

        Navigation.NavigateTo($"/{target}", forceLoad: false, replace: true);
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

    private static readonly IReadOnlyList<ContainerTypeOption> FilterContainerTypes =
    [
        .. StandardContainerTypes,
        new("20GP", "20 ft general purpose (20GP)"),
        new("40HC", "40 ft high-cube (40HC)"),
        new("40RH", "40 ft refrigerated high-cube (40RH)"),
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
