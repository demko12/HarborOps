using System.Globalization;
using HarborOps.Web.Api;
using HarborOps.Web.Api.Cargo;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace HarborOps.Web.Components.Pages.Cargo;

public partial class CargoDiscrepanciesPanel : IDisposable
{
    internal const int PageSize = 10;
    internal const int DescriptionMaxLength = 1000;
    internal const int TextMaxLength = 2000;
    internal const int SummaryMinLength = 20;

    internal const string ReportedStatus = "Reported";
    internal const string InvestigatingStatus = "Investigating";
    internal const string ResolvedStatus = "Resolved";
    internal const string VoidedStatus = "Voided";

    internal static readonly IReadOnlyList<(string Value, string Label)> Types =
    [
        ("Damage", "Damage"),
        ("Shortage", "Shortage"),
        ("SealMismatch", "Seal mismatch"),
        ("TemperatureRelated", "Temperature related"),
        ("Other", "Other"),
    ];

    internal static readonly IReadOnlyList<string> Severities = ["Low", "Medium", "High", "Critical"];

    internal static readonly IReadOnlyList<(string Value, string Label)> Dispositions =
    [
        ("Corrected", "Corrected"),
        ("AcceptedDeviation", "Accepted deviation"),
        ("NoDiscrepancyFound", "No discrepancy found"),
    ];

    private static readonly IReadOnlyList<string> RegistrationStatuses = ["Loaded", "InTransit", "Arrived", "Unloaded"];

    private readonly CancellationTokenSource disposeCancellation = new();

    private CargoDiscrepancyPage? page;
    private CargoApiFailure? listFailure;
    private bool isLoadingList = true;
    private string statusFilter = string.Empty;
    private string typeFilter = string.Empty;
    private string severityFilter = string.Empty;
    private int pageNumber = 1;
    private Guid loadedBookingId;

    private bool isRegisterOpen;
    private bool isRegistering;
    private string registerType = string.Empty;
    private string registerSeverity = string.Empty;
    private string? registerContainerNumber;
    private IReadOnlyList<CargoContainerRow> containerSuggestions = [];
    private int suggestionVersion;
    private int containerMatchCount;
    private int highlightedContainerIndex = -1;
    private bool isContainerMenuOpen;
    private bool isLoadingContainers;
    private DateTime? registerObservedAtUtc;
    private string? registerDescription;
    private Dictionary<string, string> registerErrors = new(StringComparer.OrdinalIgnoreCase);
    private string? registerError;

    private CargoDiscrepancyView? selected;
    private CargoApiFailure? selectedFailure;
    private bool isLoadingSelected;
    private Guid? selectedId;

    private string? noteText;
    private string resolveDisposition = string.Empty;
    private string? resolveSummary;
    private string? voidReason;
    private PendingAction? pendingAction;
    private bool isWorking;
    private string? actionError;
    private string? actionArea;
    private bool actionConflict;

    private const string StartArea = "start";
    private const string NoteArea = "note";
    private const string ResolveArea = "resolve";
    private const string VoidArea = "void";

    [Parameter]
    public Guid BookingId { get; set; }

    [Parameter]
    public string BookingStatus { get; set; } = string.Empty;

    [Parameter]
    public EventCallback<(Guid ContainerId, string? ContainerNumber)> OnOpenContainer { get; set; }

    [Parameter]
    public EventCallback OnChanged { get; set; }

    [Parameter]
    public EventCallback<int> OnOpenCountChanged { get; set; }

    private int? reportedOpenCount;

    private async Task ReportOpenCountAsync(int openCount)
    {
        if (reportedOpenCount == openCount)
        {
            return;
        }

        reportedOpenCount = openCount;
        await OnOpenCountChanged.InvokeAsync(openCount);
    }

    [Inject]
    private CargoBookingsApiClient CargoApi { get; set; } = null!;

    [Inject]
    private CurrentUserAuthorizationState AuthorizationState { get; set; } = null!;

