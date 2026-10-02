using System.ComponentModel.DataAnnotations;
using HarborOps.Web.Api;
using HarborOps.Web.Api.Compliance;
using HarborOps.Web.Api.Documents;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Web;

namespace HarborOps.Web.Components.Pages.Compliance;

public partial class Compliance
{
    internal const int ExpiringSoonDays = 30;
    internal const string DetainedState = "Detained";
    internal const string ReleasedState = "Released";

    [SupplyParameterFromQuery(Name = "surveyId")]
    private int? SurveyIdFromQuery { get; set; }

    [SupplyParameterFromQuery(Name = "vesselId")]
    private Guid? VesselIdFromQuery { get; set; }

    private Guid? selectedVesselId;
    private string? initialVesselLabel;
    private IReadOnlyDictionary<Guid, string> portNames = new Dictionary<Guid, string>();
    private IReadOnlyDictionary<Guid, DocumentDetailsDto> evidenceDocuments = new Dictionary<Guid, DocumentDetailsDto>();
    private ComplianceOverview? overview;
    private ComplianceApiFailure? overviewFailure;
    private bool isOverviewLoading;
    private string? operationSuccess;
    private string? surveyLookupFailureMessage;
    private CancellationTokenSource? overviewCts;
    private int overviewVersion;
    private bool isCertificateFormOpen;
    private bool isPscFormOpen;
    private bool certificateCardView;

    private CertificateFormModel certificateModel = new();
    private EditContext certificateEditContext = null!;
    private ValidationMessageStore certificateMessages = null!;
    private bool isSubmittingCertificate;
    private string? certificateSuccess;
    private string? certificateError;

    private PscFormModel pscModel = new();
    private EditContext pscEditContext = null!;
    private ValidationMessageStore pscMessages = null!;
    private bool isSubmittingPsc;
    private string? pscSuccess;
    private string? pscError;

    private bool isNonConformityFormOpen;
    private NonConformityFormModel nonConformityModel = new();
    private EditContext nonConformityEditContext = null!;
    private ValidationMessageStore nonConformityMessages = null!;
    private bool isSubmittingNonConformity;
    private string? nonConformitySuccess;
    private string? nonConformityError;
    private int nonConformityResetToken;

    private bool isInsurancePolicyFormOpen;
    private InsurancePolicyFormModel insurancePolicyModel = new();
    private EditContext insurancePolicyEditContext = null!;
    private ValidationMessageStore insurancePolicyMessages = null!;
    private bool isSubmittingInsurancePolicy;
    private string? insurancePolicyError;

    protected override Task OnInitializedAsync()
    {
        ResetCertificateForm();
        ResetPscForm();
        ResetNonConformityForm();
        ResetInsurancePolicyForm();

        return SurveyIdFromQuery is not null
            ? ApplySurveyQueryParameterAsync()
            : ApplyVesselQueryParameterAsync();
    }

    private async Task ApplyVesselQueryParameterAsync()
    {
        if (VesselIdFromQuery is not { } vesselId)
        {
            return;
        }

        selectedVesselId = vesselId;

        var namesTask = FleetApi.LookupVesselNamesAsync([vesselId], CancellationToken.None);
        await Task.WhenAll(namesTask, LoadOverviewAsync());

        initialVesselLabel = namesTask.Result.TryGetValue(vesselId, out var name) ? name : null;
    }

    private async Task ApplySurveyQueryParameterAsync()
    {
        if (SurveyIdFromQuery is not { } surveyId || !AuthorizationState.CanManageCompliance)
        {
            return;
        }

        var result = await ComplianceApi.GetSurveyAsync(surveyId, CancellationToken.None);
        if (!result.IsSuccess || result.Value is null)
        {
            surveyLookupFailureMessage = "The referenced survey could not be loaded.";
            return;
        }

        selectedVesselId = result.Value.VesselId;
        nonConformityModel.SourceType = "Survey";
        nonConformityModel.SurveyId = surveyId;
        isNonConformityFormOpen = true;

        await LoadOverviewAsync();
    }

