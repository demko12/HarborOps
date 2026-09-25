namespace HarborOps.Modules.Documents.Domain;

internal static class DocumentReadinessRules
{
    internal const int RequiredThroughHorizonDays = 365;

    internal const string SatisfiedStatus = "Satisfied";
    internal const string UnsatisfiedStatus = "Unsatisfied";

    internal const string ReadyVesselStatus = "Ready";
    internal const string NotReadyVesselStatus = "NotReady";
    internal const string NotConfiguredVesselStatus = "NotConfigured";

    internal const string NotIssuedDistributionStatus = "NotIssued";
    internal const string IssuedDistributionStatus = "Issued";

    internal const string Missing = "Missing";
    internal const string NotYetEffective = "NotYetEffective";
    internal const string InsufficientValidity = "InsufficientValidity";
    internal const string NotApproved = "NotApproved";
    internal const string MissingFile = "MissingFile";
    internal const string InactiveOrRetained = "InactiveOrRetained";

    private static readonly string[] BlockerOrder =
    [
        Missing,
        InactiveOrRetained,
        NotApproved,
        MissingFile,
        NotYetEffective,
        InsufficientValidity,
    ];

    internal static string VesselStatus(int configuredCount, int satisfiedCount) =>
        configuredCount == 0
            ? NotConfiguredVesselStatus
            : satisfiedCount == configuredCount
                ? ReadyVesselStatus
                : NotReadyVesselStatus;

    internal static DateOnly ValidityThreshold(DateOnly requiredThrough, int minimumRemainingValidityDays) =>
        requiredThrough.AddDays(minimumRemainingValidityDays);

    internal static IReadOnlyList<string> EvaluateDocument(
        DocumentReadinessCandidate candidate,
        DateOnly today,
        DateOnly validityThreshold)
    {
        var blockers = new List<string>();

        if (!string.Equals(candidate.Status, Document.ActiveStatus, StringComparison.Ordinal)
            || !string.Equals(candidate.RetentionState, DocumentRetentionState.ActiveState, StringComparison.Ordinal))
        {
            blockers.Add(InactiveOrRetained);
        }

        if (!string.Equals(candidate.ReviewStatus, Document.ApprovedReviewStatus, StringComparison.Ordinal))
        {
            blockers.Add(NotApproved);
        }

        if (!candidate.HasFile)
        {
            blockers.Add(MissingFile);
        }

        if (candidate.IssueDate > today)
        {
            blockers.Add(NotYetEffective);
        }

        if (candidate.ExpiryDate < validityThreshold)
        {
            blockers.Add(InsufficientValidity);
        }

        return blockers;
    }

    internal static IReadOnlyList<DocumentReadinessCandidate> EvaluatedCandidates(
        IReadOnlyList<DocumentReadinessCandidate> documentsOfType)
    {
        var current = documentsOfType
            .Where(candidate => !string.Equals(candidate.Status, Document.SupersededStatus, StringComparison.Ordinal))
            .ToList();

        return current.Count > 0 ? current : documentsOfType;
    }

    internal static IReadOnlyList<string> CombineBlockers(IEnumerable<IReadOnlyList<string>> documentBlockers)
    {
        var present = documentBlockers.SelectMany(blockers => blockers).ToHashSet(StringComparer.Ordinal);

        return BlockerOrder.Where(present.Contains).ToList();
    }

    internal static IOrderedEnumerable<T> OrderForDisplay<T>(
        IEnumerable<T> candidates,
        Func<T, DateOnly> expiryDate,
        Func<T, DateOnly> issueDate,
        Func<T, Guid> id) =>
        candidates
            .OrderByDescending(expiryDate)
            .ThenByDescending(issueDate)
            .ThenBy(id);
}

internal sealed record DocumentReadinessCandidate(
    Guid DocumentId,
    string Number,
    string Status,
    string ReviewStatus,
    bool HasFile,
    string RetentionState,
    DateOnly IssueDate,
    DateOnly ExpiryDate,
    Guid? FileRevisionId = null,
    Guid ConcurrencyToken = default);
