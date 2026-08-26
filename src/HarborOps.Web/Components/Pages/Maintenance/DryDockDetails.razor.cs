using System.ComponentModel.DataAnnotations;
using System.Globalization;
using HarborOps.Web.Api.Maintenance;
using Microsoft.AspNetCore.Components;

namespace HarborOps.Web.Components.Pages.Maintenance;

public partial class DryDockDetails : IDisposable
{
    private const string StartStatus = "InProgress";
    private const string CompleteStatus = "Completed";
    private const string CancelStatus = "Canceled";

    private readonly CancellationTokenSource disposeCancellation = new();
    private readonly CompleteDryDockModel completeModel = new();

    private DryDockDetailsModel? details;
    private MaintenanceApiFailure? failure;
    private MaintenanceApiFailure? actionFailure;
    private bool isLoading = true;
    private bool isActing;
    private Guid loadedDryDockId;

    private string? vesselName;
    private string? portName;

    private string? pendingAction;
    private bool isCompleteFormOpen;

    [Parameter]
    public Guid DryDockId { get; set; }

    [SupplyParameterFromQuery(Name = "from")]
    public string? BoardQuery { get; set; }

    private bool CanManage => AuthorizationState.HasPermission("maintenance.manage");

    private string PageHeading => details is null ? "Dry dock" : VesselDisplayName;

    private int LifecycleStep => details is null
        ? 0
        : MaintenanceFormat.DryDockLifecycleStep(details.Status);

    private string LifecycleLabel =>
        $"Step {LifecycleStep.ToString(CultureInfo.InvariantCulture)} of " +
        $"{MaintenanceFormat.DryDockLifecycleStepCount.ToString(CultureInfo.InvariantCulture)}";

    private string BackHref => string.IsNullOrWhiteSpace(BoardQuery)
        ? "/maintenance"
        : $"/maintenance?{BoardQuery}";

    private string VesselDisplayName =>
        vesselName ?? MaintenanceFormat.UnknownVessel();

    private string PortDisplayName =>
        portName ?? MaintenanceFormat.ShortPortReference(details?.PortId ?? Guid.Empty);

    private string StateSummary
    {
        get
        {
            if (details is not { } loaded)
            {
                return string.Empty;
            }

            if (string.Equals(loaded.Status, CompleteStatus, StringComparison.Ordinal))
            {
                return "The work is finished and the record is closed.";
            }

            if (string.Equals(loaded.Status, CancelStatus, StringComparison.Ordinal))
            {
                return "The dry dock was called off before any work began.";
            }

            if (string.Equals(loaded.Status, StartStatus, StringComparison.Ordinal))
            {
                return "The vessel is under work.";
            }

            if (loaded.IsOverdue)
            {
                var late = -(loaded.DaysUntilStart ?? 0);

                return late == 1
                    ? "The planned start was 1 day ago."
                    : $"The planned start was {late.ToString(CultureInfo.InvariantCulture)} days ago.";
            }

            return loaded.DaysUntilStart == 0
                ? "The vessel is due in the dock today."
                : "The vessel has not entered the dock yet.";
        }
    }

    private string ClosedSummary
    {
        get
        {
            if (details is not { } loaded)
            {
                return string.Empty;
            }

            return string.Equals(loaded.Status, CompleteStatus, StringComparison.Ordinal)
                ? $"Completed on {MaintenanceFormat.EndDate(loaded.EndDate)}. "
                    + $"Actual cost {MaintenanceFormat.ActualCost(loaded.ActualCost)}."
                : "Canceled. The plan is kept for history — schedule a new dry dock if the work is still needed.";
        }
    }

    private bool CanStart => Offers(StartStatus);

    private bool CanComplete => Offers(CompleteStatus);

    private bool CanCancel => Offers(CancelStatus);

    private bool Offers(string status)
    {
        return CanManage
            && details is { } loaded
            && loaded.AllowedNextStatuses.Contains(status, StringComparer.Ordinal);
    }

    protected override Task OnParametersSetAsync()
    {
        if (loadedDryDockId == DryDockId && !isLoading)
        {
            return Task.CompletedTask;
        }

        loadedDryDockId = DryDockId;

        return LoadAsync();
    }

    private Task RetryAsync()
    {
        return LoadAsync();
    }