    private void OpenCertificateForm()
    {
        certificateError = certificateSuccess = null;
        isCertificateFormOpen = true;
    }

    private void CloseCertificateForm() => isCertificateFormOpen = false;

    private void OpenPscForm()
    {
        pscError = pscSuccess = null;
        isPscFormOpen = true;
    }

    private void ClosePscForm() => isPscFormOpen = false;

    private void OpenNonConformityForm()
    {
        nonConformityError = nonConformitySuccess = null;
        isNonConformityFormOpen = true;
    }

    private void CloseNonConformityForm() => isNonConformityFormOpen = false;

    private void OpenInsurancePolicyForm()
    {
        insurancePolicyError = null;
        isInsurancePolicyFormOpen = true;
    }

    private void CloseInsurancePolicyForm() => isInsurancePolicyFormOpen = false;

    private async Task SelectVesselAsync(Guid? vesselId)
    {
        selectedVesselId = vesselId;
        overview = null;
        overviewFailure = null;
        certificateSuccess = pscSuccess = operationSuccess = null;
        Navigation.NavigateTo(vesselId is { } id ? OverviewHref(id) : "/compliance", replace: true);
        if (vesselId is not null) await LoadOverviewAsync();
    }

    internal static string OverviewHref(Guid vesselId) => $"/compliance?vesselId={vesselId}";

    private async Task LoadOverviewAsync()
    {
        if (selectedVesselId is not { } vesselId) return;
        overviewCts?.Cancel();
        overviewCts?.Dispose();
        var cts = new CancellationTokenSource();
        overviewCts = cts;
        var version = ++overviewVersion;
        isOverviewLoading = true;
        overviewFailure = null;
        overview = null;
        try
        {
            var result = await ComplianceApi.GetOverviewAsync(vesselId, cts.Token);
            if (cts.IsCancellationRequested || version != overviewVersion || selectedVesselId != vesselId) return;
            if (result.Value is { } loaded)
            {
                await ResolveOverviewReferencesAsync(loaded, cts.Token);
                if (cts.IsCancellationRequested || version != overviewVersion) return;
            }

            overview = result.Value;
            overviewFailure = result.Failure;
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested) { }
        finally
        {
            if (version == overviewVersion) isOverviewLoading = false;
        }
    }

    private async Task ResolveOverviewReferencesAsync(ComplianceOverview loaded, CancellationToken cancellationToken)
    {
        var portIds = loaded.PscInspections.Select(item => item.PortId).Distinct().ToArray();
        var portsTask = VoyagesApi.LookupPortNamesAsync(portIds, cancellationToken);
        var documentTasks = loaded.OpenNonConformities
            .Select(item => item.EvidenceDocumentId)
            .Distinct()
            .Select(documentId => DocumentsApi.GetDocumentByIdAsync(documentId, cancellationToken))
            .ToArray();

        await Task.WhenAll(documentTasks.Append<Task>(portsTask));

        portNames = portsTask.Result;
        evidenceDocuments = documentTasks
            .Select(task => task.Result.Value)
            .OfType<DocumentDetailsDto>()
            .ToDictionary(document => document.Id);
    }

    private Task RetryOverviewAsync() => LoadOverviewAsync();

    private async Task SubmitCertificateAsync()
    {
        if (isSubmittingCertificate || selectedVesselId is not { } vesselId) return;
        certificateMessages.Clear(); certificateError = certificateSuccess = null;
        if (!certificateEditContext.Validate()) return;
        isSubmittingCertificate = true;
        try
        {
            var result = await ComplianceApi.RegisterCertificateAsync(new(vesselId,
                certificateModel.CertificateType, certificateModel.CertificateNumber,
                certificateModel.IssuingAuthority, certificateModel.IssuedOn, certificateModel.ExpiresOn), CancellationToken.None);
            if (!result.IsSuccess) { ApplyErrors(result.Failure!, certificateEditContext, certificateMessages, CertificateFieldMap, value => certificateError = value); return; }
            ResetCertificateForm();
            certificateSuccess = "Certificate registered successfully.";
            operationSuccess = certificateSuccess;
            isCertificateFormOpen = false;
            await LoadOverviewAsync();
        }
        finally { isSubmittingCertificate = false; }
    }

