using HarborOps.Web.Api;
using Microsoft.AspNetCore.Components;

namespace HarborOps.Web.Components.Pages.Crew;

public partial class RenewCertificateForm
{
    private static readonly IReadOnlyDictionary<string, string[]> EmptyErrors =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);

    [Parameter, EditorRequired]
    public Guid CertificateId { get; set; }

    [Parameter, EditorRequired]
    public string Type { get; set; } = string.Empty;

    [Parameter, EditorRequired]
    public string Name { get; set; } = string.Empty;

    [Parameter, EditorRequired]
    public string IssuingAuthority { get; set; } = string.Empty;

    [Parameter, EditorRequired]
    public string LicenseNumber { get; set; } = string.Empty;

    [Parameter, EditorRequired]
    public DateOnly ExpiryDate { get; set; }

    [Parameter]
    public EventCallback OnRenewed { get; set; }

    [Parameter]
    public EventCallback OnCancelled { get; set; }

    private FormModel model = new();
    private bool isSubmitting;
    private string? conflictMessage;
    private string? generalError;
    private IReadOnlyList<string> fieldErrors = [];

    protected override void OnParametersSet()
    {
        // Renewal captures the existing certificate's fields as a starting point; the manager
        // adjusts only what actually changed (e.g. the new expiry date).
        model = new FormModel
        {
            Name = Name,
            IssuingAuthority = IssuingAuthority,
            LicenseNumber = LicenseNumber,
            IssueDate = ExpiryDate,
        };
    }

    private async Task SubmitAsync()
    {
        if (isSubmitting)
        {
            return;
        }

        conflictMessage = null;
        generalError = null;
        fieldErrors = [];
        isSubmitting = true;

        var request = new RenewCrewCertificateRequest
        {
            Name = model.Name,
            IssuingAuthority = model.IssuingAuthority,
            LicenseNumber = model.LicenseNumber,
            IssueDate = model.IssueDate,
            ExpiryDate = model.ExpiryDate,
        };

        var result = await CrewApi.RenewCertificateAsync(CertificateId, request, CancellationToken.None);

        isSubmitting = false;

        if (!result.IsSuccess)
        {
            var failure = result.Failure!;

            if (failure.IsConflict)
            {
                conflictMessage = failure.Message;
            }
            else if (failure.IsValidation)
            {
                fieldErrors = (failure.Errors ?? EmptyErrors).Values.SelectMany(messages => messages).ToList();
            }
            else
            {
                generalError = failure.Message;
            }

            return;
        }

        await OnRenewed.InvokeAsync();
    }

    private async Task Cancel() => await OnCancelled.InvokeAsync();

    private sealed class FormModel
    {
        public string? Name { get; set; }
        public string? IssuingAuthority { get; set; }
        public string? LicenseNumber { get; set; }
        public DateOnly? IssueDate { get; set; }
        public DateOnly? ExpiryDate { get; set; }
    }
}
