using System.Globalization;
using HarborOps.Web.Api.Compliance;
using Microsoft.AspNetCore.Components;

namespace HarborOps.Web.Components.Pages.Compliance;

public partial class ComplianceCertificateDetails
{
    private const string RenewNumberField = "certificateNumber";
    private const string RenewAuthorityField = "issuingAuthority";
    private const string RenewIssuedOnField = "issuedOn";
    private const string RenewExpiresOnField = "expiresOn";
    private const string RevokeReasonField = "reason";
    private const int RevocationReasonMinLength = 10;
    private const int RevocationReasonMaxLength = 1000;

    private ComplianceCertificateDetail? certificate;
    private string? vesselName;
    private ComplianceApiFailure? failure;
    private ComplianceApiFailure? actionFailure;
    private bool isLoading = true;
    private bool isSubmitting;
    private bool isConfirming;
    private string? activeAction;
    private string? actionSuccess;
    private IReadOnlyDictionary<string, string[]> fieldErrors = new Dictionary<string, string[]>();
    private RenewFormModel renewModel = new();
    private RevokeFormModel revokeModel = new();

    [Parameter] public Guid CertificateId { get; set; }

    protected override async Task OnParametersSetAsync()
    {
        CloseAction();
        actionSuccess = null;
        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        isLoading = true;
        failure = null;
        try
        {
            var result = await ComplianceApi.GetCertificateAsync(CertificateId, CancellationToken.None);
            if (result.IsSuccess)
            {
                certificate = result.Value;
                if (certificate is not null)
                {
                    var vessels = await FleetApi.LookupVesselNamesAsync([certificate.VesselId], CancellationToken.None);
                    vesselName = vessels.TryGetValue(certificate.VesselId, out var name) ? name : null;
                }
            }
            else
            {
                certificate = null;
                failure = result.Failure;
            }
        }
        finally
        {
            isLoading = false;
        }
    }

    private async Task ReloadAfterConflictAsync()
    {
        actionFailure = null;
        isConfirming = false;
        await LoadAsync();
        if (certificate is null || (activeAction == "renew" && !certificate.CanRenew) || (activeAction == "revoke" && !certificate.CanRevoke))
        {
            activeAction = null;
        }
    }

    private void OpenAction(string action)
    {
        activeAction = action;
        actionFailure = null;
        actionSuccess = null;
        isConfirming = false;
        fieldErrors = new Dictionary<string, string[]>();
        renewModel = new() { IssuingAuthority = certificate?.IssuingAuthority };
        revokeModel = new();
    }

    private void CloseAction()
    {
        activeAction = null;
        actionFailure = null;
        isConfirming = false;
        fieldErrors = new Dictionary<string, string[]>();
    }

    private void SubmitRenew()
    {
        actionFailure = null;
        var errors = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(renewModel.CertificateNumber)) errors[RenewNumberField] = ["Certificate number is required."];
        if (string.IsNullOrWhiteSpace(renewModel.IssuingAuthority)) errors[RenewAuthorityField] = ["Issuing authority is required."];
        if (renewModel.IssuedOn is null) errors[RenewIssuedOnField] = ["Issued on date is required."];
        if (renewModel.ExpiresOn is null) errors[RenewExpiresOnField] = ["Expires on date is required."];
        else if (renewModel.IssuedOn is not null && renewModel.ExpiresOn < renewModel.IssuedOn) errors[RenewExpiresOnField] = ["Expires on date must not be earlier than issued on date."];
        else if (certificate is not null && renewModel.ExpiresOn <= certificate.ExpiresOn) errors[RenewExpiresOnField] = ["Expires on date must be later than the renewed certificate's expiry date."];
        if (certificate is not null && renewModel.IssuedOn < certificate.IssuedOn) errors[RenewIssuedOnField] = ["Issued on date must not be earlier than the renewed certificate's issued on date."];