    private async Task SubmitPscAsync()
    {
        if (isSubmittingPsc || selectedVesselId is not { } vesselId) return;
        pscMessages.Clear(); pscError = pscSuccess = null;
        if (!pscEditContext.Validate()) return;
        isSubmittingPsc = true;
        try
        {
            var result = await ComplianceApi.RegisterPscInspectionAsync(new(vesselId, pscModel.PortId,
                pscModel.InspectionDate, pscModel.InspectingAuthority, pscModel.DeficiencyCount,
                pscModel.VesselDetained), CancellationToken.None);
            if (!result.IsSuccess) { ApplyErrors(result.Failure!, pscEditContext, pscMessages, PscFieldMap, value => pscError = value); return; }
            ResetPscForm();
            pscSuccess = "PSC inspection registered successfully.";
            operationSuccess = pscSuccess;
            isPscFormOpen = false;
            await LoadOverviewAsync();
        }
        finally { isSubmittingPsc = false; }
    }

    private void ResetCertificateForm()
    {
        certificateModel = new(); certificateEditContext = new(certificateModel);
        certificateMessages = new(certificateEditContext);
    }

    private void ResetPscForm()
    {
        pscModel = new(); pscEditContext = new(pscModel); pscMessages = new(pscEditContext);
    }

    private void SetPscPort(Guid? portId)
    {
        pscModel.PortId = portId;
        pscEditContext.NotifyFieldChanged(new FieldIdentifier(pscModel, nameof(PscFormModel.PortId)));
    }

    private async Task SubmitNonConformityAsync()
    {
        if (isSubmittingNonConformity || selectedVesselId is not { } vesselId) return;
        nonConformityMessages.Clear();
        nonConformityError = nonConformitySuccess = null;
        if (!nonConformityEditContext.Validate()) return;
        isSubmittingNonConformity = true;
        try
        {
            var request = new RegisterNonConformityRequest(
                nonConformityModel.SourceType == "Survey" ? nonConformityModel.SurveyId : null,
                nonConformityModel.SourceType == "Psc" ? nonConformityModel.PscId : null,
                nonConformityModel.EvidenceDocumentId,
                nonConformityModel.Severity,
                nonConformityModel.CorrectiveActionDeadline);
            var result = await ComplianceApi.RegisterNonConformityAsync(request, CancellationToken.None);
            if (!result.IsSuccess)
            {
                ApplyErrors(result.Failure!, nonConformityEditContext, nonConformityMessages, NonConformityFieldMap, value => nonConformityError = value);
                return;
            }

            ResetNonConformityForm();
            nonConformitySuccess = "Non-conformity registered successfully.";
            operationSuccess = nonConformitySuccess;
            isNonConformityFormOpen = false;
            await LoadOverviewAsync();
        }
        finally
        {
            isSubmittingNonConformity = false;
        }
    }

    private void ResetNonConformityForm()
    {
        nonConformityModel = new();
        nonConformityEditContext = new(nonConformityModel);
        nonConformityMessages = new(nonConformityEditContext);
        nonConformityResetToken++;
    }