    internal enum PendingAction
    {
        Resolve,
        Void,
    }

    private bool CanManage => AuthorizationState.CanManageCargo;

    private bool AcceptsRegistration => RegistrationStatuses.Contains(BookingStatus, StringComparer.OrdinalIgnoreCase);

    private int OpenCount => page?.Summary.OpenCount ?? 0;

    private int TotalPages => page is null || page.TotalCount == 0
        ? 1
        : (int)Math.Ceiling(page.TotalCount / (double)PageSize);

    private bool BlocksCompletion => OpenCount > 0 && string.Equals(BookingStatus, "Unloaded", StringComparison.OrdinalIgnoreCase);

    public void Dispose()
    {
        disposeCancellation.Cancel();
        disposeCancellation.Dispose();
    }

    public async Task OpenDiscrepancyAsync(Guid discrepancyId)
    {
        await LoadListAsync();
        await SelectAsync(discrepancyId);
        StateHasChanged();
    }

    protected override Task OnParametersSetAsync()
    {
        if (loadedBookingId == BookingId)
        {
            return Task.CompletedTask;
        }

        loadedBookingId = BookingId;
        pageNumber = 1;
        ResetFilters();
        selected = null;
        selectedId = null;

        return LoadListAsync();
    }

    private async Task LoadListAsync()
    {
        isLoadingList = true;
        listFailure = null;

        try
        {
            var result = await CargoApi.GetDiscrepanciesAsync(
                BookingId,
                new CargoDiscrepancyFilter(
                    NullIfEmpty(statusFilter),
                    null,
                    pageNumber,
                    PageSize,
                    NullIfEmpty(typeFilter),
                    NullIfEmpty(severityFilter)),
                disposeCancellation.Token);

            if (result.IsSuccess)
            {
                page = result.Value;
                await ReportOpenCountAsync(page!.Summary.OpenCount);
            }
            else
            {
                listFailure = result.Failure;
            }
        }
        catch (OperationCanceledException) when (disposeCancellation.IsCancellationRequested)
        {
            return;
        }

        isLoadingList = false;
    }

    private bool HasFilters =>
        !string.IsNullOrEmpty(statusFilter) || !string.IsNullOrEmpty(typeFilter) || !string.IsNullOrEmpty(severityFilter);

    private async Task ApplyFiltersAsync()
    {
        pageNumber = 1;
        await LoadListAsync();
    }

    private static IEnumerable<(string Status, int Count)> SummaryEntries(CargoDiscrepancySummaryView summary)
    {
        yield return (ReportedStatus, summary.ReportedCount);
        yield return (InvestigatingStatus, summary.InvestigatingCount);
        yield return (ResolvedStatus, summary.ResolvedCount);
        yield return (VoidedStatus, summary.VoidedCount);
    }

    private Task ToggleStatusFilterAsync(string status)
    {
        statusFilter = statusFilter == status ? string.Empty : status;

        return ApplyFiltersAsync();
    }

    private Task ClearFiltersAsync()
    {
        ResetFilters();

        return ApplyFiltersAsync();
    }

    private void ResetFilters()
    {
        statusFilter = string.Empty;
        typeFilter = string.Empty;
        severityFilter = string.Empty;
    }

    private async Task GoToPageAsync(int target)
    {
        if (target < 1 || target > TotalPages || target == pageNumber)
        {
            return;
        }

        pageNumber = target;
        await LoadListAsync();
    }

    private Task ToggleRegister()
    {
        isRegisterOpen = !isRegisterOpen;
        ResetRegisterForm();

        return isRegisterOpen ? LoadContainerSuggestionsAsync() : Task.CompletedTask;
    }

