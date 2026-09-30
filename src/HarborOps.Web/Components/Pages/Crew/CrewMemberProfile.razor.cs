using HarborOps.Web.Api;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace HarborOps.Web.Components.Pages.Crew;

public partial class CrewMemberProfile : IDisposable
{
    [Parameter]
    public Guid CrewMemberId { get; set; }

    private readonly CancellationTokenSource disposeCancellation = new();
    private readonly DateOnly currentDate = DateOnly.FromDateTime(DateTime.Today);

    private CrewMemberProfileResponse? profile;
    private IReadOnlyDictionary<Guid, string> vesselNames = new Dictionary<Guid, string>();
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
    private bool isProcessingAssignmentAction;
    private string? assignmentActionError;
    private string? pendingAssignmentAction;
    private DateOnly? completionDate;
    private string? cancellationNotes;

    private bool isLoadingQualification;
    private GetCrewQualificationEligibilityResponse? qualificationEligibility;

    private IReadOnlyList<MedicalExaminationListItemResponse> medicalExaminations = [];
    private string? currentFitnessResult;
    private bool isLoadingMedical = true;
    private CrewApiFailure? medicalFailure;

    private const int CertificatePreviewCount = 3;

    private bool isViewingAllCertificates;
    private string certificateSearch = string.Empty;
    private string certificateStatusFilter = string.Empty;

    private CrewCertificateProfileItemResponse? selectedCertificate;
    private GetCrewCertificateHistoryResponse? certificateHistory;
    private bool isLoadingCertificateHistory;
    private bool isRenewingCertificate;
    private bool isRevokingCertificate;
    private string? revokeReason;
    private bool isSubmittingCertificateAction;
    private string? certificateActionError;
    private string? certificateConflictMessage;
    private IReadOnlyList<AffectedCertificateAssignmentResponse> affectedActiveAssignments = [];

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
    private bool isConfirmingRotationExecution;

    private bool isEditingRotation;

    private bool isEditingProfile;
    private ProfileRegistration? registrationEditor;
    private EditCrewMemberProfileForm? profileEditorForm;
    private AssignToVesselForm? assignmentEditorForm;
    private RegisterCertificateForm? certificateEditorForm;
    private RegisterMedicalExaminationForm? medicalEditorForm;
    private ElementReference profileEditorElement;
    private bool wasProfileEditorOpen;
    private string? profileEditorSuccess;

    private enum ProfileRegistration { Assignment, Certificate, Medical }

    private bool IsProfileEditorOpen => isEditingProfile || registrationEditor is not null
        || isConfirmingEmploymentStatusChange || isRequestingUnavailability;

    private bool IsProfileEditorBusy => isSubmittingEmploymentStatus || isSubmittingAvailabilityRequest
        || (isEditingProfile && profileEditorForm?.IsSubmitting == true)
        || (registrationEditor == ProfileRegistration.Assignment && assignmentEditorForm?.IsSubmitting == true)
        || (registrationEditor == ProfileRegistration.Certificate && certificateEditorForm?.IsSubmitting == true)
        || (registrationEditor == ProfileRegistration.Medical && medicalEditorForm?.IsSubmitting == true);

