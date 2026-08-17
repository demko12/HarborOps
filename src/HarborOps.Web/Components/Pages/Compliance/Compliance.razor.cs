using System.ComponentModel.DataAnnotations;
using HarborOps.Web.Api.Compliance;
using Microsoft.AspNetCore.Components.Forms;

namespace HarborOps.Web.Components.Pages.Compliance;

public partial class Compliance
{
    private Guid? selectedVesselId;
    private ComplianceOverview? overview;
    private ComplianceApiFailure? overviewFailure;
    private bool isOverviewLoading;
    private CancellationTokenSource? overviewCts;
    private int overviewVersion;
    private bool isCertificateFormOpen;
    private bool isPscFormOpen;

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

    protected override void OnInitialized()
    {
        ResetCertificateForm();
        ResetPscForm();
    }

    private async Task SelectVesselAsync(Guid? vesselId)
    {
        selectedVesselId = vesselId;
        overview = null;
        overviewFailure = null;
        certificateSuccess = pscSuccess = null;
        if (vesselId is not null) await LoadOverviewAsync();
    }

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
            overview = result.Value;
            overviewFailure = result.Failure;
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested) { }
        finally
        {
            if (version == overviewVersion) isOverviewLoading = false;
        }
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
            Guid.TryParse(pscModel.PortIdText, out var portId);
            var result = await ComplianceApi.RegisterPscInspectionAsync(new(vesselId, portId,
                pscModel.InspectionDate, pscModel.InspectingAuthority, pscModel.DeficiencyCount,
                pscModel.VesselDetained), CancellationToken.None);
            if (!result.IsSuccess) { ApplyErrors(result.Failure!, pscEditContext, pscMessages, PscFieldMap, value => pscError = value); return; }
            ResetPscForm();
            pscSuccess = "PSC inspection registered successfully.";
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

    private static bool IsExpired(ComplianceCertificateItem item) => item.ExpiresOn < DateOnly.FromDateTime(DateTime.Today);
    private static bool IsCritical(OpenNonConformityItem item) => string.Equals(item.Severity, "Critical", StringComparison.OrdinalIgnoreCase);

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
        ["portId"] = nameof(PscFormModel.PortIdText),
        ["inspectionDate"] = nameof(PscFormModel.InspectionDate),
        ["inspectingAuthority"] = nameof(PscFormModel.InspectingAuthority),
        ["deficiencyCount"] = nameof(PscFormModel.DeficiencyCount),
        ["vesselDetained"] = nameof(PscFormModel.VesselDetained),
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
        [Required, RegularExpression("^[0-9a-fA-F-]{36}$", ErrorMessage = "Port ID must be a valid GUID.")] public string? PortIdText { get; set; }
        [Required] public DateOnly? InspectionDate { get; set; }
        [Required, StringLength(200)] public string? InspectingAuthority { get; set; }
        [Required, Range(0, int.MaxValue)] public int? DeficiencyCount { get; set; }
        public bool VesselDetained { get; set; }
    }
}
