using HarborOps.Web.Api;

namespace HarborOps.Web.Components.Pages.Crew;

public partial class CrewManning : IDisposable
{
    private const int PositionOptionsPageSize = 100;
    private const int MaxCoverageWindowDays = 90;

    private static readonly IReadOnlyDictionary<string, string[]> EmptyErrors =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);

    private readonly CancellationTokenSource disposeCancellation = new();
    private readonly DateOnly today = DateOnly.FromDateTime(DateTime.Today);

    private Guid? vesselId;
    private int vesselPickerResetToken;
    private DateOnly fromFilter;
    private DateOnly toFilter;
    private string? filterValidationMessage;

    private IReadOnlyList<CrewPositionListItemResponse> positionOptions = [];

    private bool plansLoading;
    private CrewApiFailure? plansFailure;
    private IReadOnlyList<VesselManningPlanListItemResponse> plans = [];

    private bool coverageLoading;
    private CrewApiFailure? coverageFailure;
    private GetCrewManningCoverageResponse? coverage;

    private bool CanManage => CurrentUser.HasPermission(CurrentUserAuthorizationState.CrewManagePermission);

    private bool showCreateForm;
    private DateOnly? createStartDate;
    private DateOnly? createEndDate;
    private readonly List<LineFormRow> createLines = [new()];
    private bool isSubmittingCreate;
    private string? createConflictMessage;
    private string? createGeneralError;
    private IReadOnlyList<string> createFieldErrors = [];

    private readonly HashSet<Guid> expandedPlanIds = [];
    private Guid? editingPlanId;
    private readonly List<LineFormRow> editLines = [];
    private string? editReason;
    private Guid? endingPlanId;
    private DateOnly? endNewEndDate;
    private string? endReason;
    private bool isSubmittingPlanAction;
    private string? planActionConflictMessage;
    private string? planActionGeneralError;
    private IReadOnlyList<string> planActionFieldErrors = [];
    private string? planActionSuccessMessage;

    private readonly HashSet<Guid> expandedShortageRankIds = [];

    protected override async Task OnInitializedAsync()
    {
        fromFilter = today;
        toFilter = today.AddDays(29);

        var positionsResult = await CrewApi.GetPositionsAsync(null, PositionOptionsPageSize, disposeCancellation.Token);
        positionOptions = positionsResult.IsSuccess ? positionsResult.Value!.Items : [];
    }

    private Task OnVesselChangedAsync(Guid? newVesselId)
    {
        vesselId = newVesselId;
        showCreateForm = false;
        editingPlanId = null;
        endingPlanId = null;

        return LoadAllAsync();
    }

    private Task ApplyDateFilterAsync()
    {
        if (toFilter < fromFilter)
        {
            filterValidationMessage = "The \"to\" date must be on or after the \"from\" date.";

            return Task.CompletedTask;
        }

        if (toFilter.DayNumber - fromFilter.DayNumber + 1 > MaxCoverageWindowDays)
        {
            filterValidationMessage = $"The coverage window cannot exceed {MaxCoverageWindowDays} days.";

            return Task.CompletedTask;
        }

        filterValidationMessage = null;

        return LoadAllAsync();
    }

    private Task ResetFiltersAsync()
    {
        vesselId = null;
        vesselPickerResetToken++;
        fromFilter = today;
        toFilter = today.AddDays(29);
        filterValidationMessage = null;
        showCreateForm = false;
        editingPlanId = null;
        endingPlanId = null;
        plans = [];
        coverage = null;

        return Task.CompletedTask;
    }

    private async Task LoadAllAsync()
    {
        if (vesselId is not { } vessel)
        {
            plans = [];
            coverage = null;

            return;
        }

        await Task.WhenAll(LoadPlansAsync(vessel), LoadCoverageAsync(vessel));
    }

    private async Task LoadPlansAsync(Guid vessel)
    {
        plansLoading = true;
        plansFailure = null;
        StateHasChanged();

        var result = await CrewApi.GetManningPlansAsync(vessel, 1, 50, disposeCancellation.Token);

        plansLoading = false;

        if (!result.IsSuccess)
        {
            plansFailure = result.Failure;
            plans = [];

            return;
        }

        plans = result.Value!.Items;
    }

    private async Task LoadCoverageAsync(Guid vessel)
    {
        coverageLoading = true;
        coverageFailure = null;
        StateHasChanged();

        var result = await CrewApi.GetManningCoverageAsync(vessel, fromFilter, toFilter, disposeCancellation.Token);

        coverageLoading = false;

        if (!result.IsSuccess)
        {
            coverageFailure = result.Failure;
            coverage = null;

            return;
        }

        coverage = result.Value;
    }

    private IEnumerable<IGrouping<Guid, CoverageShortageIntervalResponse>> GroupedShortageIntervals() =>
        (coverage?.ShortageIntervals ?? [])
            .OrderBy(interval => interval.StartDate)
            .GroupBy(interval => interval.RankId);

    private static string GapCountLabel(int count) => count == 1 ? "1 gap" : $"{count} gaps";

    private string RankNameFor(Guid rankId) =>
        coverage?.ShortageIntervals.FirstOrDefault(i => i.RankId == rankId)?.RankName ?? "Unknown rank";

    private IReadOnlyList<CoverageIneligibleMemberResponse> IneligibleMembersFor(Guid rankId, DateOnly start, DateOnly end) =>
        (coverage?.Days ?? [])
            .Where(day => day.RankId == rankId && day.Date >= start && day.Date <= end)
            .SelectMany(day => day.IneligibleMembers)
            .DistinctBy(member => member.CrewMemberId)
            .ToList();

    private IReadOnlyList<CoverageUnqualifiedMemberResponse> UnqualifiedMembersFor(Guid rankId, DateOnly start, DateOnly end) =>
        (coverage?.Days ?? [])
            .Where(day => day.RankId == rankId && day.Date >= start && day.Date <= end)
            .SelectMany(day => day.UnqualifiedMembers ?? [])
            .DistinctBy(member => member.CrewMemberId)
            .ToList();

    private void ToggleCreateForm()
    {
        showCreateForm = !showCreateForm;

        if (showCreateForm)
        {
            createStartDate = null;
            createEndDate = null;
            createLines.Clear();
            createLines.Add(new LineFormRow());
            ClearCreateState();
        }
    }

    private void AddCreateLine() => createLines.Add(new LineFormRow());

    private void RemoveCreateLine(LineFormRow row)
    {
        if (createLines.Count > 1)
        {
            createLines.Remove(row);
        }
    }

    private void ClearCreateState()
    {
        createConflictMessage = null;
        createGeneralError = null;
        createFieldErrors = [];
    }

    private async Task SubmitCreateAsync()
    {
        if (isSubmittingCreate || vesselId is not { } vessel)
        {
            return;
        }

        ClearCreateState();
        isSubmittingCreate = true;

        var request = new CreateVesselManningPlanRequest
        {
            VesselId = vessel,
            EffectiveStartDate = createStartDate,
            EffectiveEndDate = createEndDate,
            Lines = createLines
                .Select(row => new ManningPlanLineInputRequest { RankId = row.RankId, RequiredHeadcount = row.RequiredHeadcount })
                .ToList(),
        };

        var result = await CrewApi.CreateManningPlanAsync(request, disposeCancellation.Token);

        isSubmittingCreate = false;

        if (!result.IsSuccess)
        {
            var failure = result.Failure!;

            if (failure.IsConflict)
            {
                createConflictMessage = failure.Message;
            }
            else if (failure.IsValidation)
            {
                createFieldErrors = (failure.Errors ?? EmptyErrors).Values.SelectMany(m => m).ToList();
            }
            else
            {
                createGeneralError = failure.Message;
            }

            return;
        }

        showCreateForm = false;
        await LoadAllAsync();
    }

    private void BeginEditLines(VesselManningPlanListItemResponse plan)
    {
        editingPlanId = plan.Id;
        endingPlanId = null;
        editLines.Clear();
        editReason = null;
        ClearPlanActionState();
        _ = LoadPlanLinesForEditAsync(plan.Id);
    }

    private async Task LoadPlanLinesForEditAsync(Guid planId)
    {
        var result = await CrewApi.GetManningPlanDetailAsync(planId, disposeCancellation.Token);

        if (!result.IsSuccess || editingPlanId != planId)
        {
            return;
        }

        editLines.Clear();
        foreach (var line in result.Value!.Lines)
        {
            editLines.Add(new LineFormRow { RankId = line.RankId, RequiredHeadcount = line.RequiredHeadcount });
        }

        if (editLines.Count == 0)
        {
            editLines.Add(new LineFormRow());
        }

        StateHasChanged();
    }

    private void AddEditLine() => editLines.Add(new LineFormRow());

    private void RemoveEditLine(LineFormRow row)
    {
        if (editLines.Count > 1)
        {
            editLines.Remove(row);
        }
    }

    private void CancelEditLines()
    {
        editingPlanId = null;
        editLines.Clear();
        ClearPlanActionState();
    }

    private async Task SubmitEditLinesAsync()
    {
        if (isSubmittingPlanAction || editingPlanId is not { } planId)
        {
            return;
        }

        ClearPlanActionState();
        isSubmittingPlanAction = true;

        var request = new ReplaceVesselManningPlanLinesRequest
        {
            Reason = editReason,
            Lines = editLines
                .Select(row => new ManningPlanLineInputRequest { RankId = row.RankId, RequiredHeadcount = row.RequiredHeadcount })
                .ToList(),
        };

        var result = await CrewApi.ReplaceManningPlanLinesAsync(planId, request, disposeCancellation.Token);

        isSubmittingPlanAction = false;

        if (!result.IsSuccess)
        {
            HandlePlanActionFailure(result.Failure!);

            return;
        }

        planActionSuccessMessage = "Rank lines updated.";
        editingPlanId = null;
        editLines.Clear();
        await LoadAllAsync();
    }

    private void BeginEndPlan(VesselManningPlanListItemResponse plan)
    {
        endingPlanId = plan.Id;
        editingPlanId = null;
        endNewEndDate = plan.EffectiveEndDate;
        endReason = null;
        ClearPlanActionState();
    }

    private void CancelEndPlan()
    {
        endingPlanId = null;
        ClearPlanActionState();
    }

    private async Task SubmitEndPlanAsync()
    {
        if (isSubmittingPlanAction || endingPlanId is not { } planId)
        {
            return;
        }

        ClearPlanActionState();
        isSubmittingPlanAction = true;

        var result = await CrewApi.EndManningPlanAsync(
            planId, new EndVesselManningPlanRequest { NewEndDate = endNewEndDate, Reason = endReason }, disposeCancellation.Token);

        isSubmittingPlanAction = false;

        if (!result.IsSuccess)
        {
            HandlePlanActionFailure(result.Failure!);

            return;
        }

        planActionSuccessMessage = "Manning plan end date updated.";
        endingPlanId = null;
        await LoadAllAsync();
    }

    private void HandlePlanActionFailure(CrewApiFailure failure)
    {
        if (failure.IsConflict)
        {
            planActionConflictMessage = failure.Message;
        }
        else if (failure.IsValidation)
        {
            planActionFieldErrors = (failure.Errors ?? EmptyErrors).Values.SelectMany(m => m).ToList();
        }
        else
        {
            planActionGeneralError = failure.Message;
        }
    }

    private void ClearPlanActionState()
    {
        planActionConflictMessage = null;
        planActionGeneralError = null;
        planActionFieldErrors = [];
        planActionSuccessMessage = null;
    }

    private void TogglePlanExpanded(Guid planId)
    {
        if (!expandedPlanIds.Add(planId))
        {
            expandedPlanIds.Remove(planId);
        }
    }

    private void ToggleShortageRankExpanded(Guid rankId)
    {
        if (!expandedShortageRankIds.Add(rankId))
        {
            expandedShortageRankIds.Remove(rankId);
        }
    }

    public void Dispose()
    {
        disposeCancellation.Cancel();
        disposeCancellation.Dispose();
    }

    private static void OnLineRankChanged(LineFormRow row, Microsoft.AspNetCore.Components.ChangeEventArgs e)
    {
        row.RankId = Guid.TryParse(e.Value?.ToString(), out var rankId) ? rankId : null;
    }

    private sealed class LineFormRow
    {
        public Guid? RankId { get; set; }
        public int? RequiredHeadcount { get; set; }
    }
}