    private async Task LoadContainerSuggestionsAsync()
    {
        var requestVersion = ++suggestionVersion;
        var search = registerContainerNumber?.Trim();
        isLoadingContainers = true;

        try
        {
            var result = await CargoApi.GetContainersAsync(
                BookingId,
                CargoContainerFilter.Create(search, null, null, null, null),
                1,
                disposeCancellation.Token);

            if (requestVersion != suggestionVersion)
            {
                return;
            }

            if (result.IsSuccess)
            {
                containerSuggestions = result.Value!.Items;
                containerMatchCount = result.Value.MatchingCount ?? result.Value.TotalCount;
                highlightedContainerIndex = Math.Min(highlightedContainerIndex, containerSuggestions.Count - 1);
            }

            isLoadingContainers = false;
        }
        catch (OperationCanceledException) when (disposeCancellation.IsCancellationRequested)
        {
        }
    }

    private Task SearchContainersAsync()
    {
        isContainerMenuOpen = true;
        highlightedContainerIndex = -1;

        return LoadContainerSuggestionsAsync();
    }

    private Task OpenContainerMenuAsync()
    {
        isContainerMenuOpen = true;

        return containerSuggestions.Count == 0 && !isLoadingContainers
            ? LoadContainerSuggestionsAsync()
            : Task.CompletedTask;
    }

    private Task ToggleContainerMenuAsync()
    {
        if (isContainerMenuOpen)
        {
            CloseContainerMenu();

            return Task.CompletedTask;
        }

        return OpenContainerMenuAsync();
    }

    private void CloseContainerMenu()
    {
        isContainerMenuOpen = false;
        highlightedContainerIndex = -1;
    }

    private void SelectContainer(CargoContainerRow container)
    {
        registerContainerNumber = container.ContainerNumber;
        registerErrors.Remove("containerId");
        CloseContainerMenu();
    }

    private async Task HandleContainerKeyDown(KeyboardEventArgs args)
    {
        switch (args.Key)
        {
            case "ArrowDown":
                if (!isContainerMenuOpen)
                {
                    await OpenContainerMenuAsync();
                }

                if (containerSuggestions.Count > 0)
                {
                    highlightedContainerIndex = (highlightedContainerIndex + 1) % containerSuggestions.Count;
                }

                break;
            case "ArrowUp":
                if (containerSuggestions.Count > 0)
                {
                    highlightedContainerIndex = highlightedContainerIndex <= 0
                        ? containerSuggestions.Count - 1
                        : highlightedContainerIndex - 1;
                }

                break;
            case "Escape":
                CloseContainerMenu();
                break;
        }
    }

    private bool TrySelectHighlightedContainer()
    {
        if (!isContainerMenuOpen
            || highlightedContainerIndex < 0
            || highlightedContainerIndex >= containerSuggestions.Count)
        {
            return false;
        }

        SelectContainer(containerSuggestions[highlightedContainerIndex]);

        return true;
    }

    private static string ContainerOptionId(int index)
    {
        return $"discrepancy-container-option-{index.ToString(CultureInfo.InvariantCulture)}";
    }

    private string? ActiveContainerOptionId =>
        isContainerMenuOpen && highlightedContainerIndex >= 0 ? ContainerOptionId(highlightedContainerIndex) : null;

    private void ResetRegisterForm()
    {
        registerType = string.Empty;
        registerSeverity = string.Empty;
        registerContainerNumber = null;
        containerSuggestions = [];
        containerMatchCount = 0;
        isLoadingContainers = false;
        CloseContainerMenu();
        registerObservedAtUtc = TruncateToMinute(DateTime.UtcNow);
        registerDescription = null;
        registerErrors = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        registerError = null;
    }

    private bool RequiresContainer => registerType is "SealMismatch" or "TemperatureRelated";

    private string? ErrorFor(string field)
    {
        return registerErrors.TryGetValue(field, out var message) ? message : null;
    }

