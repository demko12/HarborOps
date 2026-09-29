using HarborOps.Web.Api;

namespace HarborOps.Web.Components.Pages.Crew;

public partial class CrewAvailability : IDisposable
{
    private const int PageSize = 25;

    private readonly CancellationTokenSource disposeCancellation = new();
    private int loadVersion;

    private Guid? crewMemberIdFilter;
    private int memberPickerResetToken;
    private string? typeFilter;
    private string? statusFilter;
    private DateOnly? fromFilter;
    private DateOnly? toFilter;

    private int pageNumber = 1;
    private int totalCount;
    private bool isLoading = true;
    private bool cardView = true;
    private CrewApiFailure? failure;
    private string? filterValidationMessage;

    private IReadOnlyList<CrewAvailabilityListItemResponse> items = [];
    private readonly HashSet<Guid> expandedIds = [];

    private Guid? decisionRequestId;
    private DecisionKind decisionKind;
    private string? decisionReason;

    private Guid? actionRequestId;
    private bool isSubmitting;
    private string? actionConflictMessage;
    private string? actionGeneralError;
    private string? actionSuccessMessage;
    private IReadOnlyList<string> actionFieldErrors = [];

    private bool CanManage => CurrentUser.HasPermission(CurrentUserAuthorizationState.CrewManagePermission);

    private int TotalPages => totalCount == 0 ? 1 : (totalCount + PageSize - 1) / PageSize;
    private int FirstRowNumber => items.Count == 0 ? 0 : ((pageNumber - 1) * PageSize) + 1;
    private int LastRowNumber => FirstRowNumber + items.Count - 1;

    private enum DecisionKind
    {
        Reject,
        Cancel,
    }

    protected override Task OnInitializedAsync() => LoadAsync();

    private Task SearchAsync()
    {
        pageNumber = 1;

        return LoadAsync();
    }

    private Task OnMemberFilterChangedAsync(Guid? crewMemberId)
    {
        crewMemberIdFilter = crewMemberId;

        return SearchAsync();
    }

    private Task ResetFiltersAsync()
    {
        crewMemberIdFilter = null;
        memberPickerResetToken++;
        typeFilter = null;
        statusFilter = null;
        fromFilter = null;
        toFilter = null;
        pageNumber = 1;
        filterValidationMessage = null;
        ClearActionState();

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
            filterValidationMessage = "The \"to\" date must be on or after the \"from\" date.";

            return Task.CompletedTask;
        }

        filterValidationMessage = null;

        return LoadItemsAsync();
    }

    private async Task LoadItemsAsync()
    {
        var requestVersion = ++loadVersion;
        var cancellationToken = disposeCancellation.Token;
        isLoading = true;
        failure = null;

        CrewApiResult<GetCrewAvailabilityResponse> result;
        try
        {
            result = await CrewApi.GetAvailabilityAsync(
                crewMemberIdFilter,
                fromFilter,
                toFilter,
                string.IsNullOrWhiteSpace(typeFilter) ? null : typeFilter,
                string.IsNullOrWhiteSpace(statusFilter) ? null : statusFilter,
                pageNumber,
                PageSize,
                cancellationToken);
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
            items = [];
            totalCount = 0;

            return;
        }

        var page = result.Value!;
        pageNumber = page.PageNumber;
        totalCount = page.TotalCount;
        items = page.Items;
    }

    private void ToggleExpanded(Guid id)
    {
        if (!expandedIds.Add(id))
        {
            expandedIds.Remove(id);
        }

        if (decisionRequestId is not null && decisionRequestId != id)
        {
            CancelDecision();
        }

        ClearActionState();
    }

    private void BeginDecision(Guid id, DecisionKind kind)
    {
        decisionRequestId = id;
        decisionKind = kind;
        decisionReason = null;
        ClearActionState();
    }

    private void CancelDecision()
    {
        decisionRequestId = null;
        decisionReason = null;
    }

    private async Task ApproveAsync(Guid id)
    {
        if (isSubmitting)
        {
            return;
        }

        BeginAction(id);
        var result = await CrewApi.ApproveAvailabilityRequestAsync(id, disposeCancellation.Token);
        await CompleteActionAsync(result.IsSuccess, result.Failure, "Unavailability approved.");
    }

    private async Task SubmitDecisionAsync()
    {
        if (isSubmitting || decisionRequestId is not { } id)
        {
            return;
        }

        BeginAction(id);

        if (decisionKind == DecisionKind.Reject)
        {
            var result = await CrewApi.RejectAvailabilityRequestAsync(
                id, new RejectCrewAvailabilityRequestRequest { Reason = decisionReason }, disposeCancellation.Token);
            await CompleteActionAsync(result.IsSuccess, result.Failure, "Unavailability rejected.");
        }
        else
        {
            var result = await CrewApi.CancelAvailabilityRequestAsync(
                id, new CancelCrewAvailabilityRequestRequest { Reason = decisionReason }, disposeCancellation.Token);
            await CompleteActionAsync(result.IsSuccess, result.Failure, "Unavailability cancelled.");
        }
    }

    private void BeginAction(Guid id)
    {
        isSubmitting = true;
        actionRequestId = id;
        actionConflictMessage = null;
        actionGeneralError = null;
        actionSuccessMessage = null;
        actionFieldErrors = [];
    }

    private async Task CompleteActionAsync(bool isSuccess, CrewApiFailure? actionFailure, string successMessage)
    {
        isSubmitting = false;

        if (!isSuccess)
        {
            HandleAvailabilityFailure(actionFailure!);

            return;
        }

        decisionRequestId = null;
        decisionReason = null;
        actionSuccessMessage = successMessage;
        await LoadAsync();
    }

    private void HandleAvailabilityFailure(CrewApiFailure availabilityFailure)
    {
        if (availabilityFailure.IsConflict)
        {
            actionConflictMessage = availabilityFailure.Message;
        }
        else if (availabilityFailure.IsValidation)
        {
            actionFieldErrors = (availabilityFailure.Errors ?? new Dictionary<string, string[]>())
                .Values.SelectMany(messages => messages).ToList();
        }
        else
        {
            actionGeneralError = availabilityFailure.Message;
        }
    }

    private void ClearActionState()
    {
        actionRequestId = null;
        actionConflictMessage = null;
        actionGeneralError = null;
        actionSuccessMessage = null;
        actionFieldErrors = [];
    }

    private static string TypeLabel(string type) => type switch
    {
        "AnnualLeave" => "Annual leave",
        "SickLeave" => "Sick leave",
        "Training" => "Training",
        "OtherUnavailable" => "Other",
        _ => type,
    };

    private static string StatusClass(string status) => status switch
    {
        "Approved" => "crew-availability-status-approved",
        "Rejected" => "crew-availability-status-rejected",
        "Cancelled" => "crew-availability-status-cancelled",
        _ => "crew-availability-status-requested",
    };

    public void Dispose()
    {
        disposeCancellation.Cancel();
        disposeCancellation.Dispose();
    }
}
