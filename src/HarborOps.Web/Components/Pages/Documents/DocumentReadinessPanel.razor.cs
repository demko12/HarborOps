using System.Globalization;
using HarborOps.Web.Api.Documents;
using Microsoft.AspNetCore.Components;

namespace HarborOps.Web.Components.Pages.Documents;

public partial class DocumentReadinessPanel : IDisposable
{
    internal const string ManageDocumentsPermission = "documents.manage";
    internal const string SatisfiedStatus = "Satisfied";
    internal const string UnsatisfiedStatus = "Unsatisfied";
    internal const string ReadyVesselStatus = "Ready";
    internal const string NotReadyVesselStatus = "NotReady";
    internal const string NotConfiguredVesselStatus = "NotConfigured";
    internal const string MissingBlocker = "Missing";
    internal const string NotIssuedDistributionStatus = "NotIssued";
    internal const string AcknowledgedRecipientState = "Acknowledged";

    private const int RequiredThroughHorizonDays = 365;
    private const int VesselPageSize = 25;

    private static readonly IReadOnlyDictionary<string, string> BlockerLabels = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [MissingBlocker] = "Missing",
        ["InactiveOrRetained"] = "Inactive or retained",
        ["NotApproved"] = "Not approved",
        ["MissingFile"] = "Missing file",
        ["NotYetEffective"] = "Not yet effective",
        ["InsufficientValidity"] = "Insufficient validity",
    };

    private readonly CancellationTokenSource disposeCancellation = new();
    private readonly HashSet<Guid> expandedRequirementIds = [];
    private readonly HashSet<Guid> expandedRecipientIds = [];

    private int loadVersion;
    private int appliedRefreshToken;

    private IReadOnlyList<DocumentReadinessRowDto> rows = [];
    private IReadOnlyDictionary<Guid, string> vesselNames = new Dictionary<Guid, string>();
    private DocumentReadinessSummaryDto? summary;
    private string? vesselStatus;
    private DocumentsApiFailure? failure;
    private bool isLoading = true;

    private Guid? vesselFilter;
    private DateOnly? requiredThroughFilter;
    private string statusFilter = string.Empty;
    private int filterVesselPickerResetToken;
    private string? initialVesselLabel;
    private DateOnly? appliedRequiredThrough;
    private DateOnly? serverToday;

    private int pageNumber = 1;
    private int pageSize = 25;
    private int totalCount;

    private int vesselLoadVersion;
    private IReadOnlyList<VesselReadinessOverviewRowDto> vesselRows = [];
    private DocumentsApiFailure? vesselFailure;
    private bool isLoadingVessels;
    private int vesselPageNumber = 1;
    private int vesselTotalCount;

    [Parameter]
    public int RefreshToken { get; set; }

    [Parameter]
    public Guid? InitialVesselId { get; set; }

    [Parameter]
    public EventCallback<Guid> OnOpenDocument { get; set; }

    [Parameter]
    public EventCallback<(Guid VesselId, string VesselName, int DocumentTypeId)> OnRegisterMissing { get; set; }

    private bool CanManage => AuthorizationState.HasPermission(ManageDocumentsPermission);

    private DateOnly Today => serverToday ?? DateOnly.FromDateTime(DateTime.UtcNow);

    private int TotalPages => totalCount == 0 ? 1 : (totalCount + pageSize - 1) / pageSize;

    private int FirstRowNumber => rows.Count == 0 ? 0 : ((pageNumber - 1) * pageSize) + 1;

    private int LastRowNumber => FirstRowNumber + rows.Count - 1;

    private int VesselTotalPages => vesselTotalCount == 0 ? 1 : (vesselTotalCount + VesselPageSize - 1) / VesselPageSize;

    public void Dispose()
    {
        disposeCancellation.Cancel();
        disposeCancellation.Dispose();
    }

    protected override Task OnInitializedAsync()
    {
        appliedRefreshToken = RefreshToken;
        vesselFilter = InitialVesselId;

        return Task.WhenAll(LoadAsync(), LoadVesselsAsync(), ResolveInitialVesselLabelAsync());
    }

    private async Task ResolveInitialVesselLabelAsync()
    {
        if (InitialVesselId is not { } vesselId)
        {
            return;
        }

        var names = await FleetApi.LookupVesselNamesAsync([vesselId], disposeCancellation.Token);

        initialVesselLabel = names.GetValueOrDefault(vesselId);
    }

    protected override Task OnParametersSetAsync()
    {
        if (RefreshToken == appliedRefreshToken)
        {
            return Task.CompletedTask;
        }

        appliedRefreshToken = RefreshToken;

        return Task.WhenAll(LoadAsync(), LoadVesselsAsync());
    }

    private Task SelectVesselFilterAsync(Guid? vesselId)
    {
        vesselFilter = vesselId;

        return ApplyFiltersAsync();
    }

    private Task ViewVesselAsync(VesselReadinessOverviewRowDto vessel)
    {
        vesselFilter = vessel.VesselId;
        initialVesselLabel = vessel.VesselName;
        filterVesselPickerResetToken++;

        return ApplyFiltersAsync();
    }

    private Task ApplyFiltersAsync()
    {
        pageNumber = 1;
        vesselPageNumber = 1;

        return Task.WhenAll(LoadAsync(), LoadVesselsAsync());
    }

    private Task ClearFiltersAsync()
    {
        vesselFilter = null;
        requiredThroughFilter = null;
        statusFilter = string.Empty;
        initialVesselLabel = null;
        filterVesselPickerResetToken++;
        pageNumber = 1;
        vesselPageNumber = 1;

        return Task.WhenAll(LoadAsync(), LoadVesselsAsync());
    }

    private Task PreviousVesselPageAsync()
    {
        if (vesselPageNumber <= 1)
        {
            return Task.CompletedTask;
        }

        vesselPageNumber--;

        return LoadVesselsAsync();
    }

    private Task NextVesselPageAsync()
    {
        if (vesselPageNumber >= VesselTotalPages)
        {
            return Task.CompletedTask;
        }

        vesselPageNumber++;

        return LoadVesselsAsync();
    }

    private async Task LoadVesselsAsync()
    {
        var requestVersion = ++vesselLoadVersion;
        vesselFailure = null;

        if (vesselFilter is not null)
        {
            isLoadingVessels = false;
            vesselRows = [];
            vesselTotalCount = 0;

            return;
        }

        isLoadingVessels = true;

        DocumentsApiResult<VesselReadinessOverviewPageDto> result;

        try
        {
            result = await DocumentsApi.GetVesselReadinessOverviewAsync(
                new GetVesselReadinessOverviewQuery(requiredThroughFilter, vesselPageNumber, VesselPageSize),
                disposeCancellation.Token);
        }
        catch (OperationCanceledException) when (disposeCancellation.IsCancellationRequested)
        {
            return;
        }

        if (disposeCancellation.IsCancellationRequested || requestVersion != vesselLoadVersion)
        {
            return;
        }

        isLoadingVessels = false;

        if (!result.IsSuccess)
        {
            vesselFailure = result.Failure;
            vesselRows = [];
            vesselTotalCount = 0;

            return;
        }

        var page = result.Value!;
        vesselPageNumber = page.PageNumber;
        vesselTotalCount = page.TotalCount;
        vesselRows = page.Items;
    }

    private Task RetryAsync()
    {
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
        isLoading = true;
        failure = null;

        DocumentsApiResult<DocumentReadinessPageDto> result;

        try
        {
            result = await DocumentsApi.GetReadinessAsync(
                new GetDocumentReadinessQuery(
                    vesselFilter,
                    requiredThroughFilter,
                    string.IsNullOrEmpty(statusFilter) ? null : statusFilter,
                    pageNumber,
                    pageSize),
                disposeCancellation.Token);
        }
        catch (OperationCanceledException) when (disposeCancellation.IsCancellationRequested)
        {
            return;
        }

        if (disposeCancellation.IsCancellationRequested || requestVersion != loadVersion)
        {
            return;
        }

        if (!result.IsSuccess)
        {
            isLoading = false;
            failure = result.Failure;
            rows = [];
            summary = null;
            vesselStatus = null;
            totalCount = 0;

            return;
        }

        var page = result.Value!;
        var names = await FleetApi.LookupVesselNamesAsync(
            page.Items.Select(item => item.VesselId).Distinct().ToList(),
            disposeCancellation.Token);

        if (disposeCancellation.IsCancellationRequested || requestVersion != loadVersion)
        {
            return;
        }

        isLoading = false;
        pageNumber = page.PageNumber;
        pageSize = page.PageSize;
        totalCount = page.TotalCount;
        rows = page.Items;
        summary = page.Summary;
        vesselStatus = page.VesselStatus;
        appliedRequiredThrough = page.RequiredThrough;
        serverToday = page.Today;
        vesselNames = names;
        expandedRequirementIds.Clear();
        expandedRecipientIds.Clear();
    }

    private void ToggleAlternatives(Guid requirementId)
    {
        if (!expandedRequirementIds.Remove(requirementId))
        {
            expandedRequirementIds.Add(requirementId);
        }
    }

    private void ToggleRecipients(Guid requirementId)
    {
        if (!expandedRecipientIds.Remove(requirementId))
        {
            expandedRecipientIds.Add(requirementId);
        }
    }

    private static string DistributionShare(DocumentReadinessDistributionDto distribution, int count)
    {
        var total = distribution.AcknowledgedCount + distribution.PendingCount + distribution.OverdueCount;

        return total == 0
            ? "0%"
            : string.Create(CultureInfo.InvariantCulture, $"{count * 100.0 / total:0.##}%");
    }

    private static string DistributionLabel(DocumentReadinessDistributionDto distribution) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"{distribution.AcknowledgedCount} acknowledged, {distribution.PendingCount} pending, {distribution.OverdueCount} overdue");

    private static string RecipientStateLabel(DocumentReadinessRecipientDto recipient) => recipient switch
    {
        { IsOverdue: true } => "Overdue",
        { State: AcknowledgedRecipientState, AcknowledgedAtUtc: { } acknowledgedAt } =>
            string.Create(CultureInfo.InvariantCulture, $"Acknowledged {acknowledgedAt:dd MMM yyyy}"),
        { StateChangedAtUtc: { } changedAt } =>
            string.Create(CultureInfo.InvariantCulture, $"{recipient.State} {changedAt:dd MMM yyyy}"),
        _ => recipient.State,
    };

    private static DocumentReadinessCandidateDto? SelectedCandidate(DocumentReadinessRowDto row) =>
        row.SelectedDocumentId is { } selectedId
            ? row.Candidates.FirstOrDefault(candidate => candidate.DocumentId == selectedId)
            : null;

    private static IReadOnlyList<DocumentReadinessCandidateDto> AlternativeCandidates(DocumentReadinessRowDto row) =>
        row.Candidates.Where(candidate => candidate.DocumentId != row.SelectedDocumentId).ToList();

    private string VesselName(Guid vesselId) =>
        vesselNames.TryGetValue(vesselId, out var name) ? name : vesselId.ToString();

    private static string BlockerLabel(string blocker) =>
        BlockerLabels.TryGetValue(blocker, out var label) ? label : blocker;

    private static string VesselStatusLabel(string status) => status switch
    {
        ReadyVesselStatus => "Ready",
        NotReadyVesselStatus => "Not ready",
        NotConfiguredVesselStatus => "Not configured",
        _ => status,
    };

    private static string VesselStatusClass(string status) => status switch
    {
        ReadyVesselStatus => "ready",
        NotReadyVesselStatus => "not-ready",
        _ => "not-configured",
    };

    private static string FormatCount(int? value) =>
        value?.ToString("N0", CultureInfo.InvariantCulture) ?? "-";

    private static string FormatDate(DateOnly? value) =>
        value?.ToString("dd MMM yyyy", CultureInfo.InvariantCulture) ?? "-";

    private static string FormatInputDate(DateOnly value) =>
        value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