    private async Task SubmitRegisterAsync()
    {
        if (isRegistering || TrySelectHighlightedContainer())
        {
            return;
        }

        CloseContainerMenu();
        registerErrors = ValidateRegistration();
        registerError = null;

        if (registerErrors.Count > 0)
        {
            return;
        }

        isRegistering = true;

        try
        {
            Guid? containerId = null;

            if (!string.IsNullOrWhiteSpace(registerContainerNumber))
            {
                containerId = await FindContainerIdAsync(registerContainerNumber.Trim());

                if (containerId is null)
                {
                    return;
                }
            }

            var result = await CargoApi.RegisterDiscrepancyAsync(
                BookingId,
                new RegisterCargoDiscrepancyWebRequest(
                    registerType,
                    registerSeverity,
                    containerId,
                    DateTime.SpecifyKind(registerObservedAtUtc!.Value, DateTimeKind.Utc),
                    registerDescription!.Trim()),
                disposeCancellation.Token);

            if (!result.IsSuccess)
            {
                ApplyRegisterFailure(result.Failure!);

                return;
            }

            isRegisterOpen = false;
            ResetRegisterForm();
            pageNumber = 1;
            ResetFilters();

            await LoadListAsync();
            Show(result.Value!);
            await OnChanged.InvokeAsync();
        }
        catch (OperationCanceledException) when (disposeCancellation.IsCancellationRequested)
        {
        }
        finally
        {
            isRegistering = false;
        }
    }

    private Dictionary<string, string> ValidateRegistration()
    {
        var errors = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (string.IsNullOrEmpty(registerType))
        {
            errors["type"] = "Select a discrepancy type.";
        }

        if (string.IsNullOrEmpty(registerSeverity))
        {
            errors["severity"] = "Select a severity.";
        }

        if (RequiresContainer && string.IsNullOrWhiteSpace(registerContainerNumber))
        {
            errors["containerId"] = "This type of discrepancy needs the container it was observed on.";
        }

        if (registerObservedAtUtc is not { } observedAt)
        {
            errors["observedAtUtc"] = "Enter when the discrepancy was observed.";
        }
        else if (DateTime.SpecifyKind(observedAt, DateTimeKind.Utc) > DateTime.UtcNow)
        {
            errors["observedAtUtc"] = "The observation time must not be in the future.";
        }

        if (string.IsNullOrWhiteSpace(registerDescription))
        {
            errors["description"] = "Describe what was observed.";
        }
        else if (registerDescription.Trim().Length > DescriptionMaxLength)
        {
            errors["description"] = $"The description must not exceed {DescriptionMaxLength} characters.";
        }

        return errors;
    }

    private async Task<Guid?> FindContainerIdAsync(string containerNumber)
    {
        var suggested = containerSuggestions.FirstOrDefault(container =>
            string.Equals(container.ContainerNumber, containerNumber, StringComparison.OrdinalIgnoreCase));

        if (suggested is not null)
        {
            return suggested.ContainerId;
        }

        var result = await CargoApi.GetContainersAsync(
            BookingId,
            CargoContainerFilter.Create(containerNumber, null, null, null, null),
            1,
            disposeCancellation.Token);

        if (!result.IsSuccess)
        {
            registerError = $"{result.Failure!.Title} {result.Failure.Message}".Trim();

            return null;
        }

        var match = result.Value!.Items.FirstOrDefault(container =>
            string.Equals(container.ContainerNumber, containerNumber, StringComparison.OrdinalIgnoreCase));

        if (match is null)
        {
            registerErrors["containerId"] = $"No container {containerNumber.ToUpperInvariant()} is on this booking.";
        }

        return match?.ContainerId;
    }

    private void ApplyRegisterFailure(CargoApiFailure failure)
    {
        if (failure.Errors.Count > 0)
        {
            foreach (var (field, messages) in failure.Errors)
            {
                registerErrors[field] = string.Join(" ", messages);
            }

            return;
        }

        registerError = $"{failure.Title} {failure.Message}".Trim();
    }

    private async Task SelectAsync(Guid discrepancyId)
    {
        selectedId = discrepancyId;
        selected = null;
        selectedFailure = null;
        ResetActionForms();
        await LoadSelectedAsync();
    }

