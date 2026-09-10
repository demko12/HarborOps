using HarborOps.Web.Api.Maintenance;
using Microsoft.AspNetCore.Components;

namespace HarborOps.Web.Components.Pages.Maintenance;

public partial class RegisterDefect : IDisposable
{
    private const int EquipmentPickerPageSize = 100;

    private static readonly IReadOnlyList<string> Severities = ["Low", "Medium", "High", "Critical"];

    private static readonly IReadOnlyCollection<string> KnownFields =
        ["vesselId", "equipmentId", "title", "description", "reportedSeverity", "observedAtUtc"];

    private readonly CancellationTokenSource disposeCancellation = new();
    private readonly RegisterDefectModel model = new();

    private bool isVesselFixed;
    private string? vesselDisplayName;
    private bool isLoadingVesselName;

    private IReadOnlyList<EquipmentListItem> equipmentOptions = [];
    private bool isLoadingEquipment;
    private MaintenanceApiFailure? equipmentLoadFailure;

    private bool isSubmitting;
    private MaintenanceApiFailure? submitFailure;
    private string? successMessage;
    private Guid? registeredDefectId;

    [SupplyParameterFromQuery(Name = "vesselId")]
    [Parameter]
    public Guid? VesselId { get; set; }

    private bool CanManage => AuthorizationState.HasPermission("maintenance.manage");

    private bool IsFutureObservedAtUtc =>
        model.ObservedAtUtcLocal is { } observed && observed > DateTime.UtcNow;

    private bool CanSubmit =>
        !isSubmitting
        && model.VesselId is not null
        && !string.IsNullOrWhiteSpace(model.Title)
        && !string.IsNullOrWhiteSpace(model.ReportedSeverity)
        && model.ObservedAtUtcLocal is not null;

    private string CancelHref => isVesselFixed && model.VesselId is { } vesselId
        ? $"/fleet/{vesselId}"
        : "/maintenance";

    protected override async Task OnInitializedAsync()
    {
        if (VesselId is { } vesselId)
        {
            isVesselFixed = true;
            model.VesselId = vesselId;

            await ResolveVesselNameAsync(vesselId);
            await LoadEquipmentAsync(vesselId);
        }
    }

    private async Task ResolveVesselNameAsync(Guid vesselId)
    {
        isLoadingVesselName = true;

        var names = await FleetApi.LookupVesselNamesAsync([vesselId], disposeCancellation.Token);

        isLoadingVesselName = false;
        vesselDisplayName = names.TryGetValue(vesselId, out var name) ? name : MaintenanceFormat.UnknownVessel();
    }

    private async Task OnVesselSelectedAsync(Guid? vesselId)
    {
        model.VesselId = vesselId;
        model.EquipmentId = null;
        equipmentOptions = [];
        equipmentLoadFailure = null;

        if (vesselId is { } selected)
        {
            await LoadEquipmentAsync(selected);
        }
    }

    private async Task LoadEquipmentAsync(Guid vesselId)
    {
        isLoadingEquipment = true;
        equipmentLoadFailure = null;

        try
        {
            var result = await MaintenanceApi.GetEquipmentAsync(
                new EquipmentFilter(vesselId, null, null, null, 1, EquipmentPickerPageSize),
                disposeCancellation.Token);

            if (disposeCancellation.IsCancellationRequested)
            {
                return;
            }

            if (!result.IsSuccess)
            {
                equipmentLoadFailure = result.Failure;
                equipmentOptions = [];

                return;
            }

            equipmentOptions = result.Value!.Items;
        }
        catch (OperationCanceledException) when (disposeCancellation.IsCancellationRequested)
        {
        }
        finally
        {
            isLoadingEquipment = false;
        }
    }

    private Task RetryEquipmentAsync()
    {
        return model.VesselId is { } vesselId ? LoadEquipmentAsync(vesselId) : Task.CompletedTask;
    }

    private async Task SubmitAsync()
    {
        if (!CanSubmit)
        {
            return;
        }

        isSubmitting = true;
        submitFailure = null;
        successMessage = null;

        try
        {
            var request = new RegisterDefectRequest
            {
                VesselId = model.VesselId,
                EquipmentId = model.EquipmentId,
                Title = model.Title,
                Description = string.IsNullOrWhiteSpace(model.Description) ? null : model.Description,
                ReportedSeverity = model.ReportedSeverity,
                ObservedAtUtc = model.ObservedAtUtcLocal is { } observed
                    ? DateTime.SpecifyKind(observed, DateTimeKind.Utc)
                    : null,
            };

            var result = await MaintenanceApi.RegisterDefectAsync(request, disposeCancellation.Token);

            if (disposeCancellation.IsCancellationRequested)
            {
                return;
            }

            if (!result.IsSuccess || result.Value is null)
            {
                submitFailure = result.Failure;

                return;
            }

            successMessage = "Defect reported successfully.";
            registeredDefectId = result.Value.Id;
        }
        catch (OperationCanceledException) when (disposeCancellation.IsCancellationRequested)
        {
        }
        finally
        {
            isSubmitting = false;
        }
    }

    private IReadOnlyList<string> FieldErrors(string field)
    {
        return submitFailure?.Errors.TryGetValue(field, out var messages) == true ? messages : [];
    }

    private IReadOnlyList<string> UnmappedFieldErrors()
    {
        if (submitFailure is not { IsValidation: true } || submitFailure.Errors.Count == 0)
        {
            return [];
        }

        return submitFailure.Errors
            .Where(entry => !KnownFields.Contains(entry.Key))
            .SelectMany(entry => entry.Value)
            .ToList();
    }

    public void Dispose()
    {
        disposeCancellation.Cancel();
        disposeCancellation.Dispose();
    }

    private sealed class RegisterDefectModel
    {
        public Guid? VesselId { get; set; }

        public Guid? EquipmentId { get; set; }

        public string? Title { get; set; }

        public string? Description { get; set; }

        public string? ReportedSeverity { get; set; }

        public DateTime? ObservedAtUtcLocal { get; set; }
    }
}