    private async Task SubmitInsurancePolicyAsync()
    {
        if (isSubmittingInsurancePolicy || selectedVesselId is not { } vesselId) return;
        insurancePolicyMessages.Clear();
        insurancePolicyError = null;
        if (!insurancePolicyEditContext.Validate()) return;
        isSubmittingInsurancePolicy = true;
        try
        {
            var result = await ComplianceApi.RegisterInsurancePolicyAsync(
                new(vesselId, insurancePolicyModel.Insurer, insurancePolicyModel.PolicyNumber, insurancePolicyModel.Coverage, insurancePolicyModel.ExpiryDate),
                CancellationToken.None);
            if (!result.IsSuccess)
            {
                ApplyErrors(result.Failure!, insurancePolicyEditContext, insurancePolicyMessages, InsurancePolicyFieldMap, value => insurancePolicyError = value);
                return;
            }

            ResetInsurancePolicyForm();
            operationSuccess = "Insurance policy registered successfully.";
            isInsurancePolicyFormOpen = false;
            await LoadOverviewAsync();
        }
        finally
        {
            isSubmittingInsurancePolicy = false;
        }
    }

    private void ResetInsurancePolicyForm()
    {
        insurancePolicyModel = new();
        insurancePolicyEditContext = new(insurancePolicyModel);
        insurancePolicyMessages = new(insurancePolicyEditContext);
    }

    private static void ApplyErrors(ComplianceApiFailure failure, EditContext context,
        ValidationMessageStore store, IReadOnlyDictionary<string, string> map, Action<string?> setGeneral)
    {
        var unmappedMessages = new List<string>();
        foreach (var entry in failure.Errors)
        {
            if (!map.TryGetValue(entry.Key, out var property))
            {
                unmappedMessages.AddRange(entry.Value);
                continue;
            }

            store.Add(new FieldIdentifier(context.Model, property), entry.Value);
        }

        if (!failure.IsValidation)
        {
            setGeneral(failure.Title);
        }
        else if (unmappedMessages.Count > 0)
        {
            setGeneral(string.Join(" ", unmappedMessages));
        }

        context.NotifyValidationStateChanged();
    }

    private static bool IsCurrent(ComplianceCertificateItem item) => item.Status == "Current";
    private static bool IsUncoveredExpired(ComplianceCertificateItem item) => item.Status == "Expired" && !item.IsRenewalCovered;
    private static bool IsRevoked(ComplianceCertificateItem item) => item.Status == "Revoked";
    private static bool IsAtRisk(ComplianceCertificateItem item) => IsUncoveredExpired(item) || IsRevoked(item);
    private static string CertificateHref(Guid certificateId) => $"/compliance/certificates/{certificateId}";

    private void OpenCertificate(Guid certificateId) => Navigation.NavigateTo(CertificateHref(certificateId));

    private void HandleCertificateRowKeyDown(KeyboardEventArgs args, Guid certificateId)
    {
        if (args.Key is "Enter" or " ")
        {
            OpenCertificate(certificateId);
        }
    }

    internal static string CertificateStatusLabel(string status, bool isRenewalCovered = false) => status switch
    {
        "Current" => "Valid",
        "NotYetEffective" => "Not yet effective",
        "Expired" when isRenewalCovered => "Expired · renewed",
        _ => status,
    };

    internal static string CertificateStatusClass(string status, bool isRenewalCovered = false) => status switch
    {
        "Current" => "success",
        "Expired" when isRenewalCovered => "neutral",
        "Expired" or "Revoked" => "danger",
        _ => "neutral",
    };
    private static bool IsCritical(OpenNonConformityItem item) => string.Equals(item.Severity, "Critical", StringComparison.OrdinalIgnoreCase);

    private static DateOnly Today() => DateOnly.FromDateTime(DateTime.UtcNow);

    internal static bool IsExpiringSoon(ComplianceCertificateItem item, DateOnly today) =>
        IsCurrent(item) && !item.IsRenewalCovered && item.ExpiresOn <= today.AddDays(ExpiringSoonDays);

    internal static string? ExpiryCountdown(ComplianceCertificateItem item, DateOnly today)
    {
        var days = item.ExpiresOn.DayNumber - today.DayNumber;

        return item.Status switch
        {
            "Current" when days == 0 => "Expires today",
            "Current" => days == 1 ? "Expires in 1 day" : $"Expires in {days} days",
            "Expired" => days == -1 ? "Expired 1 day ago" : $"Expired {-days} days ago",
            _ => null,
        };
    }

