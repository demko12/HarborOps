using HarborOps.Web.Api;

namespace HarborOps.Web.Components.Pages.Crew;

public partial class CrewQualificationPolicies : IDisposable
{
    private const int PageSize = 25;
    private const int PositionOptionsPageSize = 100;

    private static readonly IReadOnlyDictionary<string, string[]> EmptyErrors =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);

    private readonly CancellationTokenSource disposeCancellation = new();
    private int loadVersion;

    private Guid? crewPositionFilter;
    private int pageNumber = 1;
    private int totalCount;
    private bool isLoading = true;
    private CrewApiFailure? failure;

    private IReadOnlyList<QualificationPolicyListItemResponse> policies = [];
    private IReadOnlyList<CrewPositionListItemResponse> positionOptions = [];

    private bool isDialogOpen;
    private Guid? editingPolicyId;
    private DialogModel dialogModel = new();
    private readonly List<RequirementFormRow> requirementRows = [new()];
    private bool isSubmitting;
    private string? conflictMessage;
    private string? generalError;
    private IReadOnlyList<string> fieldErrors = [];

    private bool CanManage => CurrentUser.HasPermission(CurrentUserAuthorizationState.CrewManagePermission);

    private int TotalPages => totalCount == 0 ? 1 : (totalCount + PageSize - 1) / PageSize;
    private int FirstRowNumber => policies.Count == 0 ? 0 : ((pageNumber - 1) * PageSize) + 1;
    private int LastRowNumber => FirstRowNumber + policies.Count - 1;

    protected override async Task OnInitializedAsync()
    {
        var positionsResult = await CrewApi.GetPositionsAsync(null, PositionOptionsPageSize, disposeCancellation.Token);
        positionOptions = positionsResult.IsSuccess ? positionsResult.Value!.Items : [];

        await LoadAsync();
    }

    private Task FilterChangedAsync()
    {
        pageNumber = 1;

        return LoadAsync();
    }

    private Task ResetFiltersAsync()
    {
        crewPositionFilter = null;
        pageNumber = 1;

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

    private async Task LoadAsync()
    {
        var requestVersion = ++loadVersion;
        var cancellationToken = disposeCancellation.Token;
        isLoading = true;
        failure = null;

        CrewApiResult<GetQualificationPoliciesResponse> result;
        try
        {
            result = await CrewApi.GetQualificationPoliciesAsync(crewPositionFilter, pageNumber, PageSize, cancellationToken);
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
            policies = [];
            totalCount = 0;

            return;
        }

        var page = result.Value!;
        pageNumber = page.PageNumber;
        totalCount = page.TotalCount;
        policies = page.Items;
    }

    private void OnRankChanged(Microsoft.AspNetCore.Components.ChangeEventArgs e)
    {
        dialogModel.CrewPositionId = Guid.TryParse(e.Value?.ToString(), out var rankId) ? rankId : null;
    }

    private void OpenCreateDialog()
    {
        editingPolicyId = null;
        dialogModel = new DialogModel { Enabled = true };
        requirementRows.Clear();
        requirementRows.Add(new RequirementFormRow());
        ResetDialogFeedback();
        isDialogOpen = true;
    }

    private async Task OpenEditDialogAsync(QualificationPolicyListItemResponse policy)
    {
        editingPolicyId = policy.Id;
        dialogModel = new DialogModel { CrewPositionId = policy.CrewPositionId, Enabled = policy.Enabled };
        ResetDialogFeedback();
        isDialogOpen = true;

        var result = await CrewApi.GetQualificationPolicyDetailAsync(policy.Id, disposeCancellation.Token);

        requirementRows.Clear();
        if (result.IsSuccess && result.Value is not null)
        {
            foreach (var type in result.Value.RequiredTypes)
            {
                requirementRows.Add(new RequirementFormRow { RequiredType = type });
            }
        }

        if (requirementRows.Count == 0)
        {
            requirementRows.Add(new RequirementFormRow());
        }
    }

    private void CloseDialog() => isDialogOpen = false;

    private void AddRequirementRow() => requirementRows.Add(new RequirementFormRow());

    private void RemoveRequirementRow(RequirementFormRow row)
    {
        if (requirementRows.Count > 1)
        {
            requirementRows.Remove(row);
        }
    }

    private void ResetDialogFeedback()
    {
        conflictMessage = null;
        generalError = null;
        fieldErrors = [];
    }

    private async Task SubmitAsync()
    {
        if (isSubmitting)
        {
            return;
        }

        ResetDialogFeedback();
        isSubmitting = true;

        var requirements = requirementRows
            .Select(row => new QualificationRequirementInputRequest { RequiredType = row.RequiredType })
            .ToList();

        CrewApiFailure? submitFailure;

        if (editingPolicyId is { } policyId)
        {
            var result = await CrewApi.UpdateQualificationPolicyAsync(
                policyId,
                new UpdateQualificationPolicyRequest { Enabled = dialogModel.Enabled, Requirements = requirements },
                disposeCancellation.Token);

            submitFailure = result.IsSuccess ? null : result.Failure;
        }
        else
        {
            var result = await CrewApi.CreateQualificationPolicyAsync(
                new CreateQualificationPolicyRequest
                {
                    CrewPositionId = dialogModel.CrewPositionId,
                    Enabled = dialogModel.Enabled,
                    Requirements = requirements,
                },
                disposeCancellation.Token);

            submitFailure = result.IsSuccess ? null : result.Failure;
        }

        isSubmitting = false;

        if (submitFailure is not null)
        {
            if (submitFailure.IsConflict)
            {
                conflictMessage = submitFailure.Message;
            }
            else if (submitFailure.IsValidation)
            {
                fieldErrors = (submitFailure.Errors ?? EmptyErrors).Values.SelectMany(messages => messages).ToList();
            }
            else
            {
                generalError = submitFailure.Message;
            }

            return;
        }

        isDialogOpen = false;
        await LoadAsync();
    }

    public void Dispose()
    {
        disposeCancellation.Cancel();
        disposeCancellation.Dispose();
    }

    private sealed class DialogModel
    {
        public Guid? CrewPositionId { get; set; }
        public bool? Enabled { get; set; }
    }

    private sealed class RequirementFormRow
    {
        public string? RequiredType { get; set; }
    }
}
