using HarborOps.Web.Api;
using Microsoft.AspNetCore.Components;

namespace HarborOps.Web.Components.Pages.Crew;

public partial class RegisterMedicalExaminationForm
{
    private static readonly IReadOnlyDictionary<string, string[]> EmptyErrors =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);

    internal static readonly IReadOnlyList<(string Value, string Label)> FitnessResultOptions =
    [
        ("Fit", "Fit"),
        ("FitWithRestrictions", "Fit with restrictions"),
        ("Unfit", "Unfit"),
    ];

    [Parameter, EditorRequired]
    public Guid CrewMemberId { get; set; }

    [Parameter]
    public EventCallback OnRegistered { get; set; }

    private FormModel model = new();
    private bool isSubmitting;
    private string? successMessage;
    private string? generalError;
    private IReadOnlyList<string> fieldErrors = [];

    private async Task SubmitAsync()
    {
        if (isSubmitting)
        {
            return;
        }

        generalError = null;
        fieldErrors = [];
        successMessage = null;
        isSubmitting = true;

        var request = new RegisterMedicalExaminationRequest
        {
            ExamType = model.ExamType,
            Clinic = model.Clinic,
            ExamDate = model.ExamDate,
            ExpiryDate = model.ExpiryDate,
            FitnessResult = model.FitnessResult,
        };

        var result = await CrewApi.RegisterMedicalExaminationAsync(CrewMemberId, request, CancellationToken.None);

        isSubmitting = false;

        if (!result.IsSuccess)
        {
            var failure = result.Failure!;

            if (failure.IsValidation)
            {
                fieldErrors = (failure.Errors ?? EmptyErrors).Values.SelectMany(messages => messages).ToList();
            }
            else
            {
                generalError = failure.Message;
            }

            return;
        }

        successMessage = "Examination registered.";
        model = new FormModel();

        await OnRegistered.InvokeAsync();
    }

    private sealed class FormModel
    {
        public string? ExamType { get; set; }
        public string? Clinic { get; set; }
        public DateOnly? ExamDate { get; set; }
        public DateOnly? ExpiryDate { get; set; }
        public string? FitnessResult { get; set; }
    }
}
