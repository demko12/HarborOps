using System.Globalization;
using HarborOps.Web.Api;
using HarborOps.Web.Api.Fleet;
using Microsoft.AspNetCore.Components;

namespace HarborOps.Web.Components.Pages.Fleet;

public partial class VesselDetails : IDisposable
{

    private readonly CancellationTokenSource pageCancellation = new();
    private int loadVersion;

    private VesselDetailsWebModel? details;
    private bool isLoading = true;
    private bool notFound;
    private FleetApiFailure? loadFailure;

    private string selectedStatus = FleetStatusOptions.All[0];
    private string? notes;
    private bool isSubmitting;
    private FleetApiFailure? submitFailure;
    private string? submitSuccessMessage;
    private FleetDirectoryWebModel? fleets;
    private FleetAssignmentHistoryWebModel? assignmentHistory;
    private Guid? selectedFleetId;
    private bool isAssigning;
    private FleetApiFailure? assignmentFailure;
    private string? pendingAssignmentAction;

    private IReadOnlyList<VesselTypeWebModel> vesselTypes = [];

    private readonly RegistryFormModel registryForm = new();
    private RegistryFormModel registrySnapshot = new();
    private bool isEditingRegistry;
    private bool isSavingRegistry;
    private FleetApiFailure? registryFailure;
    private string? registrySuccessMessage;
    private bool confirmRegistryCancel;

    private readonly SpecificationsFormModel specificationsForm = new();
    private SpecificationsFormModel specificationsSnapshot = new();
    private bool isEditingSpecifications;
    private bool isSavingSpecifications;
    private FleetApiFailure? specificationsFailure;
    private string? specificationsSuccessMessage;
    private bool confirmSpecificationsCancel;

    [Parameter]
    public Guid VesselId { get; set; }

    [SupplyParameterFromQuery(Name = "returnTo")]
    [Parameter]
    public string? ReturnTo { get; set; }

    private bool CanManageFleet => AuthorizationState.HasPermission(CurrentUserAuthorizationState.FleetManagePermission);

    private string FleetHref => !string.IsNullOrWhiteSpace(ReturnTo)
        && ReturnTo.StartsWith("/fleet", StringComparison.Ordinal)
        ? ReturnTo
        : "/fleet";

    protected override async Task OnInitializedAsync() => await LoadDetailsAsync();

    private async Task LoadDetailsAsync()
    {
        var requestVersion = ++loadVersion;
        var cancellationToken = pageCancellation.Token;

        isLoading = true;
        notFound = false;
        loadFailure = null;

        FleetApiResult<VesselDetailsWebModel> result;
        try
        {
            result = await FleetApi.GetVesselDetailsAsync(VesselId, cancellationToken);
        }
        catch (OperationCanceledException) when (pageCancellation.IsCancellationRequested)
        {
            return;
        }

        if (pageCancellation.IsCancellationRequested || requestVersion != loadVersion)
        {
            return;
        }

        if (result.IsSuccess)
        {
            details = result.Value;
            selectedStatus = details is not null && FleetStatusOptions.All.Contains(details.CurrentStatus)
                ? details.CurrentStatus
                : FleetStatusOptions.All[0];
            await LoadAssignmentsAsync();

            if (CanManageFleet)
            {
                await LoadVesselTypesAsync();
            }
        }
        else if (result.Failure is { IsNotFound: true })
        {
            notFound = true;
        }
        else
        {
            loadFailure = result.Failure;
        }

        isLoading = false;
    }

    private async Task LoadAssignmentsAsync()
    {
        var directoryResult = await FleetApi.GetFleetsAsync(null, 1, 100, pageCancellation.Token);
        if (directoryResult.IsSuccess) fleets = directoryResult.Value;
        var historyResult = await FleetApi.GetFleetAssignmentHistoryAsync(VesselId, pageCancellation.Token);
        if (historyResult.IsSuccess) assignmentHistory = historyResult.Value;
    }

    private async Task LoadVesselTypesAsync()
    {
        var result = await FleetApi.GetVesselTypesAsync(pageCancellation.Token);
        if (result.IsSuccess && result.Value?.VesselTypes is { } types)
        {
            vesselTypes = types;
        }
    }

    private async Task AssignFleetAsync()
    {
        if (isAssigning || selectedFleetId is null) return;
        isAssigning = true; assignmentFailure = null;
        try
        {
            var result = await FleetApi.AssignFleetAsync(VesselId, new AssignFleetWebRequest(selectedFleetId.Value), pageCancellation.Token);
            if (result.IsSuccess) { selectedFleetId = null; await LoadDetailsAsync(); } else assignmentFailure = result.Failure;
        }
        finally { isAssigning = false; }
    }

