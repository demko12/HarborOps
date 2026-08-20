using System.ComponentModel.DataAnnotations;
using HarborOps.Web.Api.Cargo;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;

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

    private bool IsBlockedByMissingSafetyInfo =>
        details is { IsHazardous: true, SafetyInfo: null, NextStatus: "Loaded" };

    protected override void OnInitialized()
    {
        ResetSafetyForm();
        ResetTrackingForm();
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
    }

    private bool isDeclaringSafety;
    private string? safetyError;
    private SafetyFormModel safetyModel = new();
    private EditContext safetyEditContext = null!;
    private ValidationMessageStore safetyMessages = null!;

    private async Task SubmitSafetyDeclarationAsync()
    {
        if (isDeclaringSafety)
        {
            return;
        }

        safetyMessages.Clear();
        safetyError = null;

        if (!safetyEditContext.Validate())
        {
            return;
        }

        isDeclaringSafety = true;

        try
        {
            var request = new DeclareCargoSafetyInfoWebRequest(
                safetyModel.ImdgClass!.Trim(),
                safetyModel.UnNumber!.Trim(),
                safetyModel.StorageConditions!.Trim(),
                string.IsNullOrWhiteSpace(safetyModel.HandlingNotes) ? null : safetyModel.HandlingNotes.Trim());

            var result = await CargoApi.DeclareSafetyInformationAsync(
                BookingId,
                request,
                disposeCancellation.Token);

            if (!result.IsSuccess)
            {
                ApplyFailure(result.Failure!, safetyEditContext, safetyMessages, SafetyFieldMap, message => safetyError = message);

                return;
            }

            ResetSafetyForm();

            await LoadAsync();
        }
        catch (OperationCanceledException) when (disposeCancellation.IsCancellationRequested)
        {
        }
        finally
        {
            isDeclaringSafety = false;
        }
    }

    private void ResetSafetyForm()
    {
        safetyModel = new SafetyFormModel();
        safetyEditContext = new EditContext(safetyModel);
        safetyMessages = new ValidationMessageStore(safetyEditContext);
    }

    private static void ApplyFailure(
        CargoApiFailure failure,
        EditContext context,
        ValidationMessageStore store,
        IReadOnlyDictionary<string, string> fieldMap,
        Action<string> setGeneral)
    {
        var unmappedMessages = new List<string>();

        foreach (var entry in failure.Errors)
        {
            if (!fieldMap.TryGetValue(entry.Key, out var property))
            {
                unmappedMessages.AddRange(entry.Value);

                continue;
            }

            store.Add(new FieldIdentifier(context.Model, property), entry.Value);
        }

        if (unmappedMessages.Count > 0)
        {
            setGeneral(string.Join(" ", unmappedMessages));
        }
        else if (failure.Errors.Count == 0)
        {
            setGeneral($"{failure.Title} {failure.Message}".Trim());
        }

        context.NotifyValidationStateChanged();
    }

    private static readonly IReadOnlyDictionary<string, string> SafetyFieldMap =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["imdgClass"] = nameof(SafetyFormModel.ImdgClass),
            ["unNumber"] = nameof(SafetyFormModel.UnNumber),
            ["storageConditions"] = nameof(SafetyFormModel.StorageConditions),
            ["handlingNotes"] = nameof(SafetyFormModel.HandlingNotes),
        };

    private sealed class SafetyFormModel
    {
        [Required(ErrorMessage = "IMDG class is required.")]
        [StringLength(10, ErrorMessage = "IMDG class must not exceed 10 characters.")]
        public string? ImdgClass { get; set; }

        [Required(ErrorMessage = "UN number is required.")]
        [StringLength(10, ErrorMessage = "UN number must not exceed 10 characters.")]
        public string? UnNumber { get; set; }

        [Required(ErrorMessage = "Storage conditions are required.")]
        [StringLength(500, ErrorMessage = "Storage conditions must not exceed 500 characters.")]
        public string? StorageConditions { get; set; }

        [StringLength(1000, ErrorMessage = "Handling notes must not exceed 1000 characters.")]
        public string? HandlingNotes { get; set; }
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
                ApplyFailure(result.Failure!, trackingEditContext, trackingMessages, TrackingFieldMap, message => trackingError = message);

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

    public void Dispose()
    {
        disposeCancellation.Cancel();
        disposeCancellation.Dispose();
    }
}