        fieldErrors = errors;
        isConfirming = errors.Count == 0;
    }

    private void SubmitRevoke()
    {
        actionFailure = null;
        var errors = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(revokeModel.Reason)) errors[RevokeReasonField] = ["A revocation reason is required."];
        else if (revokeModel.Reason.Trim().Length < RevocationReasonMinLength) errors[RevokeReasonField] = [$"Reason must be at least {RevocationReasonMinLength} characters."];
        else if (revokeModel.Reason.Trim().Length > RevocationReasonMaxLength) errors[RevokeReasonField] = [$"Reason must not exceed {RevocationReasonMaxLength} characters."];

        fieldErrors = errors;
        isConfirming = errors.Count == 0;
    }

    private async Task ConfirmRenewAsync()
    {
        if (isSubmitting || certificate is null) return;
        isSubmitting = true;
        try
        {
            var result = await ComplianceApi.RenewCertificateAsync(
                certificate.Id,
                new(certificate.ConcurrencyToken, renewModel.CertificateNumber, renewModel.IssuingAuthority, renewModel.IssuedOn, renewModel.ExpiresOn),
                CancellationToken.None);
            if (!result.IsSuccess)
            {
                ApplyFailure(result.Failure!);
                return;
            }

            actionSuccess = $"Renewal {renewModel.CertificateNumber} registered.";
            activeAction = null;
            isConfirming = false;
            await LoadAsync();
        }
        finally
        {
            isSubmitting = false;
        }
    }

    private async Task ConfirmRevokeAsync()
    {
        if (isSubmitting || certificate is null) return;
        isSubmitting = true;
        try
        {
            var result = await ComplianceApi.RevokeCertificateAsync(
                certificate.Id,
                new(certificate.ConcurrencyToken, revokeModel.Reason),
                CancellationToken.None);
            if (!result.IsSuccess)
            {
                ApplyFailure(result.Failure!);
                return;
            }

            actionSuccess = "Certificate revoked.";
            activeAction = null;
            isConfirming = false;
            await LoadAsync();
        }
        finally
        {
            isSubmitting = false;
        }
    }

    private void ApplyFailure(ComplianceApiFailure apiFailure)
    {
        isConfirming = false;
        if (apiFailure.IsValidation && apiFailure.Errors.Count > 0)
        {
            fieldErrors = apiFailure.Errors;
            var unmapped = apiFailure.Errors
                .Where(error => error.Key is not (RenewNumberField or RenewAuthorityField or RenewIssuedOnField or RenewExpiresOnField or RevokeReasonField))
                .SelectMany(error => error.Value)
                .ToList();
            actionFailure = unmapped.Count > 0 ? apiFailure with { Message = string.Join(" ", unmapped) } : null;
            return;
        }

        actionFailure = apiFailure;
    }

    private IEnumerable<string> FieldMessages(string field) =>
        fieldErrors.FirstOrDefault(error => string.Equals(error.Key, field, StringComparison.OrdinalIgnoreCase)).Value ?? [];

    private string ChainNumber(Guid certificateId) =>
        certificate?.Chain.FirstOrDefault(item => item.Id == certificateId)?.CertificateNumber ?? certificateId.ToString();

    private bool IsCoveredByRenewal(Guid certificateId)
    {
        var chain = certificate?.Chain ?? [];
        for (var index = 0; index + 1 < chain.Count; index++)
        {
            if (chain[index].Id == certificateId)
            {
                return chain[index + 1].CoversPredecessorExpiry;
            }
        }

        return false;
    }

    private string? ActiveSuccessorNumber() =>
        certificate?.Chain.FirstOrDefault(item => item.Id == certificate.SuccessorCertificateId && item.RevokedAtUtc is null)?.CertificateNumber;

    private string ValidityNote()
    {
        if (certificate is null)
        {
            return string.Empty;
        }

        if (certificate.RevokedAtUtc is { } revokedAtUtc)
        {
            return $"Revoked {revokedAtUtc.ToString("dd MMM yyyy", CultureInfo.InvariantCulture)}";
        }

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        if (certificate.IssuedOn > today)
        {
            return $"Effective in {DayCount(certificate.IssuedOn.DayNumber - today.DayNumber)}";
        }

        return certificate.ExpiresOn < today
            ? $"Expired {DayCount(today.DayNumber - certificate.ExpiresOn.DayNumber)} ago"
            : $"{DayCount(certificate.ExpiresOn.DayNumber - today.DayNumber + 1)} of validity left";
    }

    private string ChainPositionNote()
    {
        if (certificate is null)
        {
            return string.Empty;
        }

        var position = certificate.Chain.Select(item => item.Id).ToList().IndexOf(certificate.Id) + 1;

        return certificate.Chain.Count == 1 ? "Not renewed yet" : $"This is certificate {position} of {certificate.Chain.Count}";
    }

    private static string DayCount(int days) => days == 1 ? "1 day" : $"{days} days";

    internal static int ValidityPercent(ComplianceCertificateDetail certificate, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(certificate);

        if (certificate.RevokedAtUtc is not null || certificate.ExpiresOn < today)
        {
            return 0;
        }

        if (certificate.IssuedOn > today)
        {
            return 100;
        }

        var total = certificate.ExpiresOn.DayNumber - certificate.IssuedOn.DayNumber + 1;
        var left = certificate.ExpiresOn.DayNumber - today.DayNumber + 1;

        return (int)Math.Round(Math.Clamp(left * 100d / Math.Max(total, 1), 0, 100));
    }

    internal static int CoverageGapCount(ComplianceCertificateDetail certificate)
    {
        ArgumentNullException.ThrowIfNull(certificate);

        return certificate.Chain.Count(item => item.PredecessorCertificateId is not null && !item.CoversPredecessorExpiry);
    }

    internal static string CoverageValue(ComplianceCertificateDetail certificate)
    {
        var gaps = CoverageGapCount(certificate);

        return gaps switch
        {
            0 => "No gaps",
            1 => "1 gap",
            _ => string.Create(CultureInfo.InvariantCulture, $"{gaps} gaps"),
        };
    }

    internal static string CoverageNote(ComplianceCertificateDetail certificate)
    {
        ArgumentNullException.ThrowIfNull(certificate);

        var gapDays = certificate.Chain.Sum(item => item.GapDaysAfterPredecessor is > 0 ? item.GapDaysAfterPredecessor.Value : 0);

        if (CoverageGapCount(certificate) == 0)
        {
            return certificate.Chain.Count == 1
                ? "Single certificate, not renewed yet"
                : string.Create(CultureInfo.InvariantCulture, $"Continuous since {certificate.Chain[0].IssuedOn.ToString("dd MMM yyyy", CultureInfo.InvariantCulture)}");
        }

        return gapDays > 0
            ? $"{DayCount(gapDays)} without a valid certificate"
            : "A renewal does not cover the previous expiry";
    }

    internal static string HistoryEventClass(string eventType) => eventType switch
    {
        "Revoked" => "revoked",
        "Renewed" => "renewed",
        _ => "other",
    };

    internal static string HistoryEventIcon(string eventType) => eventType switch
    {
        "Revoked" => "bi-x-octagon",
        "Renewed" => "bi-arrow-repeat",
        _ => "bi-clock-history",
    };

    internal static CertificateTimeline BuildTimeline(IReadOnlyList<ComplianceCertificateChainItem> chain, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(chain);

        if (chain.Count == 0)
        {
            return new CertificateTimeline([], [], null);
        }

        var firstYear = chain.Min(item => item.IssuedOn).Year;
        var lastYear = Math.Max(chain.Max(item => item.ExpiresOn).Year, today.Year) + 1;
        var start = new DateOnly(firstYear, 1, 1);
        var end = new DateOnly(lastYear, 1, 1);
        var totalDays = (double)(end.DayNumber - start.DayNumber);

        double Percent(DateOnly day) => Math.Clamp((day.DayNumber - start.DayNumber) * 100d / totalDays, 0, 100);

        var years = lastYear - firstYear;
        var step = Math.Max(1, (int)Math.Ceiling(years / 8d));
        var ticks = new List<CertificateTimelineTick>();
        for (var year = firstYear; year < lastYear; year += step)
        {
            ticks.Add(new CertificateTimelineTick(year.ToString(CultureInfo.InvariantCulture), Percent(new DateOnly(year, 1, 1))));
        }

        var rows = new List<CertificateTimelineRow>();
        for (var index = 0; index < chain.Count; index++)
        {
            var item = chain[index];
            var left = Percent(item.IssuedOn);
            var width = Math.Max(Percent(item.ExpiresOn.AddDays(1)) - left, 0.5);
            double? gapLeft = null;
            double? gapWidth = null;

            if (index > 0 && item.GapDaysAfterPredecessor is > 0)
            {
                gapLeft = Percent(chain[index - 1].ExpiresOn.AddDays(1));
                gapWidth = Math.Max(left - gapLeft.Value, 0.6);
            }

            var placement = left + width <= 70 ? "after" : left >= 25 ? "before" : "inside";
            rows.Add(new CertificateTimelineRow(item, left, width, gapLeft, gapWidth, placement));
        }

        double? todayPercent = today >= start && today < end ? Percent(today) : null;

        return new CertificateTimeline(ticks, rows, todayPercent);
    }

    internal static string FormatPercent(double value) => string.Create(CultureInfo.InvariantCulture, $"{value:0.###}%");

    internal static string FormatFraction(double percent) => (percent / 100).ToString("0.#####", CultureInfo.InvariantCulture);

    internal sealed record CertificateTimelineTick(string Label, double LeftPercent);

    internal sealed record CertificateTimelineRow(
        ComplianceCertificateChainItem Item,
        double LeftPercent,
        double WidthPercent,
        double? GapLeftPercent,
        double? GapWidthPercent,
        string LabelPlacement);

    internal sealed record CertificateTimeline(
        IReadOnlyList<CertificateTimelineTick> Ticks,
        IReadOnlyList<CertificateTimelineRow> Rows,
        double? TodayPercent);

    private static string DateInputValue(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string ShortUser(Guid userId)
    {
        var text = userId.ToString("D", CultureInfo.InvariantCulture);

        return $"{text[..8]}…{text[^4..]}";
    }

    private sealed class RenewFormModel
    {
        public string? CertificateNumber { get; set; }
        public string? IssuingAuthority { get; set; }
        public DateOnly? IssuedOn { get; set; }
        public DateOnly? ExpiresOn { get; set; }
    }

    private sealed class RevokeFormModel
    {
        public string? Reason { get; set; }
    }
}