    internal static string ValidityTone(ComplianceCertificateItem item, DateOnly today) => item.Status switch
    {
        "Revoked" => "danger",
        "Expired" => item.IsRenewalCovered ? "neutral" : "danger",
        "Current" when IsExpiringSoon(item, today) => "warning",
        "Current" => "success",
        _ => "neutral",
    };

    internal static int ValidityElapsedPercent(DateOnly issuedOn, DateOnly expiresOn, DateOnly today)
    {
        var totalDays = expiresOn.DayNumber - issuedOn.DayNumber;
        if (totalDays <= 0) return today >= expiresOn ? 100 : 0;

        return Math.Clamp((int)Math.Round((today.DayNumber - issuedOn.DayNumber) * 100d / totalDays), 0, 100);
    }

    internal static int OverdueDays(OpenNonConformityItem item, DateOnly today) =>
        Math.Max(0, today.DayNumber - item.CorrectiveActionDeadline.DayNumber);

    internal static string FormatOverdue(int days) => days == 1 ? "1 day overdue" : $"{days} days overdue";

    private string PortName(Guid portId) =>
        portNames.TryGetValue(portId, out var name) ? name : "Unknown port";

    private string EvidenceDocumentLabel(Guid documentId) =>
        evidenceDocuments.TryGetValue(documentId, out var document)
            ? $"{document.DocumentTypeName} — {document.Number}"
            : "Unknown document";

    private string? EvidenceDownloadHref(Guid documentId) =>
        evidenceDocuments.TryGetValue(documentId, out var document)
            && document.HasFile
            && AuthorizationState.HasPermission(CurrentUserAuthorizationState.DocumentsReadPermission)
                ? $"/documents/{documentId}/file/download"
                : null;

    internal static string HealthTone(ComplianceOverview overview, DateOnly today)
    {
        if (overview.Certificates.Any(IsUncoveredExpired) || overview.OpenNonConformities.Any(item => OverdueDays(item, today) > 0))
        {
            return "danger";
        }

        return overview.Certificates.Any(item => IsExpiringSoon(item, today)) ? "warning" : "success";
    }

    internal static string? DetentionState(PscInspectionItem item) =>
        item.DetentionState ?? (item.VesselDetained ? DetainedState : null);

    private static string DetentionBadgeClass(string? state) => state switch
    {
        DetainedState => "danger",
        ReleasedState => "neutral",
        _ => "success",
    };

    private static string DetentionLabel(string? state) => state switch
    {
        DetainedState => "Detained",
        ReleasedState => "Released",
        _ => "Clear",
    };

    private static string HealthIcon(string tone) => tone switch
    {
        "danger" => "bi-exclamation-octagon",
        "warning" => "bi-hourglass-split",
        _ => "bi-shield-check",
    };

    private static string HealthTitle(string tone) => tone switch
    {
        "danger" => "Action required",
        "warning" => "Attention needed",
        _ => "All clear",
    };

    internal static string HealthSummary(ComplianceOverview overview, DateOnly today)
    {
        var parts = new List<string>();
        var expired = overview.Certificates.Count(IsUncoveredExpired);
        var expiring = overview.Certificates.Count(item => IsExpiringSoon(item, today));
        var overdue = overview.OpenNonConformities.Where(item => OverdueDays(item, today) > 0).ToList();
        var overdueCritical = overdue.Count(IsCritical);
        var overdueOther = overdue.Count - overdueCritical;

        if (expired > 0) parts.Add(expired == 1 ? "1 certificate expired" : $"{expired} certificates expired");
        if (expiring > 0) parts.Add(expiring == 1 ? $"1 certificate expires within {ExpiringSoonDays} days" : $"{expiring} certificates expire within {ExpiringSoonDays} days");
        if (overdueCritical > 0) parts.Add(overdueCritical == 1 ? "1 critical finding overdue" : $"{overdueCritical} critical findings overdue");
        if (overdueOther > 0) parts.Add(overdueOther == 1 ? "1 finding overdue" : $"{overdueOther} findings overdue");

        return parts.Count == 0
            ? "No expired or expiring certificates and no overdue findings."
            : string.Join(" · ", parts);
    }