    private async Task LoadAsync()
    {
        var requestedId = DryDockId;
        isLoading = true;
        failure = null;

        MaintenanceApiResult<DryDockDetailsModel> result;
        try
        {
            result = await MaintenanceApi.GetDryDockDetailsAsync(requestedId, disposeCancellation.Token);
        }
        catch (OperationCanceledException) when (disposeCancellation.IsCancellationRequested)
        {
            return;
        }

        if (disposeCancellation.IsCancellationRequested || requestedId != DryDockId)
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

        await ResolveDisplayNamesAsync(result.Value!, requestedId);
    }

    private async Task ResolveDisplayNamesAsync(DryDockDetailsModel loaded, Guid requestedId)
    {
        var vessels = await VesselLookupApi.LookupVesselNamesAsync(
            [loaded.VesselId],
            disposeCancellation.Token);
        var ports = await VoyagesApi.LookupPortNamesAsync([loaded.PortId], disposeCancellation.Token);

        if (disposeCancellation.IsCancellationRequested || requestedId != DryDockId)
        {
            return;
        }

        vesselName = vessels.TryGetValue(loaded.VesselId, out var vessel) ? vessel : null;
        portName = ports.TryGetValue(loaded.PortId, out var port) ? port : null;
    }

    private void AskToConfirm(string action)
    {
        actionFailure = null;
        pendingAction = action;
    }

    private void DismissConfirmation()
    {
        if (isActing)
        {
            return;
        }

        pendingAction = null;
    }

    private Task ConfirmPendingActionAsync()
    {
        return pendingAction switch
        {
            StartStatus => RunActionAsync(token => MaintenanceApi.StartDryDockAsync(DryDockId, token)),
            CancelStatus => RunActionAsync(token => MaintenanceApi.CancelDryDockAsync(DryDockId, token)),
            _ => Task.CompletedTask,
        };
    }

    private void OpenCompleteForm()
    {
        actionFailure = null;
        completeModel.Reset();
        isCompleteFormOpen = true;
    }

    private void CloseCompleteForm()
    {
        if (isActing)
        {
            return;
        }

        actionFailure = null;
        isCompleteFormOpen = false;
    }

    private async Task SubmitCompletionAsync()
    {
        if (isActing)
        {
            return;
        }

        actionFailure = null;

        if (!completeModel.TryBuildRequest(out var request, out var localErrors))
        {
            actionFailure = new MaintenanceApiFailure(
                StatusCodes.Status400BadRequest,
                "One or more validation errors occurred.",
                "Please review the values and try again.",
                localErrors);

            return;
        }

        await RunActionAsync(token => MaintenanceApi.CompleteDryDockAsync(DryDockId, request, token));
    }

    private async Task RunActionAsync(
        Func<CancellationToken, Task<MaintenanceApiResult<DryDockTransition>>> action)
    {
        isActing = true;

        try
        {
            var result = await action(disposeCancellation.Token);

            if (!result.IsSuccess)
            {
                actionFailure = result.Failure;

                if (result.Failure!.IsConflict)
                {
                    pendingAction = null;
                    isCompleteFormOpen = false;

                    await LoadAsync();
                }

                return;
            }

            pendingAction = null;
            isCompleteFormOpen = false;
            completeModel.Reset();

            await LoadAsync();
        }
        catch (OperationCanceledException) when (disposeCancellation.IsCancellationRequested)
        {
        }
        finally
        {
            isActing = false;
        }
    }

    private static string ConfirmationQuestion(string action)
    {
        return action switch
        {
            StartStatus => "Start this dry dock? The vessel is recorded as being under work.",
            CancelStatus => "Cancel this dry dock? The planned record is kept for history.",
            _ => "Continue?",
        };
    }

    private sealed class CompleteDryDockModel
    {
        internal DateOnly? EndDate { get; set; }

        [Range(0, double.MaxValue)]
        internal decimal? ActualCost { get; set; }

        internal void Reset()
        {
            EndDate = null;
            ActualCost = null;
        }

        internal bool TryBuildRequest(
            out CompleteDryDockWebRequest request,
            out IReadOnlyDictionary<string, string[]> errors)
        {
            var found = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);

            if (EndDate is null)
            {
                found["endDate"] = ["Required."];
            }

            if (ActualCost is null)
            {
                found["actualCost"] = ["Required."];
            }
            else if (ActualCost.Value < 0m)
            {
                found["actualCost"] = ["Must not be negative."];
            }

            errors = found;

            if (found.Count > 0)
            {
                request = null!;

                return false;
            }

            request = new CompleteDryDockWebRequest(EndDate!.Value, ActualCost!.Value);

            return true;
        }
    }

    public void Dispose()
    {
        disposeCancellation.Cancel();
        disposeCancellation.Dispose();
    }
}