    private string ProfileEditorTitle => isEditingProfile ? "Edit profile"
        : isConfirmingEmploymentStatusChange ? "Change employment status"
        : isRequestingUnavailability ? "Request unavailability"
        : registrationEditor switch
        {
            ProfileRegistration.Assignment => "Assign to vessel",
            ProfileRegistration.Certificate => "Register certificate",
            _ => "Register medical examination",
        };

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        var editorOpen = CanManage && IsProfileEditorOpen && profile is not null && !isLoading && failure is null;
        if (editorOpen && !wasProfileEditorOpen)
        {
            wasProfileEditorOpen = true;
            await profileEditorElement.FocusAsync();
        }
        else
        {
            wasProfileEditorOpen = editorOpen;
        }
    }

    private void CloseProfileEditor()
    {
        if (IsProfileEditorBusy)
        {
            return;
        }

        isEditingProfile = false;
        registrationEditor = null;
        CancelEmploymentStatusChange();
        CancelUnavailabilityRequest();
    }

    private void HandleProfileEditorKeyDown(KeyboardEventArgs args)
    {
        if (args.Key == "Escape")
        {
            CloseProfileEditor();
        }
    }

    private static readonly string[] EmploymentStatusOptions = ["Active", "OnLeave", "Suspended", "Terminated"];

    private IReadOnlyList<CrewEmploymentStatusHistoryItemResponse> employmentHistory = [];
    private bool isLoadingEmploymentHistory = true;
    private CrewApiFailure? employmentHistoryFailure;

    private bool isConfirmingEmploymentStatusChange;
    private bool isSubmittingEmploymentStatus;
    private string? employmentStatusActionError;
    private EmploymentStatusFormModel employmentStatusForm = new();

    private static readonly string[] AvailabilityTypeOptions = ["AnnualLeave", "SickLeave", "Training", "OtherUnavailable"];

    private IReadOnlyList<CrewMemberAvailabilityItemResponse> availabilityItems = [];
    private bool isLoadingAvailability = true;
    private CrewApiFailure? availabilityFailure;
    private readonly HashSet<Guid> expandedAvailabilityIds = [];

    private bool isRequestingUnavailability;
    private bool isSubmittingAvailabilityRequest;
    private AvailabilityRequestFormModel availabilityRequestForm = new();
    private string? availabilityRequestConflict;
    private string? availabilityRequestGeneralError;
    private IReadOnlyList<string> availabilityRequestFieldErrors = [];

    private Guid? availabilityDecisionId;
    private string availabilityDecisionKind = "reject";
    private string? availabilityDecisionReason;
    private bool isSubmittingAvailabilityDecision;
    private Guid? availabilityActionId;
    private string? availabilityActionError;

    private bool CanManage => CurrentUser.HasPermission(CurrentUserAuthorizationState.CrewManagePermission);

    protected override async Task OnInitializedAsync() =>
        await Task.WhenAll(LoadProfileAsync(), LoadMedicalExaminationsAsync(), LoadEmploymentHistoryAsync(), LoadAvailabilityAsync());

    protected override async Task OnParametersSetAsync()
    {
        // The [crewMemberId:guid] route parameter can change when the user navigates
        // directly between two profile URLs (e.g. from a search result link) without the
        // component being torn down and recreated - reload whenever it changes.
        if (profile is not null && profile.CrewMemberId != CrewMemberId)
        {
            await Task.WhenAll(LoadProfileAsync(), LoadMedicalExaminationsAsync(), LoadEmploymentHistoryAsync(), LoadAvailabilityAsync());
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
            await ResolveVesselNamesAsync(profile?.Assignments.Select(assignment => assignment.VesselId) ?? []);
        }
        else
        {
            failure = result.Failure;
        }
    }

    private void ToggleSensitiveData() => isSensitiveDataRevealed = !isSensitiveDataRevealed;

    private void ToggleEditProfile() => isEditingProfile = !isEditingProfile;

    private async Task OnProfileUpdatedAsync()
    {
        isEditingProfile = false;
        await LoadProfileAsync();
    }

    private async Task LoadEmploymentHistoryAsync()
    {
        isLoadingEmploymentHistory = true;
        employmentHistoryFailure = null;

        CrewApiResult<GetCrewEmploymentStatusHistoryResponse> result;
        try
        {
            result = await CrewApi.GetEmploymentStatusHistoryAsync(CrewMemberId, disposeCancellation.Token);
        }
        catch (OperationCanceledException) when (disposeCancellation.IsCancellationRequested)
        {
            return;
        }

        if (disposeCancellation.IsCancellationRequested)
        {
            return;
        }

        isLoadingEmploymentHistory = false;

        if (result.IsSuccess)
        {
            employmentHistory = result.Value!.Items;
        }
        else
        {
            employmentHistoryFailure = result.Failure;
        }
    }

    private void RequestEmploymentStatusChange()
    {
        employmentStatusActionError = null;
        employmentStatusForm = new EmploymentStatusFormModel
        {
            Status = profile?.EmploymentStatus ?? EmploymentStatusOptions[0],
        };
        isConfirmingEmploymentStatusChange = true;
    }

    private void CancelEmploymentStatusChange()
    {
        if (isSubmittingEmploymentStatus) return;
        isConfirmingEmploymentStatusChange = false;
        employmentStatusActionError = null;
    }

    private async Task ConfirmEmploymentStatusChangeAsync()
    {
        if (isSubmittingEmploymentStatus)
        {
            return;
        }

        isSubmittingEmploymentStatus = true;
        employmentStatusActionError = null;

        var result = await CrewApi.ChangeEmploymentStatusAsync(
            CrewMemberId,
            new ChangeCrewMemberEmploymentStatusRequest
            {
                Status = employmentStatusForm.Status,
                Notes = employmentStatusForm.Notes,
            },
            disposeCancellation.Token);

        isSubmittingEmploymentStatus = false;

        if (!result.IsSuccess)
        {
            employmentStatusActionError = result.Failure!.Message;
            return;
        }

        isConfirmingEmploymentStatusChange = false;

        await Task.WhenAll(LoadProfileAsync(), LoadEmploymentHistoryAsync());
    }

    private async Task LoadAvailabilityAsync()
    {
        isLoadingAvailability = true;
        availabilityFailure = null;

        CrewApiResult<GetCrewMemberAvailabilityResponse> result;
        try
        {
            result = await CrewApi.GetMemberAvailabilityAsync(CrewMemberId, disposeCancellation.Token);
        }
        catch (OperationCanceledException) when (disposeCancellation.IsCancellationRequested)
        {
            return;
        }

        if (disposeCancellation.IsCancellationRequested)
        {
            return;
        }

        isLoadingAvailability = false;

        if (result.IsSuccess)
        {
            availabilityItems = result.Value!.Items;
        }
        else
        {
            availabilityFailure = result.Failure;
        }
    }

    private void ToggleAvailabilityExpanded(Guid id)
    {
        if (!expandedAvailabilityIds.Add(id))
        {
            expandedAvailabilityIds.Remove(id);
        }
    }

    private void RequestUnavailability()
    {
        availabilityRequestForm = new AvailabilityRequestFormModel { Type = AvailabilityTypeOptions[0] };
        ClearAvailabilityRequestErrors();
        isRequestingUnavailability = true;
    }

    private void CancelUnavailabilityRequest()
    {
        if (isSubmittingAvailabilityRequest) return;
        isRequestingUnavailability = false;
        ClearAvailabilityRequestErrors();
    }

    private void ClearAvailabilityRequestErrors()
    {
        availabilityRequestConflict = null;
        availabilityRequestGeneralError = null;
        availabilityRequestFieldErrors = [];
    }

    private async Task SubmitUnavailabilityRequestAsync()
    {
        if (isSubmittingAvailabilityRequest)
        {
            return;
        }

        isSubmittingAvailabilityRequest = true;
        ClearAvailabilityRequestErrors();

        var result = await CrewApi.RequestAvailabilityAsync(
            CrewMemberId,
            new CreateCrewAvailabilityRequestRequest
            {
                Type = availabilityRequestForm.Type,
                StartDate = availabilityRequestForm.StartDate,
                EndDate = availabilityRequestForm.EndDate,
                Reason = availabilityRequestForm.Reason,
                Notes = availabilityRequestForm.Notes,
            },
            disposeCancellation.Token);

        isSubmittingAvailabilityRequest = false;

        if (!result.IsSuccess)
        {
            var availabilityRequestFailure = result.Failure!;
            if (availabilityRequestFailure.IsConflict)
            {
                availabilityRequestConflict = availabilityRequestFailure.Message;
            }
            else if (availabilityRequestFailure.IsValidation)
            {
                availabilityRequestFieldErrors = (availabilityRequestFailure.Errors ?? EmptyErrors)
                    .Values.SelectMany(messages => messages).ToList();
            }
            else
            {
                availabilityRequestGeneralError = availabilityRequestFailure.Message;
            }

            return;
        }

        isRequestingUnavailability = false;
        await LoadAvailabilityAsync();
    }

    private void BeginAvailabilityDecision(Guid id, string kind)
    {
        availabilityDecisionId = id;
        availabilityDecisionKind = kind;
        availabilityDecisionReason = null;
        availabilityActionId = null;
        availabilityActionError = null;
    }

    private void CancelAvailabilityDecision()
    {
        availabilityDecisionId = null;
        availabilityDecisionReason = null;
    }

    private async Task ApproveAvailabilityAsync(Guid id)
    {
        if (isSubmittingAvailabilityDecision)
        {
            return;
        }

        isSubmittingAvailabilityDecision = true;
        availabilityActionId = id;
        availabilityActionError = null;

        var result = await CrewApi.ApproveAvailabilityRequestAsync(id, disposeCancellation.Token);

        isSubmittingAvailabilityDecision = false;

        if (!result.IsSuccess)
        {
            availabilityActionError = result.Failure!.Message;
            return;
        }

        await LoadAvailabilityAsync();
    }

    private async Task SubmitAvailabilityDecisionAsync()
    {
        if (isSubmittingAvailabilityDecision || availabilityDecisionId is not { } id)
        {
            return;
        }

        isSubmittingAvailabilityDecision = true;
        availabilityActionId = id;
        availabilityActionError = null;

        CrewApiWriteResult<CrewAvailabilityDecisionResponse> result;
        if (availabilityDecisionKind == "reject")
        {
            result = await CrewApi.RejectAvailabilityRequestAsync(
                id, new RejectCrewAvailabilityRequestRequest { Reason = availabilityDecisionReason }, disposeCancellation.Token);
        }
        else
        {
            result = await CrewApi.CancelAvailabilityRequestAsync(
                id, new CancelCrewAvailabilityRequestRequest { Reason = availabilityDecisionReason }, disposeCancellation.Token);
        }

        isSubmittingAvailabilityDecision = false;

        if (!result.IsSuccess)
        {
            availabilityActionError = result.Failure!.Message;
            return;
        }

        availabilityDecisionId = null;
        availabilityDecisionReason = null;
        await LoadAvailabilityAsync();
    }

    private static string AvailabilityTypeLabel(string type) => type switch
    {
        "AnnualLeave" => "Annual leave",
        "SickLeave" => "Sick leave",
        "Training" => "Training",
        "OtherUnavailable" => "Other",
        _ => type,
    };

    private async Task OnCertificateRegisteredAsync()
    {
        registrationEditor = null;
        profileEditorSuccess = "Certificate registered.";
        await RefreshCertificatesAsync();
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

    private Task OnMedicalExaminationRegisteredAsync()
    {
        registrationEditor = null;
        profileEditorSuccess = "Examination registered.";
        return LoadMedicalExaminationsAsync();
    }

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
        registrationEditor = null;
        profileEditorSuccess = "Crew member assigned to vessel.";
        var result = await CrewApi.GetProfileAsync(CrewMemberId, disposeCancellation.Token);
        if (result.IsSuccess && result.Value is not null && profile is not null)
        {
            profile = profile with { Assignments = result.Value.Assignments };
            await ResolveVesselNamesAsync(profile.Assignments.Select(assignment => assignment.VesselId));
        }
    }

    private async Task OpenAssignmentDetailsAsync(CrewAssignmentProfileItemResponse assignment)
    {
        selectedAssignment = assignment;
        assignmentActionError = null;
        pendingAssignmentAction = null;
        completionDate = assignment.ActualSignOffDate ?? assignment.PlannedSignOffDate;
        cancellationNotes = null;
        qualificationEligibility = null;
        ResetRotationFormState();

        await Task.WhenAll(LoadRotationAsync(), LoadQualificationEligibilityAsync(assignment));
    }

    private async Task LoadQualificationEligibilityAsync(CrewAssignmentProfileItemResponse assignment)
    {
        if (assignment.ActualSignOffDate is not null)
        {
            return;
        }

        isLoadingQualification = true;

        var result = await CrewApi.GetQualificationEligibilityAsync(
            CrewMemberId, assignment.RankId, assignment.SignOnDate, assignment.PlannedSignOffDate, disposeCancellation.Token);

        isLoadingQualification = false;
        qualificationEligibility = result.IsSuccess ? result.Value : null;
    }

    private void CloseAssignmentDetails()
    {
        selectedAssignment = null;
        assignmentActionError = null;
        pendingAssignmentAction = null;
        qualificationEligibility = null;
        ResetRotationFormState();
    }

    private async Task OpenCertificateDetailsAsync(CrewCertificateProfileItemResponse certificate)
    {
        selectedCertificate = certificate;
        certificateHistory = null;
        isRenewingCertificate = false;
        isRevokingCertificate = false;
        revokeReason = null;
        certificateActionError = null;
        certificateConflictMessage = null;
        affectedActiveAssignments = [];

        await LoadCertificateHistoryAsync();
    }

    private async Task LoadCertificateHistoryAsync()
    {
        if (selectedCertificate is null)
        {
            return;
        }

        isLoadingCertificateHistory = true;

        var result = await CrewApi.GetCertificateHistoryAsync(selectedCertificate.CertificateId, disposeCancellation.Token);

        isLoadingCertificateHistory = false;
        certificateHistory = result.IsSuccess ? result.Value : null;
    }

    private void CloseCertificateDetails()
    {
        selectedCertificate = null;
        certificateHistory = null;
        isRenewingCertificate = false;
        isRevokingCertificate = false;
        revokeReason = null;
        certificateActionError = null;
        certificateConflictMessage = null;
        affectedActiveAssignments = [];
    }

    private void OpenAllCertificates()
    {
        certificateSearch = string.Empty;
        certificateStatusFilter = string.Empty;
        isViewingAllCertificates = true;
    }

    private void CloseAllCertificates() => isViewingAllCertificates = false;

    private async Task OpenCertificateFromAllListAsync(CrewCertificateProfileItemResponse certificate)
    {
        isViewingAllCertificates = false;
        await OpenCertificateDetailsAsync(certificate);
    }

    private IEnumerable<CrewCertificateProfileItemResponse> FilteredCertificates =>
        (profile?.Certificates ?? [])
            .Where(certificate => string.IsNullOrEmpty(certificateStatusFilter)
                || GetCertificateState(certificate).Label == certificateStatusFilter)
            .Where(certificate => string.IsNullOrWhiteSpace(certificateSearch)
                || certificate.Type.Contains(certificateSearch, StringComparison.OrdinalIgnoreCase)
                || certificate.Name.Contains(certificateSearch, StringComparison.OrdinalIgnoreCase)
                || certificate.LicenseNumber.Contains(certificateSearch, StringComparison.OrdinalIgnoreCase));

    private CrewCertificateProfileItemResponse? FindCertificate(Guid certificateId) =>
        profile?.Certificates.FirstOrDefault(certificate => certificate.CertificateId == certificateId);

    private string? GetRevocationReason() =>
        certificateHistory?.History
            .FirstOrDefault(entry => entry.CertificateId == selectedCertificate!.CertificateId && entry.EventType == "Revoked")
            ?.Reason;

    private void RequestRenewCertificate()
    {
        certificateActionError = null;
        certificateConflictMessage = null;
        isRevokingCertificate = false;
        isRenewingCertificate = true;
    }

    private void CancelRenewCertificate() => isRenewingCertificate = false;

    private async Task OnCertificateRenewedAsync()
    {
        isRenewingCertificate = false;
        await RefreshCertificatesAsync();

        if (selectedCertificate is not null)
        {
            selectedCertificate = FindCertificate(selectedCertificate.CertificateId) ?? selectedCertificate;
            await LoadCertificateHistoryAsync();
        }
    }

    private void RequestRevokeCertificate()
    {
        certificateActionError = null;
        certificateConflictMessage = null;
        revokeReason = null;
        isRenewingCertificate = false;
        isRevokingCertificate = true;
    }

    private void CancelRevokeCertificate()
    {
        isRevokingCertificate = false;
        revokeReason = null;
    }

    private async Task SubmitRevokeCertificateAsync()
    {
        if (selectedCertificate is null || isSubmittingCertificateAction)
        {
            return;
        }

        isSubmittingCertificateAction = true;
        certificateActionError = null;
        certificateConflictMessage = null;

        var result = await CrewApi.RevokeCertificateAsync(
            selectedCertificate.CertificateId,
            new RevokeCrewCertificateRequest { Reason = revokeReason },
            disposeCancellation.Token);

        isSubmittingCertificateAction = false;

        if (!result.IsSuccess)
        {
            var revokeFailure = result.Failure!;
            if (revokeFailure.IsConflict)
            {
                certificateConflictMessage = revokeFailure.Message;
            }
            else
            {
                certificateActionError = revokeFailure.Message;
            }

            return;
        }

        isRevokingCertificate = false;
        revokeReason = null;
        affectedActiveAssignments = result.Value!.AffectedActiveAssignments;

        await RefreshCertificatesAsync();

        if (selectedCertificate is not null)
        {
            selectedCertificate = FindCertificate(selectedCertificate.CertificateId) ?? selectedCertificate;
            await LoadCertificateHistoryAsync();
        }
    }

    private async Task RefreshCertificatesAsync()
    {
        // Re-query the profile but only replace the certificates collection on the currently
        // displayed model, leaving personal/employment fields and the assignments list untouched.
        var result = await CrewApi.GetProfileAsync(CrewMemberId, disposeCancellation.Token);
        if (result.IsSuccess && result.Value is not null && profile is not null)
        {
            profile = profile with { Certificates = result.Value.Certificates };
        }
    }

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
        isConfirmingRotationExecution = false;
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
        isConfirmingRotationExecution = false;
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
            await ResolveVesselNamesAsync(profile.Assignments.Select(assignment => assignment.VesselId));
        }
    }

    private void RequestRotationExecution() => isConfirmingRotationExecution = true;

    private void CancelRotationExecution() => isConfirmingRotationExecution = false;

    private void RequestAssignmentAction(string action)
    {
        assignmentActionError = null;
        pendingAssignmentAction = action;
    }

    private void RequestActivation() => RequestAssignmentAction("activate");

    private void RequestCompletion() => RequestAssignmentAction("complete");

    private void RequestCancellation() => RequestAssignmentAction("cancel");

    private void CancelAssignmentAction() => pendingAssignmentAction = null;

    private async Task ConfirmAssignmentActionAsync()
    {
        if (selectedAssignment is null || pendingAssignmentAction is null || isProcessingAssignmentAction)
        {
            return;
        }

        isProcessingAssignmentAction = true;
        assignmentActionError = null;
        CrewApiWriteResult<bool> result = pendingAssignmentAction switch
        {
            "activate" => await CrewApi.ActivateAssignmentAsync(selectedAssignment.AssignmentId, disposeCancellation.Token),
            "complete" => await CrewApi.CompleteAssignmentAsync(selectedAssignment.AssignmentId, new CompleteCrewAssignmentRequest
            {
                ActualSignOffDate = completionDate,
            }, disposeCancellation.Token),
            "cancel" => await CrewApi.CancelAssignmentAsync(selectedAssignment.AssignmentId, new CancelCrewAssignmentRequest
            {
                Notes = cancellationNotes,
            }, disposeCancellation.Token),
            _ => throw new InvalidOperationException("Unknown crew assignment action."),
        };
        isProcessingAssignmentAction = false;

        if (!result.IsSuccess)
        {
            assignmentActionError = result.Failure!.Message;
            return;
        }

        pendingAssignmentAction = null;
        selectedAssignment = null;

        var refreshed = await CrewApi.GetProfileAsync(CrewMemberId, disposeCancellation.Token);
        if (refreshed.IsSuccess && refreshed.Value is not null && profile is not null)
        {
            profile = profile with { Assignments = refreshed.Value.Assignments };
            await ResolveVesselNamesAsync(profile.Assignments.Select(assignment => assignment.VesselId));
        }
    }
    private bool IsPassportExpiringSoon(DateOnly passportExpiry) => passportExpiry <= currentDate.AddMonths(6);

    private bool IsPassportExpired(DateOnly passportExpiry) => passportExpiry < currentDate;

    private CertificateState GetCertificateState(CrewCertificateProfileItemResponse certificate)
    {
        if (certificate.IsRevoked)
        {
            return new CertificateState("Revoked", "crew-profile-indicator-expired");
        }

        if (certificate.ExpiryDate < currentDate)
        {
            return new CertificateState("Expired", "crew-profile-indicator-expired");
        }

        return certificate.ExpiryDate <= currentDate.AddDays(30)
            ? new CertificateState("Expiring soon", "crew-profile-indicator-warning")
            : new CertificateState("Valid", "crew-profile-indicator-valid");
    }

    private static AssignmentState GetAssignmentState(CrewAssignmentProfileItemResponse assignment)
    {
        if (assignment.ActualSignOffDate is not null && !string.Equals(assignment.Status, "Cancelled", StringComparison.OrdinalIgnoreCase))
        {
            return new AssignmentState("Completed", "crew-profile-indicator-neutral");
        }

        return assignment.Status switch
        {
            "Active" => new AssignmentState("Active", "crew-profile-indicator-valid"),
            "Completed" => new AssignmentState("Completed", "crew-profile-indicator-neutral"),
            "Cancelled" => new AssignmentState("Cancelled", "crew-profile-indicator-expired"),
            _ => new AssignmentState("Planned", "crew-profile-indicator-warning"),
        };
    }

    private static string GetEmploymentStatusClass(string status)
    {
        if (string.Equals(status, "Active", StringComparison.OrdinalIgnoreCase)) return "employment-active";
        if (string.Equals(status, "OnLeave", StringComparison.OrdinalIgnoreCase)) return "employment-onleave";
        if (string.Equals(status, "Suspended", StringComparison.OrdinalIgnoreCase)) return "employment-suspended";
        if (string.Equals(status, "Terminated", StringComparison.OrdinalIgnoreCase)) return "employment-terminated";
        return "employment-inactive";
    }

    private async Task ResolveVesselNamesAsync(IEnumerable<Guid> vesselIds)
    {
        var ids = vesselIds.Distinct().ToArray();

        try
        {
            vesselNames = await FleetApi.LookupVesselNamesAsync(ids, disposeCancellation.Token);
        }
        catch (OperationCanceledException) when (disposeCancellation.IsCancellationRequested)
        {
            // The profile is being disposed; there is no UI left to update.
        }
    }

    private string VesselDisplayName(Guid vesselId) =>
        vesselNames.TryGetValue(vesselId, out var name) ? name : "Unknown vessel";

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

    private sealed class EmploymentStatusFormModel
    {
        public string? Status { get; set; }
        public string? Notes { get; set; }
    }

    private sealed class AvailabilityRequestFormModel
    {
        public string? Type { get; set; }
        public DateOnly? StartDate { get; set; }
        public DateOnly? EndDate { get; set; }
        public string? Reason { get; set; }
        public string? Notes { get; set; }
    }
}