    private static readonly IReadOnlyDictionary<string, string> CertificateFieldMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["certificateType"] = nameof(CertificateFormModel.CertificateType),
        ["certificateNumber"] = nameof(CertificateFormModel.CertificateNumber),
        ["issuingAuthority"] = nameof(CertificateFormModel.IssuingAuthority),
        ["issuedOn"] = nameof(CertificateFormModel.IssuedOn),
        ["expiresOn"] = nameof(CertificateFormModel.ExpiresOn),
    };
    private static readonly IReadOnlyDictionary<string, string> PscFieldMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["portId"] = nameof(PscFormModel.PortId),
        ["inspectionDate"] = nameof(PscFormModel.InspectionDate),
        ["inspectingAuthority"] = nameof(PscFormModel.InspectingAuthority),
        ["deficiencyCount"] = nameof(PscFormModel.DeficiencyCount),
        ["vesselDetained"] = nameof(PscFormModel.VesselDetained),
    };
    private static readonly IReadOnlyDictionary<string, string> NonConformityFieldMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["surveyId"] = nameof(NonConformityFormModel.SurveyId),
        ["pscId"] = nameof(NonConformityFormModel.PscId),
        ["evidenceDocumentId"] = nameof(NonConformityFormModel.EvidenceDocumentId),
        ["severity"] = nameof(NonConformityFormModel.Severity),
        ["correctiveActionDeadline"] = nameof(NonConformityFormModel.CorrectiveActionDeadline),
    };
    private static readonly IReadOnlyDictionary<string, string> InsurancePolicyFieldMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["insurer"] = nameof(InsurancePolicyFormModel.Insurer),
        ["policyNumber"] = nameof(InsurancePolicyFormModel.PolicyNumber),
        ["coverage"] = nameof(InsurancePolicyFormModel.Coverage),
        ["expiryDate"] = nameof(InsurancePolicyFormModel.ExpiryDate),
    };

    public void Dispose() { overviewCts?.Cancel(); overviewCts?.Dispose(); }

    private sealed class CertificateFormModel
    {
        [Required, StringLength(100)] public string? CertificateType { get; set; }
        [Required, StringLength(100)] public string? CertificateNumber { get; set; }
        [Required, StringLength(200)] public string? IssuingAuthority { get; set; }
        [Required] public DateOnly? IssuedOn { get; set; }
        [Required] public DateOnly? ExpiresOn { get; set; }
    }

    private sealed class PscFormModel
    {
        [Required, Display(Name = "Port")] public Guid? PortId { get; set; }
        [Required] public DateOnly? InspectionDate { get; set; }
        [Required, StringLength(200)] public string? InspectingAuthority { get; set; }
        [Required, Range(0, int.MaxValue)] public int? DeficiencyCount { get; set; }
        public bool VesselDetained { get; set; }
    }

    private sealed class NonConformityFormModel
    {
        public string SourceType { get; set; } = "Survey";
        public int? SurveyId { get; set; }
        public int? PscId { get; set; }
        [Required] public Guid? EvidenceDocumentId { get; set; }
        [Required] public string? Severity { get; set; }
        [Required] public DateOnly? CorrectiveActionDeadline { get; set; }
    }

    private sealed class InsurancePolicyFormModel
    {
        [Required, StringLength(200)] public string? Insurer { get; set; }
        [Required, StringLength(100)] public string? PolicyNumber { get; set; }
        [Required, StringLength(200)] public string? Coverage { get; set; }
        [Required] public DateOnly? ExpiryDate { get; set; }
    }
}
