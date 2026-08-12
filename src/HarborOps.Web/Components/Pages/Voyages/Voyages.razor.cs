using System.Globalization;
using HarborOps.Web.Api.Voyages;

namespace HarborOps.Web.Components.Pages.Voyages;

public partial class Voyages
{
    internal const string PlannedStatus = "Planned";
    internal const string InProgressStatus = "InProgress";
    internal const string CompletedStatus = "Completed";
    internal const string MissingValue = "—";
    internal const int DefaultWindowDays = 30;
    internal const int ImminentDepartureHours = 24;

    internal const string ForbiddenTitle = "You do not have access to voyages";

    internal const string ForbiddenMessage =
        "Your account is missing the voyages read permission. Ask an administrator to grant it.";

    private static readonly string[] StatusOptions =
        [PlannedStatus, InProgressStatus, CompletedStatus];

    private readonly List<VoyageListItemModel> voyages = [];

    private CancellationTokenSource loadCancellation = new();

    private DateOnly fromDate = DefaultFrom();
    private DateOnly toDate = DefaultTo();
    private string statusFilter = string.Empty;

    private DateOnly appliedFrom = DefaultFrom();
    private DateOnly appliedTo = DefaultTo();
    private string appliedStatus = string.Empty;
    private int pageNumber = 1;
    private int pageSize = 25;
    private int totalCount;
    private int? plannedCount;
    private VoyagesApiFailure? failure;
    private bool isLoading = true;

    private int TotalPages => totalCount == 0
        ? 1
        : (int)Math.Ceiling(totalCount / (double)pageSize);

    private int FirstRowNumber => totalCount == 0 ? 0 : ((pageNumber - 1) * pageSize) + 1;

    private int LastRowNumber => Math.Min(pageNumber * pageSize, totalCount);

    protected override Task OnInitializedAsync()
    {
        return LoadAsync();
    }

    internal static string FormatRoute(string departurePortCode, string arrivalPortCode)
    {
        return $"{departurePortCode} → {arrivalPortCode}";
    }

    internal static string FormatDate(DateOnly value)
    {
        return value.ToString("dd MMM yyyy", CultureInfo.InvariantCulture);
    }

    internal static string FormatUtc(DateTime? value)
    {
        return value is { } stamp
            ? stamp.ToString("dd MMM yyyy HH:mm", CultureInfo.InvariantCulture)
            : MissingValue;
    }

    internal static string FormatVesselReference(Guid vesselId)
    {
        return vesselId.ToString("N", CultureInfo.InvariantCulture)[..8];
    }

    internal static int StatusStep(string status)
    {
        return status switch
        {
            CompletedStatus => 3,
            InProgressStatus => 2,
            _ => 1,
        };
    }

    internal static string StatusModifier(string status)
    {
        return status switch
        {
            CompletedStatus => "completed",
            InProgressStatus => "in-progress",
            _ => "planned",
        };
    }

    internal static bool IsImminentDeparture(DateTime plannedDeparture, DateTime utcNow)
    {
        return plannedDeparture >= utcNow
            && plannedDeparture <= utcNow.AddHours(ImminentDepartureHours);
    }

    internal static string FailureTitle(VoyagesApiFailure failure)
    {
        return failure.IsForbidden ? ForbiddenTitle : failure.Title;
    }

    internal static string FailureMessage(VoyagesApiFailure failure)
    {
        return failure.IsForbidden ? ForbiddenMessage : failure.Message;
    }

    private static DateOnly DefaultFrom()
    {
        return DateOnly.FromDateTime(DateTime.UtcNow);
    }

    private static DateOnly DefaultTo()
    {
        return DateOnly.FromDateTime(DateTime.UtcNow).AddDays(DefaultWindowDays);
    }

    private async Task ApplyFiltersAsync()
    {
        appliedFrom = fromDate;
        appliedTo = toDate;
        appliedStatus = statusFilter;
        pageNumber = 1;

        await LoadAsync();
    }

    private async Task ResetFiltersAsync()
    {
        fromDate = DefaultFrom();
        toDate = DefaultTo();
        statusFilter = string.Empty;

        await ApplyFiltersAsync();
    }

    private async Task GoToPreviousPageAsync()
    {
        if (pageNumber <= 1)
        {
            return;
        }

        pageNumber--;
        await LoadAsync();
    }

    private async Task GoToNextPageAsync()
    {
        if (pageNumber >= TotalPages)
        {
            return;
        }

        pageNumber++;
        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        loadCancellation.Cancel();
        loadCancellation.Dispose();
        loadCancellation = new CancellationTokenSource();

        var cancellationToken = loadCancellation.Token;

        isLoading = true;
        failure = null;

        var result = await VoyagesApi.GetVoyagesAsync(
            appliedFrom,
            appliedTo,
            appliedStatus,
            pageNumber,
            pageSize,
            cancellationToken);

        if (cancellationToken.IsCancellationRequested)
        {
            return;
        }

        isLoading = false;

        if (!result.IsSuccess)
        {
            failure = result.Failure;
            voyages.Clear();
            totalCount = 0;
            plannedCount = null;
            return;
        }

        var page = result.Value!;
        voyages.Clear();
        voyages.AddRange(page.Items);
        pageNumber = page.PageNumber;
        totalCount = page.TotalCount;

        await LoadPlannedCountAsync(cancellationToken);
    }

    private async Task LoadPlannedCountAsync(CancellationToken cancellationToken)
    {
        var result = await VoyagesApi.GetVoyagesAsync(
            appliedFrom,
            appliedTo,
            PlannedStatus,
            1,
            1,
            cancellationToken);

        if (cancellationToken.IsCancellationRequested)
        {
            return;
        }

        plannedCount = result.IsSuccess ? result.Value!.TotalCount : null;
    }

    public void Dispose()
    {
        loadCancellation.Cancel();
        loadCancellation.Dispose();
    }
}