    private async Task LoadSelectedAsync()
    {
        if (selectedId is not { } discrepancyId)
        {
            return;
        }

        isLoadingSelected = true;
        selectedFailure = null;
        actionConflict = false;

        try
        {
            var result = await CargoApi.GetDiscrepancyAsync(BookingId, discrepancyId, disposeCancellation.Token);

            if (result.IsSuccess)
            {
                selected = result.Value;
            }
            else
            {
                selected = null;
                selectedFailure = result.Failure;
            }
        }
        catch (OperationCanceledException) when (disposeCancellation.IsCancellationRequested)
        {
            return;
        }

        isLoadingSelected = false;
    }

    private void Show(CargoDiscrepancyView view)
    {
        selectedId = view.Discrepancy.Id;
        selected = view;
        selectedFailure = null;
        isLoadingSelected = false;
        ResetActionForms();
    }

    private void CloseDetails()
    {
        if (isWorking)
        {
            return;
        }

        selectedId = null;
        selected = null;
        selectedFailure = null;
        ResetActionForms();
    }

    private void ResetActionForms()
    {
        noteText = null;
        resolveDisposition = string.Empty;
        resolveSummary = null;
        voidReason = null;
        pendingAction = null;
        actionError = null;
        actionArea = null;
        actionConflict = false;
    }

    private void AskToConfirm(PendingAction action)
    {
        actionConflict = false;
        actionArea = AreaOf(action);

        actionError = action switch
        {
            PendingAction.Resolve => ValidateResolution(),
            _ => ValidateVoid(),
        };

        if (actionError is null)
        {
            pendingAction = action;
        }
    }

    private static string AreaOf(PendingAction action)
    {
        return action == PendingAction.Resolve ? ResolveArea : VoidArea;
    }

    private Task StartInvestigationAsync()
    {
        return RunAsync(StartArea, discrepancyId => CargoApi.StartDiscrepancyInvestigationAsync(
            BookingId,
            discrepancyId,
            disposeCancellation.Token));
    }

    private void CancelConfirmation()
    {
        pendingAction = null;
    }

    private string? ValidateResolution()
    {
        if (string.IsNullOrEmpty(resolveDisposition))
        {
            return "Select a disposition.";
        }

        var summary = resolveSummary?.Trim() ?? string.Empty;

        if (summary.Length < SummaryMinLength)
        {
            return $"Write a resolution summary of at least {SummaryMinLength} characters.";
        }

        return summary.Length > TextMaxLength
            ? $"The resolution summary must not exceed {TextMaxLength} characters."
            : null;
    }

    private string? ValidateVoid()
    {
        if (string.IsNullOrWhiteSpace(voidReason))
        {
            return "Give a reason for voiding the discrepancy.";
        }

        return voidReason.Trim().Length > TextMaxLength
            ? $"The reason must not exceed {TextMaxLength} characters."
            : null;
    }

    private Task AddNoteAsync()
    {
        actionArea = NoteArea;
        actionConflict = false;

        if (string.IsNullOrWhiteSpace(noteText))
        {
            actionError = "Write a note first.";

            return Task.CompletedTask;
        }

        if (noteText.Trim().Length > TextMaxLength)
        {
            actionError = $"The note must not exceed {TextMaxLength} characters.";

            return Task.CompletedTask;
        }

        return RunAsync(NoteArea, discrepancyId => CargoApi.AddDiscrepancyNoteAsync(
            BookingId,
            discrepancyId,
            new AddCargoDiscrepancyNoteWebRequest(noteText.Trim()),
            disposeCancellation.Token));
    }

    private Task ConfirmPendingActionAsync()
    {
        return pendingAction switch
        {
            PendingAction.Resolve => RunAsync(ResolveArea, discrepancyId => CargoApi.ResolveDiscrepancyAsync(
                BookingId,
                discrepancyId,
                new ResolveCargoDiscrepancyWebRequest(resolveDisposition, resolveSummary!.Trim()),
                disposeCancellation.Token)),
            PendingAction.Void => RunAsync(VoidArea, discrepancyId => CargoApi.VoidDiscrepancyAsync(
                BookingId,
                discrepancyId,
                new VoidCargoDiscrepancyWebRequest(voidReason!.Trim()),
                disposeCancellation.Token)),
            _ => Task.CompletedTask,
        };
    }

