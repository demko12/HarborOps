using HarborOps.Web.Api;

namespace HarborOps.Web.Components.Pages.Crew;

public partial class CrewRotations : IDisposable
{
    private const int PageSize = 25;

    private readonly CancellationTokenSource disposeCancellation = new();
    private int loadVersion;

    private Guid? vesselIdFilter;
    private int vesselPickerResetToken;
    private DateOnly? fromFilter;
    private DateOnly? toFilter;
    private string? overdueSelection;

    private bool? OverdueOnlyFilter => overdueSelection == "true" ? true : null;

    private int pageNumber = 1;
    private int totalCount;
    private bool isLoading = true;
    private bool cardView = true;
    private CrewApiFailure? failure;
    private string? filterValidationMessage;

    private IReadOnlyList<CrewRotationListItemResponse> rotations = [];
    private IReadOnlyDictionary<Guid, string> vesselNames = new Dictionary<Guid, string>();
    private readonly HashSet<Guid> expandedCardIds = [];
    private Guid? pendingCancellationId;
    private bool isCancellingRotation;

    private bool CanManage => CurrentUser.HasPermission(CurrentUserAuthorizationState.CrewManagePermission);

    private int TotalPages => totalCount == 0 ? 1 : (totalCount + PageSize - 1) / PageSize;
    private int FirstRowNumber => rotations.Count == 0 ? 0 : ((pageNumber - 1) * PageSize) + 1;
    private int LastRowNumber => FirstRowNumber + rotations.Count - 1;

    protected override Task OnInitializedAsync() => LoadAsync();

    private Task SearchAsync()
    {
        pageNumber = 1;

        return LoadAsync();
    }

    private Task OnVesselFilterChangedAsync(Guid? vesselId)
    {
        vesselIdFilter = vesselId;

        return SearchAsync();
    }

    private Task ResetFiltersAsync()
    {
        vesselIdFilter = null;
        vesselPickerResetToken++;
        fromFilter = null;
        toFilter = null;
        overdueSelection = null;
        pageNumber = 1;
        filterValidationMessage = null;

        return LoadAsync();
    }

    private Task PreviousPageAsync()
    {
        if (pageNumber <= 1)
        {
            return Task.CompletedTask;
        }

        pageNumber--;

        return LoadAsync();
    }

    private Task NextPageAsync()
    {
        if (pageNumber >= TotalPages)
        {
            return Task.CompletedTask;
        }

        pageNumber++;

        return LoadAsync();
    }

    private Task LoadAsync()
    {
        if (fromFilter is { } from && toFilter is { } to && to < from)
        {
            filterValidationMessage = "The \"relief to\" date must be on or after the \"relief from\" date.";

            return Task.CompletedTask;
        }

        filterValidationMessage = null;

        return LoadRotationsAsync();
    }

    private async Task LoadRotationsAsync()
    {
        var requestVersion = ++loadVersion;
        var cancellationToken = disposeCancellation.Token;
        isLoading = true;
        failure = null;

        CrewApiResult<CrewRotationsPageResponse> result;
        try
        {
            result = await CrewApi.GetRotationsAsync(
                vesselIdFilter, fromFilter, toFilter, OverdueOnlyFilter, pageNumber, PageSize, cancellationToken);
        }
        catch (OperationCanceledException) when (disposeCancellation.IsCancellationRequested)
        {
            return;
        }

        if (disposeCancellation.IsCancellationRequested || requestVersion != loadVersion)
        {
            return;
        }

        isLoading = false;

        if (!result.IsSuccess)
        {
            failure = result.Failure;
            rotations = [];
            totalCount = 0;

            return;
        }

        var page = result.Value!;
        pageNumber = page.PageNumber;
        totalCount = page.TotalCount;
        rotations = page.Items;

        await ResolveVesselNamesAsync(page.Items.Select(item => item.VesselId), cancellationToken, requestVersion);
    }

    private async Task ResolveVesselNamesAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken, int requestVersion)
    {
        var distinctIds = ids.Distinct().ToArray();
        var names = await FleetApi.LookupVesselNamesAsync(distinctIds, cancellationToken);

        if (disposeCancellation.IsCancellationRequested || requestVersion != loadVersion)
        {
            return;
        }

        vesselNames = names;
    }

    private void ToggleCardExpanded(Guid rotationId)
    {
        if (!expandedCardIds.Add(rotationId))
        {
            expandedCardIds.Remove(rotationId);
        }
    }

    private void RequestRotationCancellation(Guid rotationId) => pendingCancellationId = rotationId;

    private void CancelRotationCancellation() => pendingCancellationId = null;

    private async Task CancelRotationAsync(Guid rotationId)
    {
        if (isCancellingRotation)
        {
            return;
        }

        isCancellingRotation = true;
        var result = await CrewApi.CancelRotationAsync(rotationId, new CancelCrewRotationRequest(), disposeCancellation.Token);
        isCancellingRotation = false;

        if (!result.IsSuccess)
        {
            failure = result.Failure;
            return;
        }

        pendingCancellationId = null;
        await LoadAsync();
    }

    private string VesselDisplayName(Guid vesselId) =>
        vesselNames.TryGetValue(vesselId, out var name) ? name : "Unknown vessel";

    private static string RotationStatusLabel(CrewRotationListItemResponse rotation) =>
        rotation.IsOverdue ? "Overdue" : rotation.Status;

    private static string RotationStatusClass(CrewRotationListItemResponse rotation) =>
        rotation.IsOverdue ? "crew-rotations-status-overdue" : rotation.Status switch
        {
            "Executed" => "crew-rotations-status-executed",
            "Cancelled" => "crew-rotations-status-cancelled",
            _ => "crew-rotations-status-planned",
        };

    public void Dispose()
    {
        disposeCancellation.Cancel();
        disposeCancellation.Dispose();
    }
}