    private async Task EndAssignmentAsync()
    {
        if (isAssigning) return;
        isAssigning = true; assignmentFailure = null;
        try
        {
            var result = await FleetApi.EndFleetAssignmentAsync(VesselId, pageCancellation.Token);
            if (result.IsSuccess) await LoadDetailsAsync(); else assignmentFailure = result.Failure;
        }
        finally { isAssigning = false; }
    }

    private void BeginAssignConfirmation()
    {
        if (selectedFleetId is not null) pendingAssignmentAction = "assign";
    }

    private void BeginEndConfirmation() => pendingAssignmentAction = "end";

    private async Task ConfirmAssignmentAsync()
    {
        var action = pendingAssignmentAction;
        pendingAssignmentAction = null;
        if (action == "end") await EndAssignmentAsync(); else if (action == "assign") await AssignFleetAsync();
    }

    private void CancelAssignmentConfirmation() => pendingAssignmentAction = null;

    private async Task SubmitStatusChangeAsync()
    {
        if (isSubmitting || details is null)
        {
            return;
        }

        isSubmitting = true;
        submitFailure = null;
        submitSuccessMessage = null;

        try
        {
            var request = new RecordVesselStatusChangeWebRequest(
                selectedStatus,
                string.IsNullOrWhiteSpace(notes) ? null : notes);

            var result = await FleetApi.RecordStatusChangeAsync(VesselId, request, pageCancellation.Token);

            if (pageCancellation.IsCancellationRequested)
            {
                return;
            }

            if (!result.IsSuccess || result.Value is null)
            {
                submitFailure = result.Failure;
                return;
            }

            notes = null;
            submitSuccessMessage = $"Status updated to {result.Value.Status}.";

            await LoadDetailsAsync();
        }
        finally
        {
            isSubmitting = false;
        }
    }

    private void BeginRegistryEdit()
    {
        if (details is null)
        {
            return;
        }

        registryForm.VesselTypeId = details.VesselTypeId;
        registryForm.Name = details.Name;
        registryForm.Mmsi = details.Mmsi;
        registryForm.CallSign = details.CallSign;
        registryForm.FlagState = details.FlagState;
        registryForm.PortOfRegistry = details.PortOfRegistry;
        registryForm.ClassificationSociety = details.ClassificationSociety;
        registryForm.LifecycleStage = details.LifecycleStage;
        registryForm.MsmdId = details.MsmdId;

        registrySnapshot = registryForm.Clone();
        isEditingRegistry = true;
        isEditingSpecifications = false;
        confirmRegistryCancel = false;
        registryFailure = null;
        registrySuccessMessage = null;
        specificationsSuccessMessage = null;
    }

    private void CancelRegistryEdit()
    {
        if (!confirmRegistryCancel && !registryForm.Matches(registrySnapshot))
        {
            confirmRegistryCancel = true;
            return;
        }

        isEditingRegistry = false;
        confirmRegistryCancel = false;
        registryFailure = null;
    }

    private void KeepEditingRegistry() => confirmRegistryCancel = false;

    private async Task SubmitRegistryAsync()
    {
        if (isSavingRegistry || details is null)
        {
            return;
        }

        isSavingRegistry = true;
        registryFailure = null;
        registrySuccessMessage = null;
        confirmRegistryCancel = false;

        try
        {
            var request = new UpdateVesselRegistryDetailsWebRequest(
                registryForm.VesselTypeId,
                registryForm.Name,
                NullIfBlank(registryForm.Mmsi),
                NullIfBlank(registryForm.CallSign),
                registryForm.FlagState,
                NullIfBlank(registryForm.PortOfRegistry),
                NullIfBlank(registryForm.ClassificationSociety),
                NullIfBlank(registryForm.LifecycleStage),
                NullIfBlank(registryForm.MsmdId));

            var result = await FleetApi.UpdateVesselRegistryDetailsAsync(VesselId, request, pageCancellation.Token);

            if (pageCancellation.IsCancellationRequested)
            {
                return;
            }

            if (!result.IsSuccess || result.Value is null)
            {
                registryFailure = result.Failure;
                return;
            }

            isEditingRegistry = false;
            registrySuccessMessage = "Registry details updated.";

            await LoadDetailsAsync();
        }
        finally
        {
            isSavingRegistry = false;
        }
    }

    private void BeginSpecificationsEdit()
    {
        if (details is null)
        {
            return;
        }

        specificationsForm.Dwt = details.Dwt;
        specificationsForm.Gt = details.Gt;
        specificationsForm.Loa = details.Loa;
        specificationsForm.Beam = details.Beam;
        specificationsForm.Draft = details.Draft;

        specificationsSnapshot = specificationsForm.Clone();
        isEditingSpecifications = true;
        isEditingRegistry = false;
        confirmSpecificationsCancel = false;
        specificationsFailure = null;
        specificationsSuccessMessage = null;
        registrySuccessMessage = null;
    }

