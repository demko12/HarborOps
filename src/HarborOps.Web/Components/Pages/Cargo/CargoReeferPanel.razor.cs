using System.ComponentModel.DataAnnotations;
using HarborOps.Web.Api.Cargo;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;

namespace HarborOps.Web.Components.Pages.Cargo;

public partial class CargoReeferPanel : IDisposable
{
    internal const string OpenExcursionStatus = "Open";
    internal const string ResolvedExcursionStatus = "Resolved";
    internal const string SupersededExcursionStatus = "Superseded";
    internal const string EffectiveReadingStatus = "Effective";
    internal const string SupersededReadingStatus = "Superseded";
    internal const string VoidedReadingStatus = "Voided";
    internal const string CorrectiveActionDisposition = "CorrectiveAction";
    internal const string AcceptedDeviationDisposition = "AcceptedDeviation";

    internal const string NotAReeferMessage =
        "Temperature monitoring is only available for reefer containers.";

    internal const string ProfileMissingMessage =
        "Configure the acceptable temperature range before recording readings.";

    internal const string BlockedReleaseMessage =
        "An unresolved temperature excursion blocks releasing this container and starting a load operation.";

    internal const string InspectionOutOfRangeMessage = "Outside profile range";

    private const int PageSize = 10;

    private static readonly IReadOnlyDictionary<string, string> ProfileFieldMap =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["minimumCelsius"] = nameof(ProfileFormModel.MinimumCelsius),
            ["maximumCelsius"] = nameof(ProfileFormModel.MaximumCelsius),
            ["targetCelsius"] = nameof(ProfileFormModel.TargetCelsius),
            ["reason"] = nameof(ProfileFormModel.Reason),
        };

    private static readonly IReadOnlyDictionary<string, string> ReadingFieldMap =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["temperatureCelsius"] = nameof(ReadingFormModel.TemperatureCelsius),
            ["measuredAtUtc"] = nameof(ReadingFormModel.MeasuredAtUtc),
            ["notes"] = nameof(ReadingFormModel.Notes),
            ["reason"] = nameof(ReadingFormModel.Reason),
        };

    private static readonly IReadOnlyDictionary<string, string> ResolutionFieldMap =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["disposition"] = nameof(ResolutionFormModel.Disposition),
            ["explanation"] = nameof(ResolutionFormModel.Explanation),
        };

    private readonly CancellationTokenSource disposeCancellation = new();

    private ReeferProfileView? profileView;
    private ReeferReadingPage? readingPage;
    private ReeferExcursionList? excursionList;
    private CargoApiFailure? failure;
    private bool isLoading = true;
    private Guid loadedContainerId;
    private int pageNumber = 1;

    private ProfileFormModel profileModel = new();
    private EditContext profileEditContext = null!;
    private ValidationMessageStore profileMessages = null!;
    private bool isEditingProfile;
    private bool isSavingProfile;
    private string? profileError;

    private ReadingFormModel readingModel = new();
    private EditContext readingEditContext = null!;
    private ValidationMessageStore readingMessages = null!;
    private bool isSavingReading;
    private string? readingError;
    private Guid? correctingReadingId;
    private bool isRecordingReading;

    private ResolutionFormModel resolutionModel = new();
    private EditContext resolutionEditContext = null!;
    private ValidationMessageStore resolutionMessages = null!;
    private bool isResolving;
    private string? resolutionError;
    private Guid? resolvingExcursionId;

    private Guid? voidingReadingId;
    private string? voidReason;
    private string? voidError;
    private bool isVoiding;

    private bool isChangeLogOpen;

    [Parameter]
    public Guid ContainerId { get; set; }

    [Parameter]
    public string ContainerNumber { get; set; } = string.Empty;

    [Parameter]
    public bool IsReefer { get; set; }

    [Parameter]
    public EventCallback OnChanged { get; set; }

    private bool CanManage => AuthorizationState.CanManageCargo;

    private ReeferProfileRow? Profile => profileView?.Profile;

    private IReadOnlyList<ReeferProfileChangeRow> Changes => profileView?.Changes ?? [];

    private IReadOnlyList<ReeferReadingRow> Readings => readingPage?.Items ?? [];

    private IReadOnlyList<ReeferExcursionRow> Excursions => excursionList?.Items ?? [];

    private IReadOnlyList<ReeferExcursionRow> OpenExcursions =>
        [.. Excursions.Where(excursion => excursion.Status == OpenExcursionStatus)];

    private int OpenExcursionCount => excursionList?.OpenCount ?? 0;

    private bool HasProfile => Profile is not null;

    private decimal? InspectionTemperatureCelsius =>
        profileView?.LatestInspectionTemperatureCelsius;

    private bool HasInspectionTemperature => HasProfile && InspectionTemperatureCelsius is not null;

    private bool IsInspectionTemperatureOutOfRange =>
        HasProfile
        && InspectionTemperatureCelsius is { } temperature
        && (temperature < Profile!.MinimumCelsius || temperature > Profile!.MaximumCelsius);

    private bool CanRecordReading => CanManage && HasProfile;

    private int TotalPages => readingPage is null || readingPage.TotalCount == 0
        ? 1
        : (readingPage.TotalCount + PageSize - 1) / PageSize;

    private bool HasPreviousPage => pageNumber > 1;

    private bool HasNextPage => pageNumber < TotalPages;

    public void Dispose()
    {
        DetachValidation();

        disposeCancellation.Cancel();
        disposeCancellation.Dispose();
    }

    protected override void OnInitialized()
    {
        ResetProfileForm();
        ResetReadingForm();
        ResetResolutionForm();
    }

    protected override async Task OnParametersSetAsync()
    {
        if (!IsReefer)
        {
            isLoading = false;

            return;
        }

        if (loadedContainerId == ContainerId && !isLoading)
        {
            return;
        }

        pageNumber = 1;

        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        isLoading = true;
        failure = null;
        loadedContainerId = ContainerId;

        try
        {
            var profileResult = await CargoApi.GetReeferProfileAsync(
                ContainerId,
                disposeCancellation.Token);

            if (!profileResult.IsSuccess)
            {
                profileView = null;
                readingPage = null;
                excursionList = null;
                failure = profileResult.Failure;

                return;
            }

            profileView = profileResult.Value;

            var excursionResult = await CargoApi.GetReeferExcursionsAsync(
                ContainerId,
                disposeCancellation.Token);

            if (!excursionResult.IsSuccess)
            {
                failure = excursionResult.Failure;

                return;
            }

            excursionList = excursionResult.Value;

            await LoadReadingsAsync();
        }
        catch (OperationCanceledException) when (disposeCancellation.IsCancellationRequested)
        {
        }
        finally
        {
            isLoading = false;
        }
    }

    private async Task LoadReadingsAsync()
    {
        var result = await CargoApi.GetReeferReadingsAsync(
            ContainerId,
            pageNumber,
            PageSize,
            disposeCancellation.Token);

        if (result.IsSuccess)
        {
            readingPage = result.Value;

            return;
        }

        readingPage = null;
        failure = result.Failure;
    }

    private async Task GoToPageAsync(int target)
    {
        if (target < 1 || target > TotalPages || target == pageNumber)
        {
            return;
        }

        pageNumber = target;

        try
        {
            await LoadReadingsAsync();
        }
        catch (OperationCanceledException) when (disposeCancellation.IsCancellationRequested)
        {
        }
    }

    private void StartProfileEdit()
    {
        ResetProfileForm();

        if (Profile is { } profile)
        {
            profileModel.MinimumCelsius = profile.MinimumCelsius;
            profileModel.MaximumCelsius = profile.MaximumCelsius;
            profileModel.TargetCelsius = profile.TargetCelsius;
        }

        isEditingProfile = true;
    }

    private void CancelProfileEdit()
    {
        isEditingProfile = false;
        ResetProfileForm();
    }

    private async Task SubmitProfileAsync()
    {
        if (isSavingProfile)
        {
            return;
        }

        profileMessages.Clear();
        profileError = null;

        if (!profileEditContext.Validate())
        {
            return;
        }

        if (HasProfile && string.IsNullOrWhiteSpace(profileModel.Reason))
        {
            profileMessages.Add(
                new FieldIdentifier(profileModel, nameof(ProfileFormModel.Reason)),
                "A reason is required when the thresholds change.");
            profileEditContext.NotifyValidationStateChanged();

            return;
        }

        isSavingProfile = true;

        try
        {
            var result = await CargoApi.SaveReeferProfileAsync(
                ContainerId,
                new SaveReeferProfileWebRequest(
                    profileModel.MinimumCelsius!.Value,
                    profileModel.MaximumCelsius!.Value,
                    profileModel.TargetCelsius!.Value,
                    Trimmed(profileModel.Reason)),
                disposeCancellation.Token);

            if (!result.IsSuccess)
            {
                CargoFormFailure.Apply(
                    result.Failure!,
                    profileEditContext,
                    profileMessages,
                    ProfileFieldMap,
                    message => profileError = message);

                if (result.Failure!.IsConflict || result.Failure!.IsNotFound)
                {
                    await LoadAsync();
                }

                return;
            }

            isEditingProfile = false;
            ResetProfileForm();

            await LoadAsync();
            await OnChanged.InvokeAsync();
        }
        catch (OperationCanceledException) when (disposeCancellation.IsCancellationRequested)
        {
        }
        finally
        {
            isSavingProfile = false;
        }
    }

    private void StartReading()
    {
        ResetReadingForm();

        isRecordingReading = true;
    }

    private void CancelReading()
    {
        isRecordingReading = false;
        ResetReadingForm();
    }

    private void StartCorrection(ReeferReadingRow reading)
    {
        ResetReadingForm();
        isRecordingReading = false;

        correctingReadingId = reading.Id;
        readingModel.TemperatureCelsius = reading.TemperatureCelsius;
        readingModel.MeasuredAtUtc = reading.MeasuredAtUtc;
        readingModel.Notes = reading.Notes;
    }

    private void CancelCorrection()
    {
        correctingReadingId = null;
        ResetReadingForm();
    }

    private async Task SubmitReadingAsync()
    {
        if (isSavingReading)
        {
            return;
        }

        readingMessages.Clear();
        readingError = null;

        if (!readingEditContext.Validate())
        {
            return;
        }

        if (correctingReadingId is not null && string.IsNullOrWhiteSpace(readingModel.Reason))
        {
            readingMessages.Add(
                new FieldIdentifier(readingModel, nameof(ReadingFormModel.Reason)),
                "A correction reason is required.");
            readingEditContext.NotifyValidationStateChanged();

            return;
        }

        isSavingReading = true;

        try
        {
            var result = correctingReadingId is { } readingId
                ? await CargoApi.CorrectReeferReadingAsync(
                    ContainerId,
                    readingId,
                    new CorrectReeferReadingWebRequest(
                        readingModel.TemperatureCelsius!.Value,
                        readingModel.MeasuredAtUtc!.Value,
                        Trimmed(readingModel.Notes),
                        readingModel.Reason!.Trim()),
                    disposeCancellation.Token)
                : await CargoApi.RecordReeferReadingAsync(
                    ContainerId,
                    new RecordReeferReadingWebRequest(
                        readingModel.TemperatureCelsius!.Value,
                        readingModel.MeasuredAtUtc!.Value,
                        Trimmed(readingModel.Notes)),
                    disposeCancellation.Token);

            if (!result.IsSuccess)
            {
                CargoFormFailure.Apply(
                    result.Failure!,
                    readingEditContext,
                    readingMessages,
                    ReadingFieldMap,
                    message => readingError = message);

                if (result.Failure!.IsConflict || result.Failure!.IsNotFound)
                {
                    await LoadAsync();
                }

                return;
            }

            correctingReadingId = null;
            isRecordingReading = false;
            ResetReadingForm();
            pageNumber = 1;

            await LoadAsync();
            await OnChanged.InvokeAsync();
        }
        catch (OperationCanceledException) when (disposeCancellation.IsCancellationRequested)
        {
        }
        finally
        {
            isSavingReading = false;
        }
    }

    private void StartVoid(ReeferReadingRow reading)
    {
        voidingReadingId = reading.Id;
        voidReason = null;
        voidError = null;
    }

    private void CancelVoid()
    {
        voidingReadingId = null;
        voidReason = null;
        voidError = null;
    }

    private async Task ConfirmVoidAsync()
    {
        if (isVoiding || voidingReadingId is not { } readingId)
        {
            return;
        }

        voidError = null;

        if (string.IsNullOrWhiteSpace(voidReason))
        {
            voidError = "A void reason is required.";

            return;
        }

        isVoiding = true;

        try
        {
            var result = await CargoApi.VoidReeferReadingAsync(
                ContainerId,
                readingId,
                new VoidReeferReadingWebRequest(voidReason.Trim()),
                disposeCancellation.Token);

            if (!result.IsSuccess)
            {
                var voidFailure = result.Failure!;

                voidError = voidFailure.Errors.TryGetValue("reason", out var messages)
                    ? string.Join(" ", messages)
                    : $"{voidFailure.Title} {voidFailure.Message}".Trim();

                if (voidFailure.IsConflict || voidFailure.IsNotFound)
                {
                    await LoadAsync();
                }

                return;
            }

            CancelVoid();

            await LoadAsync();
            await OnChanged.InvokeAsync();
        }
        catch (OperationCanceledException) when (disposeCancellation.IsCancellationRequested)
        {
        }
        finally
        {
            isVoiding = false;
        }
    }

    private void StartResolution(ReeferExcursionRow excursion)
    {
        ResetResolutionForm();

        resolvingExcursionId = excursion.Id;
    }

    private void CancelResolution()
    {
        resolvingExcursionId = null;
        ResetResolutionForm();
    }

    private async Task SubmitResolutionAsync()
    {
        if (isResolving || resolvingExcursionId is not { } excursionId)
        {
            return;
        }

        resolutionMessages.Clear();
        resolutionError = null;

        if (!resolutionEditContext.Validate())
        {
            return;
        }

        isResolving = true;

        try
        {
            var result = await CargoApi.ResolveReeferExcursionAsync(
                ContainerId,
                excursionId,
                new ResolveReeferExcursionWebRequest(
                    resolutionModel.Disposition!,
                    resolutionModel.Explanation!.Trim()),
                disposeCancellation.Token);

            if (!result.IsSuccess)
            {
                CargoFormFailure.Apply(
                    result.Failure!,
                    resolutionEditContext,
                    resolutionMessages,
                    ResolutionFieldMap,
                    message => resolutionError = message);

                if (result.Failure!.IsConflict || result.Failure!.IsNotFound)
                {
                    await LoadAsync();
                }

                return;
            }

            resolvingExcursionId = null;
            ResetResolutionForm();

            await LoadAsync();
            await OnChanged.InvokeAsync();
        }
        catch (OperationCanceledException) when (disposeCancellation.IsCancellationRequested)
        {
        }
        finally
        {
            isResolving = false;
        }
    }

    private static string? Trimmed(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static string ReadingStatusClass(string status)
    {
        return status switch
        {
            SupersededReadingStatus => "reefer-reading-superseded",
            VoidedReadingStatus => "reefer-reading-voided",
            _ => "reefer-reading-effective",
        };
    }

    private static string ExcursionStatusClass(string status)
    {
        return status switch
        {
            OpenExcursionStatus => "reefer-excursion-open",
            ResolvedExcursionStatus => "reefer-excursion-resolved",
            _ => "reefer-excursion-superseded",
        };
    }

    private static string DispositionLabel(string? disposition)
    {
        return disposition switch
        {
            CorrectiveActionDisposition => "Corrective action",
            AcceptedDeviationDisposition => "Accepted deviation",
            _ => "Not dispositioned",
        };
    }

    private void DetachValidation()
    {
        if (profileEditContext is not null)
        {
            profileEditContext.OnValidationRequested -= HandleProfileValidationRequested;
        }

        if (readingEditContext is not null)
        {
            readingEditContext.OnValidationRequested -= HandleReadingValidationRequested;
        }

        if (resolutionEditContext is not null)
        {
            resolutionEditContext.OnValidationRequested -= HandleResolutionValidationRequested;
        }
    }

    private void ResetProfileForm()
    {
        if (profileEditContext is not null)
        {
            profileEditContext.OnValidationRequested -= HandleProfileValidationRequested;
        }

        profileModel = new ProfileFormModel();
        profileEditContext = new EditContext(profileModel);
        profileMessages = new ValidationMessageStore(profileEditContext);
        profileEditContext.OnValidationRequested += HandleProfileValidationRequested;
        profileError = null;
    }

    private void ResetReadingForm()
    {
        if (readingEditContext is not null)
        {
            readingEditContext.OnValidationRequested -= HandleReadingValidationRequested;
        }

        readingModel = new ReadingFormModel();
        readingEditContext = new EditContext(readingModel);
        readingMessages = new ValidationMessageStore(readingEditContext);
        readingEditContext.OnValidationRequested += HandleReadingValidationRequested;
        readingError = null;
    }

    private void ResetResolutionForm()
    {
        if (resolutionEditContext is not null)
        {
            resolutionEditContext.OnValidationRequested -= HandleResolutionValidationRequested;
        }

        resolutionModel = new ResolutionFormModel();
        resolutionEditContext = new EditContext(resolutionModel);
        resolutionMessages = new ValidationMessageStore(resolutionEditContext);
        resolutionEditContext.OnValidationRequested += HandleResolutionValidationRequested;
        resolutionError = null;
    }

    private void HandleProfileValidationRequested(
        object? sender,
        ValidationRequestedEventArgs args)
    {
        profileMessages.Clear();
    }

    private void HandleReadingValidationRequested(
        object? sender,
        ValidationRequestedEventArgs args)
    {
        readingMessages.Clear();
    }

    private void HandleResolutionValidationRequested(
        object? sender,
        ValidationRequestedEventArgs args)
    {
        resolutionMessages.Clear();
    }

    private string? LocalTimeHint
    {
        get
        {
            if (readingModel.MeasuredAtUtc is not { } measuredAtUtc)
            {
                return null;
            }

            var local = DateTime.SpecifyKind(measuredAtUtc, DateTimeKind.Utc).ToLocalTime();

            return $"That is {local:yyyy-MM-dd HH:mm} in your local time.";
        }
    }
    private sealed class ProfileFormModel
    {
        [Required(ErrorMessage = "A minimum temperature is required.")]
        [Range(-60, 60, ErrorMessage = "Minimum must be between -60 and 60 degrees Celsius.")]
        public decimal? MinimumCelsius { get; set; }

        [Required(ErrorMessage = "A maximum temperature is required.")]
        [Range(-60, 60, ErrorMessage = "Maximum must be between -60 and 60 degrees Celsius.")]
        public decimal? MaximumCelsius { get; set; }

        [Required(ErrorMessage = "A target temperature is required.")]
        [Range(-60, 60, ErrorMessage = "Target must be between -60 and 60 degrees Celsius.")]
        public decimal? TargetCelsius { get; set; }

        [StringLength(500, ErrorMessage = "A reason must not exceed 500 characters.")]
        public string? Reason { get; set; }
    }

    private sealed class ReadingFormModel
    {
        [Required(ErrorMessage = "A temperature is required.")]
        [Range(-60, 60, ErrorMessage = "Temperature must be between -60 and 60 degrees Celsius.")]
        public decimal? TemperatureCelsius { get; set; }

        [Required(ErrorMessage = "A measurement time is required.")]
        public DateTime? MeasuredAtUtc { get; set; }

        [StringLength(1000, ErrorMessage = "Notes must not exceed 1000 characters.")]
        public string? Notes { get; set; }

        [StringLength(500, ErrorMessage = "A reason must not exceed 500 characters.")]
        public string? Reason { get; set; }
    }

    private sealed class ResolutionFormModel
    {
        [Required(ErrorMessage = "A disposition is required.")]
        public string? Disposition { get; set; }

        [Required(ErrorMessage = "An explanation is required.")]
        [StringLength(1000, ErrorMessage = "An explanation must not exceed 1000 characters.")]
        public string? Explanation { get; set; }
    }
}
