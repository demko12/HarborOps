using System.ComponentModel.DataAnnotations;
using HarborOps.Web.Api.Documents;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;

namespace HarborOps.Web.Components.Pages.Documents;

public partial class RegisterDocumentForm
{
    private static readonly IReadOnlyDictionary<string, string> RegisterDocumentServerFieldMap =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [Documents.VesselIdFieldKey] = nameof(RegisterDocumentFormModel.VesselId),
            [Documents.DocumentTypeIdFieldKey] = nameof(RegisterDocumentFormModel.DocumentTypeId),
            [Documents.DocumentNumberFieldKey] = nameof(RegisterDocumentFormModel.Number),
            [Documents.IssueDateFieldKey] = nameof(RegisterDocumentFormModel.IssueDate),
            [Documents.ExpiryDateFieldKey] = nameof(RegisterDocumentFormModel.ExpiryDate),
            [Documents.IssuingAuthorityFieldKey] = nameof(RegisterDocumentFormModel.IssuingAuthority),
        };

    private RegisterDocumentFormModel registerDocumentModel = new();
    private EditContext registerDocumentEditContext = null!;
    private ValidationMessageStore registerDocumentMessageStore = null!;
    private bool isRegisteringDocument;
    private string? registerDocumentSuccessMessage;
    private string? registerDocumentConflictMessage;
    private string? registerDocumentGeneralError;
    private IReadOnlyList<string> registerDocumentUnmappedErrors = [];

    // Bumped on every successful document registration and bound to VesselPicker's @key,
    // forcing Blazor to discard the old instance and mount a fresh one. VesselPicker has no
    // reset method of its own and does not resync its internal display text when its Value
    // parameter is set back to null from outside (see the comment on VesselPicker.Value) -
    // @key is the only reliable way to blank it from a parent.
    private int vesselPickerResetToken;
    private string? registerDocumentVesselLabel;

    [Parameter]
    public IReadOnlyList<DocumentTypeOption> DocumentTypeOptions { get; set; } = [];

    [Parameter]
    public bool IsLoadingDocumentTypes { get; set; }

    [Parameter]
    public Guid? InitialVesselId { get; set; }

    [Parameter]
    public string? InitialVesselName { get; set; }

    [Parameter]
    public int? InitialDocumentTypeId { get; set; }

    [Parameter]
    public EventCallback OnClose { get; set; }

    [Parameter]
    public EventCallback OnRegistered { get; set; }

    private string DocumentTypePlaceholder => IsLoadingDocumentTypes
        ? "Loading document types..."
        : DocumentTypeOptions.Count == 0
            ? "No document types available - register one first"
            : "Select...";

    protected override void OnInitialized()
    {
        registerDocumentModel = new RegisterDocumentFormModel
        {
            VesselId = InitialVesselId,
            DocumentTypeId = InitialDocumentTypeId,
        };
        registerDocumentEditContext = new EditContext(registerDocumentModel);
        registerDocumentMessageStore = new ValidationMessageStore(registerDocumentEditContext);
        registerDocumentVesselLabel = InitialVesselName;
    }

    private async Task SubmitDocumentAsync()
    {
        registerDocumentMessageStore.Clear();
        registerDocumentConflictMessage = null;
        registerDocumentGeneralError = null;
        registerDocumentUnmappedErrors = [];
        registerDocumentSuccessMessage = null;

        if (!registerDocumentEditContext.Validate())
        {
            return;
        }

        if (isRegisteringDocument)
        {
            return;
        }

        isRegisteringDocument = true;

        var request = new RegisterDocumentRequest
        {
            VesselId = registerDocumentModel.VesselId,
            DocumentTypeId = registerDocumentModel.DocumentTypeId,
            Number = registerDocumentModel.Number,
            IssueDate = registerDocumentModel.IssueDate,
            ExpiryDate = registerDocumentModel.ExpiryDate,
            IssuingAuthority = registerDocumentModel.IssuingAuthority,
            Status = Documents.DefaultDocumentStatus,
        };

        var result = await DocumentsApi.RegisterDocumentAsync(request, CancellationToken.None);

        isRegisteringDocument = false;

        if (!result.IsSuccess)
        {
            var documentFailure = result.Failure!;

            if (documentFailure.IsConflict)
            {
                registerDocumentConflictMessage = Documents.ExtractConflictMessage(documentFailure, Documents.DocumentNumberFieldKey);
            }
            else if (documentFailure.IsValidation)
            {
                registerDocumentUnmappedErrors = Documents.ApplyServerValidationErrors(
                    documentFailure.Errors, RegisterDocumentServerFieldMap, registerDocumentModel, registerDocumentMessageStore);
                registerDocumentEditContext.NotifyValidationStateChanged();
            }
            else
            {
                registerDocumentGeneralError = documentFailure.Message;
            }

            // Entered values (including the selected vessel/document type) are intentionally
            // preserved on every failure branch above - the form is only reset below, on success.
            return;
        }

        registerDocumentSuccessMessage = "Document registered.";

        registerDocumentModel = new RegisterDocumentFormModel();
        registerDocumentEditContext = new EditContext(registerDocumentModel);
        registerDocumentMessageStore = new ValidationMessageStore(registerDocumentEditContext);
        vesselPickerResetToken++;
        registerDocumentVesselLabel = null;

        await OnRegistered.InvokeAsync();
    }

    private sealed class RegisterDocumentFormModel : IValidatableObject
    {
        [Required(ErrorMessage = "Document number is required.")]
        [StringLength(Documents.DocumentNumberMaxLength, ErrorMessage = "Document number must not exceed 100 characters.")]
        public string? Number { get; set; }

        [Required(ErrorMessage = "Vessel is required.")]
        public Guid? VesselId { get; set; }

        [Required(ErrorMessage = "Document type is required.")]
        public int? DocumentTypeId { get; set; }

        [Required(ErrorMessage = "Issue date is required.")]
        public DateOnly? IssueDate { get; set; }

        [Required(ErrorMessage = "Expiry date is required.")]
        public DateOnly? ExpiryDate { get; set; }

        [Required(ErrorMessage = "Issuing authority is required.")]
        [StringLength(Documents.DocumentIssuingAuthorityMaxLength, ErrorMessage = "Issuing authority must not exceed 200 characters.")]
        public string? IssuingAuthority { get; set; }

        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            if (ExpiryDate is { } expiry && IssueDate is { } issue && expiry < issue)
            {
                yield return new ValidationResult(
                    "Expiry date must not be earlier than the issue date.",
                    [nameof(ExpiryDate)]);
            }
        }
    }
}
