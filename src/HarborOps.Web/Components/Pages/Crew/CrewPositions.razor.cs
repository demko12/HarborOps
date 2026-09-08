using HarborOps.Web.Api;

namespace HarborOps.Web.Components.Pages.Crew;

public partial class CrewPositions : IDisposable
{
    private const int PageSize = 25;

    private static readonly IReadOnlyDictionary<string, string[]> EmptyErrors =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);

    private static readonly (string Value, string Label)[] DepartmentOptions =
    [
        ("Deck", "Deck"),
        ("Engine", "Engine"),
        ("Catering", "Catering"),
        ("Electrical", "Electrical / ETO"),
    ];

    private static readonly (string Value, string Label)[] SeniorityLevelOptions =
    [
        ("Officer", "Officer"),
        ("SeniorRating", "Senior Rating"),
        ("JuniorRating", "Junior Rating"),
        ("Cadet", "Cadet"),
    ];

    private readonly CancellationTokenSource disposeCancellation = new();
    private int loadVersion;

    private string? search;
    private string? departmentFilter;

    private int pageNumber = 1;
    private int totalCount;
    private bool isLoading = true;
    private CrewApiFailure? failure;

    private IReadOnlyList<CrewPositionListItemResponse> positions = [];

    private bool isDialogOpen;
    private Guid? editingPositionId;
    private DialogModel dialogModel = new();
    private bool isSubmitting;
    private string? conflictMessage;
    private string? generalError;
    private IReadOnlyList<string> fieldErrors = [];

    private bool CanManage => CurrentUser.HasPermission(CurrentUserAuthorizationState.CrewManagePermission);

    private int TotalPages => totalCount == 0 ? 1 : ((totalCount + PageSize - 1) / PageSize);
    private int FirstRowNumber => positions.Count == 0 ? 0 : ((pageNumber - 1) * PageSize) + 1;
    private int LastRowNumber => FirstRowNumber + positions.Count - 1;

    protected override Task OnInitializedAsync() => LoadAsync();

    private static string DepartmentLabel(string value) =>
        DepartmentOptions.FirstOrDefault(option => option.Value == value).Label is { } label && !string.IsNullOrEmpty(label)
            ? label
            : value;

    private static string SeniorityLevelLabel(string value) =>
        SeniorityLevelOptions.FirstOrDefault(option => option.Value == value).Label is { } label && !string.IsNullOrEmpty(label)
            ? label
            : value;

    private Task SearchAsync()
    {
        pageNumber = 1;

        return LoadAsync();
    }

    private Task ResetFiltersAsync()
    {
        search = null;
        departmentFilter = null;
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

        CrewApiResult<CrewPositionsResponse> result;
        try
        {
            result = await CrewApi.GetPositionsAsync(
                search, PageSize, cancellationToken, departmentFilter, pageNumber);
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
            positions = [];
            totalCount = 0;

            return;
        }

        var page = result.Value!;
        pageNumber = page.PageNumber;
        totalCount = page.TotalCount;
        positions = page.Items;
    }

    private void OpenCreateDialog()
    {
        editingPositionId = null;
        dialogModel = new DialogModel();
        ResetDialogFeedback();
        isDialogOpen = true;
    }

    private void OpenEditDialog(CrewPositionListItemResponse position)
    {
        editingPositionId = position.CrewPositionId;
        dialogModel = new DialogModel
        {
            RankName = position.RankName,
            Department = position.Department,
            SeniorityLevel = position.SeniorityLevel,
        };
        ResetDialogFeedback();
        isDialogOpen = true;
    }

    private void CloseDialog()
    {
        isDialogOpen = false;
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

        CrewApiFailure? submitFailure;

        if (editingPositionId is { } positionId)
        {
            var result = await CrewApi.UpdatePositionAsync(
                positionId,
                new UpdateCrewPositionRequest
                {
                    RankName = dialogModel.RankName,
                    Department = dialogModel.Department,
                    SeniorityLevel = dialogModel.SeniorityLevel,
                },
                disposeCancellation.Token);

            submitFailure = result.IsSuccess ? null : result.Failure;
        }
        else
        {
            var result = await CrewApi.RegisterPositionAsync(
                new RegisterCrewPositionRequest
                {
                    RankName = dialogModel.RankName,
                    Department = dialogModel.Department,
                    SeniorityLevel = dialogModel.SeniorityLevel,
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
        public string? RankName { get; set; }
        public string? Department { get; set; }
        public string? SeniorityLevel { get; set; }
    }
}
