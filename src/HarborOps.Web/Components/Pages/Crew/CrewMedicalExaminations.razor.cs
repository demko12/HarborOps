using HarborOps.Web.Api;

namespace HarborOps.Web.Components.Pages.Crew;

public partial class CrewMedicalExaminations : IDisposable
{
    private const int PageSize = 25;

    private readonly CancellationTokenSource disposeCancellation = new();
    private int loadVersion;

    private string? searchFilter;
    private string? fitnessResultSelection;
    private string? expiredSelection;
    private int? expiringWithinDaysFilter;

    private bool? ExpiredFilter => expiredSelection == "true" ? true : null;

    private int pageNumber = 1;
    private int totalCount;
    private bool isLoading = true;
    private bool cardView = true;
    private CrewApiFailure? failure;

    private IReadOnlyList<MedicalExaminationListItemResponse> examinations = [];
    private readonly HashSet<Guid> expandedCardIds = [];

    private int TotalPages => totalCount == 0 ? 1 : (totalCount + PageSize - 1) / PageSize;
    private int FirstRowNumber => examinations.Count == 0 ? 0 : ((pageNumber - 1) * PageSize) + 1;
    private int LastRowNumber => FirstRowNumber + examinations.Count - 1;

    protected override Task OnInitializedAsync() => LoadAsync();

    private Task SearchAsync()
    {
        pageNumber = 1;

        return LoadAsync();
    }

    private Task ResetFiltersAsync()
    {
        searchFilter = null;
        fitnessResultSelection = null;
        expiredSelection = null;
        expiringWithinDaysFilter = null;
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

        CrewApiResult<MedicalExaminationsPageResponse> result;
        try
        {
            result = await CrewApi.GetMedicalExaminationsAsync(
                searchFilter, fitnessResultSelection, ExpiredFilter, expiringWithinDaysFilter, pageNumber, PageSize, cancellationToken);
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
            examinations = [];
            totalCount = 0;

            return;
        }

        var page = result.Value!;
        pageNumber = page.PageNumber;
        totalCount = page.TotalCount;
        examinations = page.Items;
    }

    private void ToggleCardExpanded(Guid examinationId)
    {
        if (!expandedCardIds.Add(examinationId))
        {
            expandedCardIds.Remove(examinationId);
        }
    }

    private static string FitnessLabel(string fitnessResult) => fitnessResult switch
    {
        "Fit" => "Fit",
        "FitWithRestrictions" => "Fit with restrictions",
        "Unfit" => "Unfit",
        _ => fitnessResult,
    };

    private static string FitnessBadgeClass(string fitnessResult) => fitnessResult switch
    {
        "Fit" => "crew-medical-badge-valid",
        "FitWithRestrictions" => "crew-medical-badge-warning",
        "Unfit" => "crew-medical-badge-expired",
        _ => "crew-medical-badge-neutral",
    };

    private static string StatusLabel(MedicalExaminationListItemResponse exam) =>
        exam.IsExpired ? "Expired" : exam.IsExpiringSoon ? "Expiring soon" : "Valid";

    private static string StatusBadgeClass(MedicalExaminationListItemResponse exam) =>
        exam.IsExpired ? "crew-medical-badge-expired" : exam.IsExpiringSoon ? "crew-medical-badge-warning" : "crew-medical-badge-valid";

    public void Dispose()
    {
        disposeCancellation.Cancel();
        disposeCancellation.Dispose();
    }
}
