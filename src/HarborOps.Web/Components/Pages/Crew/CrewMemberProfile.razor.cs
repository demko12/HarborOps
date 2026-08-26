using HarborOps.Web.Api;
using Microsoft.AspNetCore.Components;

namespace HarborOps.Web.Components.Pages.Crew;

public partial class CrewMemberProfile : IDisposable
{
    [Parameter]
    public Guid CrewMemberId { get; set; }

    private readonly CancellationTokenSource disposeCancellation = new();
    private readonly DateOnly currentDate = DateOnly.FromDateTime(DateTime.Today);

    private CrewMemberProfileResponse? profile;
    private CrewApiFailure? failure;
    private bool isLoading = true;
    private bool isSensitiveDataRevealed;

    private static readonly IReadOnlyDictionary<string, string[]> EmptyErrors =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);

    private static readonly string[] RotationPatternOptions =
    [
        "2 months on / 2 months off",
        "3 months on / 3 months off",
        "4 months on / 2 months off",
        "6 months on / 2 months off",
    ];

    private CrewAssignmentProfileItemResponse? selectedAssignment;
    private bool isDeletingAssignment;
    private string? deleteAssignmentError;

    private IReadOnlyList<MedicalExaminationListItemResponse> medicalExaminations = [];
    private string? currentFitnessResult;
    private bool isLoadingMedical = true;
    private CrewApiFailure? medicalFailure;

    private CrewCertificateProfileItemResponse? selectedCertificate;

    private CrewRotationListItemResponse? selectedRotation;
    private bool rotationExists;
    private bool isLoadingRotation;
    private string? rotationLoadError;

    private RotationFormModel rotationForm = new();
    private bool isSavingRotation;
    private string? rotationConflictMessage;
    private string? rotationGeneralError;
    private IReadOnlyList<string> rotationFieldErrors = [];
    private string? rotationSuccessMessage;
    private int relievingMemberPickerResetToken;

    private bool isExecutingRotation;
    private string? executeRotationError;

    private bool isEditingRotation;

    private bool CanManage => CurrentUser.HasPermission(CurrentUserAuthorizationState.CrewManagePermission);

    protected override async Task OnInitializedAsync() =>
        await Task.WhenAll(LoadProfileAsync(), LoadMedicalExaminationsAsync());

    protected override async Task OnParametersSetAsync()
    {
        // The [crewMemberId:guid] route parameter can change when the user navigates
        // directly between two profile URLs (e.g. from a search result link) without the
        // component being torn down and recreated - reload whenever it changes.
        if (profile is not null && profile.CrewMemberId != CrewMemberId)
        {
            await Task.WhenAll(LoadProfileAsync(), LoadMedicalExaminationsAsync());
        }
    }

    private async Task LoadProfileAsync()
    {
        isLoading = true;
        failure = null;

        CrewApiResult<CrewMemberProfileResponse> result;
        try
        {
            result = await CrewApi.GetProfileAsync(CrewMemberId, disposeCancellation.Token);
        }
        catch (OperationCanceledException) when (disposeCancellation.IsCancellationRequested)
        {
            return;
        }

        if (disposeCancellation.IsCancellationRequested)
        {
            return;
        }

        isLoading = false;

        if (result.IsSuccess)
        {
            profile = result.Value;
        }
        else
        {
            failure = result.Failure;
        }
    }

    private void ToggleSensitiveData() => isSensitiveDataRevealed = !isSensitiveDataRevealed;

    private async Task OnCertificateRegisteredAsync()
    {
        // "Refresh only affected profile sections": re-query the profile but only replace
        // the certificates collection on the currently displayed model, leaving personal/
        // employment fields and the assignments list untouched.
        var result = await CrewApi.GetProfileAsync(CrewMemberId, disposeCancellation.Token);
        if (result.IsSuccess && result.Value is not null && profile is not null)
        {
            profile = profile with { Certificates = result.Value.Certificates };
        }
    }

    private async Task LoadMedicalExaminationsAsync()
    {
        isLoadingMedical = true;
        medicalFailure = null;

        CrewApiResult<MemberMedicalExaminationsResponse> result;
        try
        {
            result = await CrewApi.GetMemberMedicalExaminationsAsync(CrewMemberId, disposeCancellation.Token);
        }
        catch (OperationCanceledException) when (disposeCancellation.IsCancellationRequested)
        {
            return;
        }

        if (disposeCancellation.IsCancellationRequested)
        {
            return;
        }

        isLoadingMedical = false;

        if (result.IsSuccess)
        {
            medicalExaminations = result.Value!.Items;
            currentFitnessResult = result.Value.CurrentFitnessResult;
        }
        else
        {
            medicalFailure = result.Failure;
        }
    }

    private Task OnMedicalExaminationRegisteredAsync() => LoadMedicalExaminationsAsync();

    private static FitnessState GetFitnessResultState(string fitnessResult) => fitnessResult switch
    {
        "Fit" => new FitnessState("Fit", "crew-profile-indicator-valid"),
        "FitWithRestrictions" => new FitnessState("Fit with restrictions", "crew-profile-indicator-warning"),
        "Unfit" => new FitnessState("Unfit", "crew-profile-indicator-expired"),
        _ => new FitnessState(fitnessResult, "crew-profile-indicator-neutral"),
    };

    private static FitnessState GetExamStatusState(MedicalExaminationListItemResponse exam) =>
        exam.IsExpired
            ? new FitnessState("Expired", "crew-profile-indicator-expired")
            : exam.IsExpiringSoon
                ? new FitnessState("Expiring soon", "crew-profile-indicator-warning")
                : new FitnessState("Valid", "crew-profile-indicator-valid");

    private FitnessState CurrentFitnessDisplayState =>
        currentFitnessResult is { } result
            ? GetFitnessResultState(result)
            : new FitnessState("Not assessed", "crew-profile-indicator-neutral");

    private sealed record FitnessState(string Label, string Class);

    private async Task OnAssignmentRegisteredAsync()
    {
        var result = await CrewApi.GetProfileAsync(CrewMemberId, disposeCancellation.Token);
        if (result.IsSuccess && result.Value is not null && profile is not null)
        {
            profile = profile with { Assignments = result.Value.Assignments };
        }
    }

    private async Task OpenAssignmentDetailsAsync(CrewAssignmentProfileItemResponse assignment)
    {
        selectedAssignment = assignment;
        deleteAssignmentError = null;
        ResetRotationFormState();

        await LoadRotationAsync();
    }

    private void CloseAssignmentDetails()
    {
        selectedAssignment = null;
        deleteAssignmentError = null;
        ResetRotationFormState();
    }

    private void OpenCertificateDetails(CrewCertificateProfileItemResponse certificate) => selectedCertificate = certificate;

    private void CloseCertificateDetails() => selectedCertificate = null;

    private void ResetRotationFormState()
    {
        selectedRotation = null;
        rotationExists = false;
        isLoadingRotation = false;
        rotationLoadError = null;
        rotationForm = new RotationFormModel();
        rotationConflictMessage = null;
        rotationGeneralError = null;
        rotationFieldErrors = [];
        rotationSuccessMessage = null;
        executeRotationError = null;
        isEditingRotation = false;
        relievingMemberPickerResetToken++;
    }

    private void ToggleEditRotation()
    {
        if (isEditingRotation && selectedRotation is not null)
        {
            rotationForm = new RotationFormModel
            {
                RelievingCrewMemberId = selectedRotation.RelievingCrewMemberId,
                RotationPattern = selectedRotation.RotationPattern,
                PlannedReliefDate = selectedRotation.PlannedReliefDate,
            };
            rotationFieldErrors = [];
            rotationConflictMessage = null;
            rotationGeneralError = null;
            relievingMemberPickerResetToken++;
        }

        isEditingRotation = !isEditingRotation;
    }

    private async Task LoadRotationAsync()
    {
        if (selectedAssignment is null)
        {
            return;
        }

        isLoadingRotation = true;
        rotationLoadError = null;

        var result = await CrewApi.GetRotationAsync(selectedAssignment.AssignmentId, disposeCancellation.Token);

        isLoadingRotation = false;

        if (result.IsSuccess)
        {
            selectedRotation = result.Value;
            rotationExists = true;
            rotationForm = new RotationFormModel
            {
                RelievingCrewMemberId = result.Value!.RelievingCrewMemberId,
                RotationPattern = result.Value.RotationPattern,
                PlannedReliefDate = result.Value.PlannedReliefDate,
            };
        }
        else if (result.Failure!.IsNotFound)
        {
            selectedRotation = null;
            rotationExists = false;
            rotationForm.PlannedReliefDate = selectedAssignment.PlannedSignOffDate;
        }
        else
        {
            rotationLoadError = result.Failure.Message;
        }
    }

    private async Task SaveRotationAsync()
    {
        if (selectedAssignment is null || isSavingRotation)
        {
            return;
        }

        rotationConflictMessage = null;
        rotationGeneralError = null;
        rotationFieldErrors = [];
        rotationSuccessMessage = null;
        isSavingRotation = true;

        if (rotationExists)
        {
            var result = await CrewApi.UpdateRotationAsync(
                selectedAssignment.AssignmentId,
                new UpdateCrewRotationRequest
                {
                    RelievingCrewMemberId = rotationForm.RelievingCrewMemberId,
                    RotationPattern = rotationForm.RotationPattern,
                    PlannedReliefDate = rotationForm.PlannedReliefDate,
                },
                disposeCancellation.Token);

            isSavingRotation = false;

            if (!result.IsSuccess)
            {
                HandleRotationFailure(result.Failure!);
                return;
            }

            rotationSuccessMessage = "Rotation updated.";
        }
        else
        {
            var result = await CrewApi.PlanRotationAsync(
                selectedAssignment.AssignmentId,
                new PlanCrewRotationRequest
                {
                    RelievingCrewMemberId = rotationForm.RelievingCrewMemberId,
                    RotationPattern = rotationForm.RotationPattern,
                    PlannedReliefDate = rotationForm.PlannedReliefDate,
                },
                disposeCancellation.Token);

            isSavingRotation = false;

            if (!result.IsSuccess)
            {
                HandleRotationFailure(result.Failure!);
                return;
            }

            rotationSuccessMessage = "Rotation planned.";
        }

        isEditingRotation = false;
        await LoadRotationAsync();
    }

    private void HandleRotationFailure(CrewApiFailure failure)
    {
        if (failure.IsConflict)
        {
            rotationConflictMessage = failure.Message;
        }
        else if (failure.IsValidation)
        {
            rotationFieldErrors = (failure.Errors ?? EmptyErrors).Values.SelectMany(messages => messages).ToList();
        }
        else
        {
            rotationGeneralError = failure.Message;
        }
    }

    private async Task ExecuteRotationAsync()
    {
        if (selectedAssignment is null || isExecutingRotation)
        {
            return;
        }

        isExecutingRotation = true;
        executeRotationError = null;

        var result = await CrewApi.ExecuteRotationAsync(selectedAssignment.AssignmentId, disposeCancellation.Token);

        isExecutingRotation = false;

        if (!result.IsSuccess)
        {
            executeRotationError = result.Failure!.Message;
            return;
        }

        CloseAssignmentDetails();

        var refreshed = await CrewApi.GetProfileAsync(CrewMemberId, disposeCancellation.Token);
        if (refreshed.IsSuccess && refreshed.Value is not null && profile is not null)
        {
            profile = profile with { Assignments = refreshed.Value.Assignments };
        }
    }

    private async Task DeleteSelectedAssignmentAsync()
    {
        if (selectedAssignment is null || isDeletingAssignment)
        {
            return;
        }

        isDeletingAssignment = true;
        deleteAssignmentError = null;

        var result = await CrewApi.DeleteAssignmentAsync(selectedAssignment.AssignmentId, disposeCancellation.Token);

        isDeletingAssignment = false;

        if (!result.IsSuccess)
        {
            deleteAssignmentError = result.Failure!.Message;
            return;
        }

        selectedAssignment = null;

        var refreshed = await CrewApi.GetProfileAsync(CrewMemberId, disposeCancellation.Token);
        if (refreshed.IsSuccess && refreshed.Value is not null && profile is not null)
        {
            profile = profile with { Assignments = refreshed.Value.Assignments };
        }
    }
    private bool IsPassportExpiringSoon(DateOnly passportExpiry) => passportExpiry <= currentDate.AddMonths(6);

    private bool IsPassportExpired(DateOnly passportExpiry) => passportExpiry < currentDate;

    private CertificateState GetCertificateState(DateOnly expiryDate)
    {
        if (expiryDate < currentDate)
        {
            return new CertificateState("Expired", "crew-profile-indicator-expired");
        }

        return expiryDate <= currentDate.AddDays(30)
            ? new CertificateState("Expiring soon", "crew-profile-indicator-warning")
            : new CertificateState("Valid", "crew-profile-indicator-valid");
    }

    private static AssignmentState GetAssignmentState(CrewAssignmentProfileItemResponse assignment)
    {
        if (assignment.ActualSignOffDate is not null)
        {
            return new AssignmentState("Closed", "crew-profile-indicator-neutral");
        }

        return string.Equals(assignment.Status, "Active", StringComparison.OrdinalIgnoreCase)
            ? new AssignmentState("Active", "crew-profile-indicator-valid")
            : new AssignmentState("Planned", "crew-profile-indicator-warning");
    }

    private static string GetEmploymentStatusClass(string status) =>
        string.Equals(status, "Active", StringComparison.OrdinalIgnoreCase) ? "employment-active" : "employment-inactive";

    private static string ShortId(Guid id) => id.ToString()[..8];

    public void Dispose()
    {
        disposeCancellation.Cancel();
        disposeCancellation.Dispose();
    }

    private sealed record CertificateState(string Label, string Class);

    private sealed record AssignmentState(string Label, string Class);

    private sealed class RotationFormModel
    {
        public Guid? RelievingCrewMemberId { get; set; }
        public string? RotationPattern { get; set; }
        public DateOnly? PlannedReliefDate { get; set; }
    }
}
