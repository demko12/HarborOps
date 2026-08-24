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
}