    private async Task RunAsync(string area, Func<Guid, Task<CargoApiResult<CargoDiscrepancyView>>> action)
    {
        if (isWorking || selected is null)
        {
            return;
        }

        isWorking = true;
        actionArea = area;
        actionError = null;
        actionConflict = false;

        try
        {
            var result = await action(selected.Discrepancy.Id);

            if (!result.IsSuccess)
            {
                var failure = result.Failure!;
                actionError = failure.Errors.Count > 0
                    ? string.Join(" ", failure.Errors.SelectMany(error => error.Value))
                    : $"{failure.Title} {failure.Message}".Trim();
                actionConflict = failure.IsConflict;
                pendingAction = null;

                return;
            }

            Show(result.Value!);
            await LoadListAsync();
            await OnChanged.InvokeAsync();
        }
        catch (OperationCanceledException) when (disposeCancellation.IsCancellationRequested)
        {
        }
        finally
        {
            isWorking = false;
        }
    }

    private async Task ReloadAfterConflictAsync()
    {
        ResetActionForms();
        await LoadSelectedAsync();
        await LoadListAsync();
    }

    private Task OpenContainerAsync(CargoDiscrepancyRow discrepancy)
    {
        CloseDetails();

        return discrepancy.ContainerId is { } containerId
            ? OnOpenContainer.InvokeAsync((containerId, discrepancy.ContainerNumber))
            : Task.CompletedTask;
    }

    private string ConfirmationQuestion(PendingAction action)
    {
        return action switch
        {
            PendingAction.Resolve =>
                $"Resolve this discrepancy as \"{DispositionLabel(resolveDisposition)}\"? A resolved discrepancy can no longer change.",
            _ => "Void this discrepancy? A voided discrepancy can no longer change and stops blocking completion.",
        };
    }

    internal static string TypeLabel(string type)
    {
        return Types.FirstOrDefault(entry => entry.Value == type).Label ?? type;
    }

    internal static string DispositionLabel(string? disposition)
    {
        return Dispositions.FirstOrDefault(entry => entry.Value == disposition).Label ?? disposition ?? string.Empty;
    }

    internal static string StatusClass(string status)
    {
        return status switch
        {
            ReportedStatus => "discrepancy-status discrepancy-status-reported",
            InvestigatingStatus => "discrepancy-status discrepancy-status-investigating",
            ResolvedStatus => "discrepancy-status discrepancy-status-resolved",
            _ => "discrepancy-status discrepancy-status-voided",
        };
    }

    internal static string SeverityClass(string severity)
    {
        return $"discrepancy-severity discrepancy-severity-{severity.ToLowerInvariant()}";
    }

    internal static string HistoryLabel(string eventType)
    {
        return eventType switch
        {
            "Reported" => "Reported",
            "InvestigationStarted" => "Investigation started",
            "NoteAdded" => "Note added",
            "Resolved" => "Resolved",
            "Voided" => "Voided",
            _ => eventType,
        };
    }

    internal static string Count(int value, string singular, string plural)
    {
        return $"{value.ToString(CultureInfo.InvariantCulture)} {(value == 1 ? singular : plural)}";
    }

    private static string ObservedAtMax =>
        TruncateToMinute(DateTime.UtcNow).ToString("yyyy-MM-ddTHH:mm", CultureInfo.InvariantCulture);

    private static DateTime TruncateToMinute(DateTime value)
    {
        return new DateTime(value.Year, value.Month, value.Day, value.Hour, value.Minute, 0, DateTimeKind.Utc);
    }

    private static string? NullIfEmpty(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }
}
