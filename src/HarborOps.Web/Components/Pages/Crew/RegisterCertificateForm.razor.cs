using System.Globalization;
using HarborOps.Web.Api;
using Microsoft.AspNetCore.Components;

namespace HarborOps.Web.Components.Pages.Crew;

public partial class RegisterCertificateForm
{
    private static readonly IReadOnlyDictionary<string, string[]> EmptyErrors =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);

    private readonly DateOnly today = DateOnly.FromDateTime(DateTime.Today);

    [Parameter, EditorRequired]
    public Guid CrewMemberId { get; set; }

    [Parameter]
    public EventCallback OnRegistered { get; set; }

    private FormModel model = new();
    private bool isSubmitting;

    public bool IsSubmitting => isSubmitting;
    private string? successMessage;
    private string? conflictMessage;
    private string? generalError;
    private IReadOnlyList<string> fieldErrors = [];

    internal static string FormatIsoDate(DateOnly value) => value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private async Task SubmitAsync()
    {
        if (isSubmitting)
        {
            return;
        }

        conflictMessage = null;
        generalError = null;
        fieldErrors = [];
        successMessage = null;
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

        successMessage = "Certificate registered.";
        model = new FormModel();

        await OnRegistered.InvokeAsync();
    }

    private sealed class FormModel
    {
        public string? Type { get; set; }
        public string? Name { get; set; }
        public string? IssuingAuthority { get; set; }
        public string? LicenseNumber { get; set; }
        public DateOnly? IssueDate { get; set; }
        public DateOnly? ExpiryDate { get; set; }
    }
}
