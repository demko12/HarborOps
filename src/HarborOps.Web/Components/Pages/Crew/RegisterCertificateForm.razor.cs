using System.ComponentModel.DataAnnotations;
using HarborOps.Web.Api;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;

namespace HarborOps.Web.Components.Pages.Crew;

public partial class RegisterCertificateForm
{
    private const int TypeMaxLength = 100;
    private const int NameMaxLength = 200;
    private const int IssuingAuthorityMaxLength = 200;
    private const int LicenseNumberMaxLength = 100;

    private const string TypeFieldKey = "type";
    private const string NameFieldKey = "name";
    private const string IssuingAuthorityFieldKey = "issuingAuthority";
    private const string LicenseNumberFieldKey = "licenseNumber";
    private const string IssueDateFieldKey = "issueDate";
    private const string ExpiryDateFieldKey = "expiryDate";

    private static readonly IReadOnlyDictionary<string, string> ServerFieldMap =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [TypeFieldKey] = nameof(FormModel.Type),
            [NameFieldKey] = nameof(FormModel.Name),
            [IssuingAuthorityFieldKey] = nameof(FormModel.IssuingAuthority),
            [LicenseNumberFieldKey] = nameof(FormModel.LicenseNumber),
            [IssueDateFieldKey] = nameof(FormModel.IssueDate),
            [ExpiryDateFieldKey] = nameof(FormModel.ExpiryDate),
        };

    private static readonly IReadOnlyDictionary<string, string[]> EmptyErrors =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);

    [Parameter, EditorRequired]
    public Guid CrewMemberId { get; set; }

    [Parameter]
    public EventCallback OnRegistered { get; set; }

    private FormModel model = new();
    private EditContext editContext = null!;
    private ValidationMessageStore messageStore = null!;
    private bool isSubmitting;
    private string? successMessage;
    private string? conflictMessage;
    private string? generalError;
    private IReadOnlyList<string> unmappedErrors = [];

    protected override void OnInitialized()
    {
        editContext = new EditContext(model);
        messageStore = new ValidationMessageStore(editContext);
    }

    private async Task SubmitAsync()
    {
        messageStore.Clear();
        conflictMessage = null;
        generalError = null;
        unmappedErrors = [];
        successMessage = null;

        if (!editContext.Validate())
        {
            return;
        }

        if (isSubmitting)
        {
            return;
        }

        isSubmitting = true;

        var request = new RegisterCrewCertificateRequest
        {
            Type = model.Type,
            Name = model.Name,
            IssuingAuthority = model.IssuingAuthority,
            LicenseNumber = model.LicenseNumber,
            IssueDate = model.IssueDate,
            ExpiryDate = model.ExpiryDate,
        };

        var result = await CrewApi.RegisterCertificateAsync(CrewMemberId, request, CancellationToken.None);

        isSubmitting = false;

        if (!result.IsSuccess)
        {
            var failure = result.Failure!;

            if (failure.IsConflict)
            {
                conflictMessage = failure.Errors is not null
                    && failure.Errors.TryGetValue(LicenseNumberFieldKey, out var messages)
                    && messages.Length > 0
                    ? messages[0]
                    : failure.Message;
            }
            else if (failure.IsValidation)
            {
                unmappedErrors = ApplyServerValidationErrors(failure.Errors ?? EmptyErrors);
                editContext.NotifyValidationStateChanged();
            }
            else
            {
                generalError = failure.Message;
            }

            return;
        }

        successMessage = "Certificate registered.";
        model = new FormModel();
        editContext = new EditContext(model);
        messageStore = new ValidationMessageStore(editContext);

        await OnRegistered.InvokeAsync();
    }

    private IReadOnlyList<string> ApplyServerValidationErrors(IReadOnlyDictionary<string, string[]> errors)
    {
        var unmapped = new List<string>();

        foreach (var (key, messages) in errors)
        {
            if (ServerFieldMap.TryGetValue(key, out var propertyName))
            {
                var identifier = new FieldIdentifier(model, propertyName);
                foreach (var message in messages)
                {
                    messageStore.Add(identifier, message);
                }
            }
            else
            {
                unmapped.AddRange(messages);
            }
        }

        return unmapped;
    }

    private sealed class FormModel
    {
        [Required(ErrorMessage = "Type is required.")]
        [StringLength(TypeMaxLength, ErrorMessage = "Type must not exceed 100 characters.")]
        public string? Type { get; set; }

        [Required(ErrorMessage = "Name is required.")]
        [StringLength(NameMaxLength, ErrorMessage = "Name must not exceed 200 characters.")]
        public string? Name { get; set; }

        [Required(ErrorMessage = "Issuing authority is required.")]
        [StringLength(IssuingAuthorityMaxLength, ErrorMessage = "Issuing authority must not exceed 200 characters.")]
        public string? IssuingAuthority { get; set; }

        [Required(ErrorMessage = "License number is required.")]
        [StringLength(LicenseNumberMaxLength, ErrorMessage = "License number must not exceed 100 characters.")]
        public string? LicenseNumber { get; set; }

        [Required(ErrorMessage = "Issue date is required.")]
        public DateOnly? IssueDate { get; set; }

        [Required(ErrorMessage = "Expiry date is required.")]
        public DateOnly? ExpiryDate { get; set; }
    }
}