    private void CancelSpecificationsEdit()
    {
        if (!confirmSpecificationsCancel && !specificationsForm.Matches(specificationsSnapshot))
        {
            confirmSpecificationsCancel = true;
            return;
        }

        isEditingSpecifications = false;
        confirmSpecificationsCancel = false;
        specificationsFailure = null;
    }

    private void KeepEditingSpecifications() => confirmSpecificationsCancel = false;

    private async Task SubmitSpecificationsAsync()
    {
        if (isSavingSpecifications || details is null)
        {
            return;
        }

        isSavingSpecifications = true;
        specificationsFailure = null;
        specificationsSuccessMessage = null;
        confirmSpecificationsCancel = false;

        try
        {
            var request = new UpdateVesselSpecificationsWebRequest(
                specificationsForm.Dwt,
                specificationsForm.Gt,
                specificationsForm.Loa,
                specificationsForm.Beam,
                specificationsForm.Draft);

            var result = await FleetApi.UpdateVesselSpecificationsAsync(VesselId, request, pageCancellation.Token);

            if (pageCancellation.IsCancellationRequested)
            {
                return;
            }

            if (!result.IsSuccess || result.Value is null)
            {
                specificationsFailure = result.Failure;
                return;
            }

            isEditingSpecifications = false;
            specificationsSuccessMessage = "Specifications updated.";

            await LoadDetailsAsync();
        }
        finally
        {
            isSavingSpecifications = false;
        }
    }

    private static string? NullIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    internal static string GetStatusClass(string status) => status.ToLowerInvariant() switch
    {
        "active" => "vessel-status-active",
        "en route" => "vessel-status-transit",
        "maintenance" => "vessel-status-maintenance",
        "at port" => "vessel-status-port",
        _ => "vessel-status-neutral",
    };

    internal static string GetStatusIcon(string status) => status.ToLowerInvariant() switch
    {
        "active" => "bi-check2-circle",
        "en route" => "bi-compass",
        "at port" => "bi-geo-alt",
        "maintenance" => "bi-tools",
        _ => "bi-info-circle",
    };

    internal static string FormatMeasurement(decimal? value) => value is null
        ? "—"
        : value.Value.ToString("0.##", CultureInfo.InvariantCulture);

    private static string FormatTimestamp(DateTime timestamp) =>
        timestamp.ToLocalTime().ToString("dd MMM yyyy, HH:mm", CultureInfo.InvariantCulture);

    public void Dispose()
    {
        pageCancellation.Cancel();
        pageCancellation.Dispose();
    }

    private sealed class RegistryFormModel
    {
        public Guid? VesselTypeId { get; set; }

        public string? Name { get; set; }

        public string? Mmsi { get; set; }

        public string? CallSign { get; set; }

        public string? FlagState { get; set; }

        public string? PortOfRegistry { get; set; }

        public string? ClassificationSociety { get; set; }

        public string? LifecycleStage { get; set; }

        public string? MsmdId { get; set; }

        public RegistryFormModel Clone() => (RegistryFormModel)MemberwiseClone();

        public bool Matches(RegistryFormModel other) =>
            VesselTypeId == other.VesselTypeId
            && string.Equals(Name, other.Name, StringComparison.Ordinal)
            && string.Equals(Mmsi, other.Mmsi, StringComparison.Ordinal)
            && string.Equals(CallSign, other.CallSign, StringComparison.Ordinal)
            && string.Equals(FlagState, other.FlagState, StringComparison.Ordinal)
            && string.Equals(PortOfRegistry, other.PortOfRegistry, StringComparison.Ordinal)
            && string.Equals(ClassificationSociety, other.ClassificationSociety, StringComparison.Ordinal)
            && string.Equals(LifecycleStage, other.LifecycleStage, StringComparison.Ordinal)
            && string.Equals(MsmdId, other.MsmdId, StringComparison.Ordinal);
    }

    private sealed class SpecificationsFormModel
    {
        public decimal? Dwt { get; set; }

        public decimal? Gt { get; set; }

        public decimal? Loa { get; set; }

        public decimal? Beam { get; set; }

        public decimal? Draft { get; set; }

        public SpecificationsFormModel Clone() => (SpecificationsFormModel)MemberwiseClone();

        public bool Matches(SpecificationsFormModel other) =>
            Dwt == other.Dwt
            && Gt == other.Gt
            && Loa == other.Loa
            && Beam == other.Beam
            && Draft == other.Draft;
    }
}
