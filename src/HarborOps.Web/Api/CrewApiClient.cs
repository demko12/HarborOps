using System.Globalization;
using System.Net;
using System.Net.Http.Json;

namespace HarborOps.Web.Api;

internal sealed class CrewApiClient(HttpClient httpClient)
{
    internal async Task<CrewApiResult<CrewMemberPageResponse>> GetMembersAsync(
        string? search,
        string? employmentStatus,
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var path = $"api/crew/members?search={Uri.EscapeDataString(search ?? string.Empty)}&employmentStatus={Uri.EscapeDataString(employmentStatus ?? string.Empty)}&pageNumber={pageNumber}&pageSize={pageSize}";
        try
        {
            using var response = await httpClient.GetAsync(path, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                return new CrewApiResult<CrewMemberPageResponse>(
                    await response.Content.ReadFromJsonAsync<CrewMemberPageResponse>(cancellationToken), null);
            }

            var problem = await TryReadProblemAsync(response, cancellationToken);
            var isForbidden = response.StatusCode == HttpStatusCode.Forbidden;
            return new CrewApiResult<CrewMemberPageResponse>(null, new CrewApiFailure(
                (int)response.StatusCode,
                problem?.Title ?? (isForbidden ? "Crew access denied" : "Unable to load crew roster"),
                problem?.Detail ?? (isForbidden ? "Your account does not have permission to view crew members." : "Please try again.")));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException)
        {
            return new CrewApiResult<CrewMemberPageResponse>(null, new CrewApiFailure(0, "HarborOps API is unavailable", "Please try again."));
        }
    }

    private static async Task<ApiProblemDetails?> TryReadProblemAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try { return await response.Content.ReadFromJsonAsync<ApiProblemDetails>(cancellationToken); }
        catch (System.Text.Json.JsonException) { return null; }
    }

    internal async Task<CrewApiResult<CrewMemberProfileResponse>> GetProfileAsync(
    Guid crewMemberId,
    CancellationToken cancellationToken)
    {
        try
        {
            using var response = await httpClient.GetAsync(
                $"api/crew/members/{crewMemberId}/profile",
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                return new CrewApiResult<CrewMemberProfileResponse>(
                    await response.Content.ReadFromJsonAsync<CrewMemberProfileResponse>(cancellationToken), null);
            }

            var problem = await TryReadProblemAsync(response, cancellationToken);
            var isForbidden = response.StatusCode == HttpStatusCode.Forbidden;
            var isNotFound = response.StatusCode == HttpStatusCode.NotFound;
            return new CrewApiResult<CrewMemberProfileResponse>(null, new CrewApiFailure(
                (int)response.StatusCode,
                problem?.Title ?? (isForbidden ? "Crew access denied" : isNotFound ? "Crew member not found" : "Unable to load crew profile"),
                problem?.Detail ?? (isForbidden ? "Your account does not have permission to view this profile." : "Please try again.")));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException)
        {
            return new CrewApiResult<CrewMemberProfileResponse>(null, new CrewApiFailure(0, "HarborOps API is unavailable", "Please try again."));
        }
    }

    internal async Task<CrewApiResult<CrewPositionsResponse>> GetPositionsAsync(
        string? search,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var path = $"api/crew/positions?search={Uri.EscapeDataString(search ?? string.Empty)}&pageSize={pageSize}";
        try
        {
            using var response = await httpClient.GetAsync(path, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                return new CrewApiResult<CrewPositionsResponse>(
                    await response.Content.ReadFromJsonAsync<CrewPositionsResponse>(cancellationToken), null);
            }

            var problem = await TryReadProblemAsync(response, cancellationToken);
            return new CrewApiResult<CrewPositionsResponse>(null, new CrewApiFailure(
                (int)response.StatusCode,
                problem?.Title ?? "Unable to load positions",
                problem?.Detail ?? "Please try again."));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException)
        {
            return new CrewApiResult<CrewPositionsResponse>(null, new CrewApiFailure(0, "HarborOps API is unavailable", "Please try again."));
        }
    }

    internal async Task<CrewApiWriteResult<RegisterCrewCertificateResponse>> RegisterCertificateAsync(
        Guid crewMemberId,
        RegisterCrewCertificateRequest request,
        CancellationToken cancellationToken)
    {
        return await PostAsync<RegisterCrewCertificateRequest, RegisterCrewCertificateResponse>(
            $"api/crew/members/{crewMemberId}/certificates", request, cancellationToken);
    }

    internal async Task<CrewApiWriteResult<RegisterCrewAssignmentResponse>> RegisterAssignmentAsync(
        RegisterCrewAssignmentRequest request,
        CancellationToken cancellationToken)
    {
        return await PostAsync<RegisterCrewAssignmentRequest, RegisterCrewAssignmentResponse>(
            "api/crew/assignments", request, cancellationToken);
    }

    internal Task<CrewApiWriteResult<bool>> ActivateAssignmentAsync(
        Guid assignmentId,
        CancellationToken cancellationToken)
        => PostEmptyAsync($"api/crew/assignments/{assignmentId}/activate", new { }, cancellationToken, "Unable to activate assignment");

    internal Task<CrewApiWriteResult<bool>> CompleteAssignmentAsync(
        Guid assignmentId,
        CompleteCrewAssignmentRequest request,
        CancellationToken cancellationToken)
        => PostEmptyAsync($"api/crew/assignments/{assignmentId}/complete", request, cancellationToken, "Unable to complete assignment");

    internal Task<CrewApiWriteResult<bool>> CancelAssignmentAsync(
        Guid assignmentId,
        CancelCrewAssignmentRequest request,
        CancellationToken cancellationToken)
        => PostEmptyAsync($"api/crew/assignments/{assignmentId}/cancel", request, cancellationToken, "Unable to cancel assignment");

    internal Task<CrewApiWriteResult<bool>> CancelRotationAsync(
        Guid rotationId,
        CancelCrewRotationRequest request,
        CancellationToken cancellationToken)
        => PostEmptyAsync($"api/crew/rotations/{rotationId}/cancel", request, cancellationToken, "Unable to cancel rotation");

    private async Task<CrewApiWriteResult<bool>> PostEmptyAsync<TRequest>(
        string path,
        TRequest? request,
        CancellationToken cancellationToken,
        string action)
    {
        try
        {
            using var response = await httpClient.PostAsJsonAsync(path, request, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                return new CrewApiWriteResult<bool>(true, null);
            }

            var problem = await TryReadProblemAsync(response, cancellationToken);
            var isForbidden = response.StatusCode == HttpStatusCode.Forbidden;
            var isNotFound = response.StatusCode == HttpStatusCode.NotFound;
            var isConflict = response.StatusCode == HttpStatusCode.Conflict;

            return new CrewApiWriteResult<bool>(false, new CrewApiFailure(
                (int)response.StatusCode,
                problem?.Title ?? (isForbidden ? "Crew access denied"
                    : isNotFound ? "Crew assignment not found"
                    : isConflict ? "Conflict"
                    : action),
                problem?.Detail ?? "Please try again.",
                problem?.Errors ?? EmptyErrors));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException)
        {
            return new CrewApiWriteResult<bool>(false, new CrewApiFailure(0, "HarborOps API is unavailable", "Please try again.", EmptyErrors));
        }
    }

    internal async Task<CrewApiResult<CrewRotationsPageResponse>> GetRotationsAsync(
        Guid? vesselId,
        DateOnly? from,
        DateOnly? to,
        bool? overdueOnly,
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var query = new List<string> { $"pageNumber={pageNumber}", $"pageSize={pageSize}" };

        if (vesselId is { } vessel)
        {
            query.Add($"vesselId={vessel}");
        }

        if (from is { } fromDate)
        {
            query.Add($"from={FormatIsoDate(fromDate)}");
        }

        if (to is { } toDate)
        {
            query.Add($"to={FormatIsoDate(toDate)}");
        }

        if (overdueOnly is { } overdue)
        {
            query.Add($"overdueOnly={(overdue ? "true" : "false")}");
        }

        var path = $"api/crew/rotations?{string.Join('&', query)}";

        try
        {
            using var response = await httpClient.GetAsync(path, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                return new CrewApiResult<CrewRotationsPageResponse>(
                    await response.Content.ReadFromJsonAsync<CrewRotationsPageResponse>(cancellationToken), null);
            }

            var problem = await TryReadProblemAsync(response, cancellationToken);
            var isForbidden = response.StatusCode == HttpStatusCode.Forbidden;
            return new CrewApiResult<CrewRotationsPageResponse>(null, new CrewApiFailure(
                (int)response.StatusCode,
                problem?.Title ?? (isForbidden ? "Crew access denied" : "Unable to load crew rotations"),
                problem?.Detail ?? (isForbidden ? "Your account does not have permission to view crew rotations." : "Please try again.")));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException)
        {
            return new CrewApiResult<CrewRotationsPageResponse>(null, new CrewApiFailure(0, "HarborOps API is unavailable", "Please try again."));
        }
    }

    internal async Task<CrewApiResult<CrewRotationListItemResponse>> GetRotationAsync(
        Guid assignmentId,
        CancellationToken cancellationToken)
    {
        try
        {
            using var response = await httpClient.GetAsync(
                $"api/crew/assignments/{assignmentId}/rotation",
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                return new CrewApiResult<CrewRotationListItemResponse>(
                    await response.Content.ReadFromJsonAsync<CrewRotationListItemResponse>(cancellationToken), null);
            }

            var problem = await TryReadProblemAsync(response, cancellationToken);
            var isForbidden = response.StatusCode == HttpStatusCode.Forbidden;
            var isNotFound = response.StatusCode == HttpStatusCode.NotFound;
            return new CrewApiResult<CrewRotationListItemResponse>(null, new CrewApiFailure(
                (int)response.StatusCode,
                problem?.Title ?? (isForbidden ? "Crew access denied" : isNotFound ? "No rotation planned" : "Unable to load the crew rotation"),
                problem?.Detail ?? (isForbidden ? "Your account does not have permission to view this rotation." : "Please try again.")));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException)
        {
            return new CrewApiResult<CrewRotationListItemResponse>(null, new CrewApiFailure(0, "HarborOps API is unavailable", "Please try again."));
        }
    }

    internal async Task<CrewApiWriteResult<PlanCrewRotationResponse>> PlanRotationAsync(
        Guid assignmentId,
        PlanCrewRotationRequest request,
        CancellationToken cancellationToken)
    {
        return await PostAsync<PlanCrewRotationRequest, PlanCrewRotationResponse>(
            $"api/crew/assignments/{assignmentId}/rotation", request, cancellationToken);
    }

    internal async Task<CrewApiWriteResult<UpdateCrewRotationResponse>> UpdateRotationAsync(
        Guid assignmentId,
        UpdateCrewRotationRequest request,
        CancellationToken cancellationToken)
    {
        return await PutAsync<UpdateCrewRotationRequest, UpdateCrewRotationResponse>(
            $"api/crew/assignments/{assignmentId}/rotation", request, cancellationToken);
    }

    internal async Task<CrewApiWriteResult<ExecuteCrewRotationResponse>> ExecuteRotationAsync(
        Guid assignmentId,
        CancellationToken cancellationToken)
    {
        try
        {
            using var response = await httpClient.PostAsync(
                $"api/crew/assignments/{assignmentId}/rotation/execute", null, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                return new CrewApiWriteResult<ExecuteCrewRotationResponse>(
                    await response.Content.ReadFromJsonAsync<ExecuteCrewRotationResponse>(cancellationToken), null);
            }

            var problem = await TryReadProblemAsync(response, cancellationToken);
            var isForbidden = response.StatusCode == HttpStatusCode.Forbidden;
            var isNotFound = response.StatusCode == HttpStatusCode.NotFound;
            var isConflict = response.StatusCode == HttpStatusCode.Conflict;

            return new CrewApiWriteResult<ExecuteCrewRotationResponse>(default, new CrewApiFailure(
                (int)response.StatusCode,
                problem?.Title ?? (isForbidden ? "Crew access denied"
                    : isNotFound ? "Related record not found"
                    : isConflict ? "Conflict"
                    : "Unable to execute the rotation"),
                problem?.Detail ?? "Please try again.",
                problem?.Errors ?? EmptyErrors));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException)
        {
            return new CrewApiWriteResult<ExecuteCrewRotationResponse>(default, new CrewApiFailure(0, "HarborOps API is unavailable", "Please try again.", EmptyErrors));
        }
    }

    internal async Task<CrewApiResult<MedicalExaminationsPageResponse>> GetMedicalExaminationsAsync(
        string? search,
        string? fitnessResult,
        bool? expired,
        int? expiringWithinDays,
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var query = new List<string> { $"pageNumber={pageNumber}", $"pageSize={pageSize}" };

        if (!string.IsNullOrWhiteSpace(search))
        {
            query.Add($"search={Uri.EscapeDataString(search)}");
        }

        if (!string.IsNullOrWhiteSpace(fitnessResult))
        {
            query.Add($"fitnessResult={Uri.EscapeDataString(fitnessResult)}");
        }

        if (expired is { } isExpired)
        {
            query.Add($"expired={(isExpired ? "true" : "false")}");
        }

        if (expiringWithinDays is { } withinDays)
        {
            query.Add($"expiringWithinDays={withinDays}");
        }

        var path = $"api/crew/medical-examinations?{string.Join('&', query)}";

        try
        {
            using var response = await httpClient.GetAsync(path, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                return new CrewApiResult<MedicalExaminationsPageResponse>(
                    await response.Content.ReadFromJsonAsync<MedicalExaminationsPageResponse>(cancellationToken), null);
            }

            var problem = await TryReadProblemAsync(response, cancellationToken);
            var isForbidden = response.StatusCode == HttpStatusCode.Forbidden;
            return new CrewApiResult<MedicalExaminationsPageResponse>(null, new CrewApiFailure(
                (int)response.StatusCode,
                problem?.Title ?? (isForbidden ? "Crew access denied" : "Unable to load medical examinations"),
                problem?.Detail ?? (isForbidden ? "Your account does not have permission to view medical examinations." : "Please try again.")));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException)
        {
            return new CrewApiResult<MedicalExaminationsPageResponse>(null, new CrewApiFailure(0, "HarborOps API is unavailable", "Please try again."));
        }
    }

    internal async Task<CrewApiResult<MemberMedicalExaminationsResponse>> GetMemberMedicalExaminationsAsync(
        Guid crewMemberId,
        CancellationToken cancellationToken)
    {
        try
        {
            using var response = await httpClient.GetAsync(
                $"api/crew/members/{crewMemberId}/medical-examinations",
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                return new CrewApiResult<MemberMedicalExaminationsResponse>(
                    await response.Content.ReadFromJsonAsync<MemberMedicalExaminationsResponse>(cancellationToken), null);
            }

            var problem = await TryReadProblemAsync(response, cancellationToken);
            var isForbidden = response.StatusCode == HttpStatusCode.Forbidden;
            var isNotFound = response.StatusCode == HttpStatusCode.NotFound;
            return new CrewApiResult<MemberMedicalExaminationsResponse>(null, new CrewApiFailure(
                (int)response.StatusCode,
                problem?.Title ?? (isForbidden ? "Crew access denied" : isNotFound ? "Crew member not found" : "Unable to load medical examinations"),
                problem?.Detail ?? (isForbidden ? "Your account does not have permission to view medical examinations." : "Please try again.")));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException)
        {
            return new CrewApiResult<MemberMedicalExaminationsResponse>(null, new CrewApiFailure(0, "HarborOps API is unavailable", "Please try again."));
        }
    }

    internal async Task<CrewApiWriteResult<RegisterMedicalExaminationResponse>> RegisterMedicalExaminationAsync(
        Guid crewMemberId,
        RegisterMedicalExaminationRequest request,
        CancellationToken cancellationToken)
    {
        return await PostAsync<RegisterMedicalExaminationRequest, RegisterMedicalExaminationResponse>(
            $"api/crew/members/{crewMemberId}/medical-examinations", request, cancellationToken);
    }

    internal static string FormatIsoDate(DateOnly value) => value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private async Task<CrewApiWriteResult<TResponse>> PutAsync<TRequest, TResponse>(
        string path,
        TRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            using var response = await httpClient.PutAsJsonAsync(path, request, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                return new CrewApiWriteResult<TResponse>(
                    await response.Content.ReadFromJsonAsync<TResponse>(cancellationToken), null);
            }

            var problem = await TryReadProblemAsync(response, cancellationToken);
            var isForbidden = response.StatusCode == HttpStatusCode.Forbidden;
            var isNotFound = response.StatusCode == HttpStatusCode.NotFound;
            var isConflict = response.StatusCode == HttpStatusCode.Conflict;

            return new CrewApiWriteResult<TResponse>(default, new CrewApiFailure(
                (int)response.StatusCode,
                problem?.Title ?? (isForbidden ? "Crew access denied"
                    : isNotFound ? "Related record not found"
                    : isConflict ? "Conflict"
                    : "Unable to save"),
                problem?.Detail ?? "Please try again.",
                problem?.Errors ?? EmptyErrors));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException)
        {
            return new CrewApiWriteResult<TResponse>(default, new CrewApiFailure(0, "HarborOps API is unavailable", "Please try again.", EmptyErrors));
        }
    }

    private async Task<CrewApiWriteResult<TResponse>> PostAsync<TRequest, TResponse>(
    string path,
    TRequest request,
    CancellationToken cancellationToken)
    {
        try
        {
            using var response = await httpClient.PostAsJsonAsync(path, request, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                return new CrewApiWriteResult<TResponse>(
                    await response.Content.ReadFromJsonAsync<TResponse>(cancellationToken), null);
            }

            var problem = await TryReadProblemAsync(response, cancellationToken);
            var isForbidden = response.StatusCode == HttpStatusCode.Forbidden;
            var isNotFound = response.StatusCode == HttpStatusCode.NotFound;
            var isConflict = response.StatusCode == HttpStatusCode.Conflict;

            return new CrewApiWriteResult<TResponse>(default, new CrewApiFailure(
                (int)response.StatusCode,
                problem?.Title ?? (isForbidden ? "Crew access denied"
                    : isNotFound ? "Related record not found"
                    : isConflict ? "Conflict"
                    : "Unable to save"),
                problem?.Detail ?? "Please try again.",
                problem?.Errors ?? EmptyErrors));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException)
        {
            return new CrewApiWriteResult<TResponse>(default, new CrewApiFailure(0, "HarborOps API is unavailable", "Please try again.", EmptyErrors));
        }
    }

    private static readonly IReadOnlyDictionary<string, string[]> EmptyErrors =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);

}

internal sealed record CrewMemberPageResponse(IReadOnlyList<CrewMemberListItemResponse> Items, int PageNumber, int PageSize, int TotalCount);

internal sealed record CrewMemberListItemResponse(
    Guid CrewMemberId,
    string Name,
    string Nationality,
    string ImoSeafarerId,
    Guid BaseRankId,
    string BaseRankName,
    string EmploymentStatus,
    DateOnly PassportExpiry,
    DateOnly HireDate);

internal sealed record CrewApiResult<T>(T? Value, CrewApiFailure? Failure) { internal bool IsSuccess => Failure is null; }

internal sealed record CrewApiFailure(
    int StatusCode,
    string Title,
    string Message,
    IReadOnlyDictionary<string, string[]>? Errors = null)
{
    internal bool IsValidation => StatusCode == StatusCodes.Status400BadRequest;
    internal bool IsConflict => StatusCode == StatusCodes.Status409Conflict;
    internal bool IsNotFound => StatusCode == StatusCodes.Status404NotFound;
}

internal sealed record CrewMemberProfileResponse(
    Guid CrewMemberId,
    string Name,
    string Nationality,
    string ImoSeafarerId,
    string PassportNumber,
    DateOnly DateOfBirth,
    DateOnly PassportExpiry,
    string CdcNumber,
    DateOnly HireDate,
    string EmploymentStatus,
    Guid BaseRankId,
    string BaseRankName,
    IReadOnlyList<CrewCertificateProfileItemResponse> Certificates,
    IReadOnlyList<CrewAssignmentProfileItemResponse> Assignments);

internal sealed record CrewCertificateProfileItemResponse(
    Guid CertificateId,
    string Type,
    string Name,
    string IssuingAuthority,
    string LicenseNumber,
    DateOnly IssueDate,
    DateOnly ExpiryDate,
    string Status);

internal sealed record CrewAssignmentProfileItemResponse(
    Guid AssignmentId,
    Guid VesselId,
    Guid RankId,
    string RankName,
    Guid? SignOnPortId,
    Guid? SignOffPortId,
    DateOnly SignOnDate,
    DateOnly PlannedSignOffDate,
    DateOnly? ActualSignOffDate,
    string Status,
    DateTime? CancelledAtUtc = null,
    Guid? CancelledByUserId = null,
    string? CancellationNotes = null);

internal sealed record CrewPositionsResponse(IReadOnlyList<CrewPositionListItemResponse> Items);

internal sealed record CrewPositionListItemResponse(
    Guid CrewPositionId,
    string RankName,
    string Department,
    string SeniorityLevel);

internal sealed record RegisterCrewCertificateRequest
{
    public string? Type { get; init; }
    public string? Name { get; init; }
    public string? IssuingAuthority { get; init; }
    public string? LicenseNumber { get; init; }
    public DateOnly? IssueDate { get; init; }
    public DateOnly? ExpiryDate { get; init; }
}

internal sealed record RegisterCrewCertificateResponse(Guid Id);

internal sealed record RegisterCrewAssignmentRequest
{
    public Guid? CrewMemberId { get; init; }
    public Guid? VesselId { get; init; }
    public Guid? RankId { get; init; }
    public Guid? SignOnPortId { get; init; }
    public DateOnly? SignOnDate { get; init; }
    public DateOnly? PlannedSignOffDate { get; init; }
}

internal sealed record RegisterCrewAssignmentResponse(Guid Id);

internal sealed record CrewApiWriteResult<T>(T? Value, CrewApiFailure? Failure)
{
    internal bool IsSuccess => Failure is null;
}

internal sealed record CrewRotationsPageResponse(
    IReadOnlyList<CrewRotationListItemResponse> Items, int PageNumber, int PageSize, int TotalCount);

internal sealed record CrewRotationListItemResponse(
    Guid RotationId,
    Guid AssignmentId,
    Guid VesselId,
    Guid CrewMemberId,
    string CrewMemberName,
    Guid RankId,
    string RankName,
    Guid? RelievingCrewMemberId,
    string? RelievingCrewMemberName,
    string RotationPattern,
    DateOnly PlannedReliefDate,
    bool IsOverdue,
    string Status = "Planned",
    DateTime? ExecutedAtUtc = null,
    Guid? ExecutedByUserId = null,
    Guid? ReplacementAssignmentId = null,
    DateTime? CancelledAtUtc = null,
    Guid? CancelledByUserId = null,
    string? CancellationNotes = null);

internal sealed record CompleteCrewAssignmentRequest
{
    public DateOnly? ActualSignOffDate { get; init; }
    public Guid? SignOffPortId { get; init; }
}

internal sealed record CancelCrewAssignmentRequest
{
    public string? Notes { get; init; }
}

internal sealed record CancelCrewRotationRequest
{
    public string? Notes { get; init; }
}

internal sealed record MedicalExaminationsPageResponse(
    IReadOnlyList<MedicalExaminationListItemResponse> Items, int PageNumber, int PageSize, int TotalCount);

internal sealed record MemberMedicalExaminationsResponse(
    IReadOnlyList<MedicalExaminationListItemResponse> Items, string? CurrentFitnessResult);

internal sealed record MedicalExaminationListItemResponse(
    Guid MedicalExaminationId,
    Guid CrewMemberId,
    string CrewMemberName,
    string EmploymentStatus,
    string ExamType,
    string Clinic,
    DateOnly ExamDate,
    DateOnly ExpiryDate,
    string FitnessResult,
    bool IsExpired,
    bool IsExpiringSoon);

internal sealed record RegisterMedicalExaminationRequest
{
    public string? ExamType { get; init; }
    public string? Clinic { get; init; }
    public DateOnly? ExamDate { get; init; }
    public DateOnly? ExpiryDate { get; init; }
    public string? FitnessResult { get; init; }
}

internal sealed record RegisterMedicalExaminationResponse(Guid Id);

internal sealed record PlanCrewRotationRequest
{
    public Guid? RelievingCrewMemberId { get; init; }
    public string? RotationPattern { get; init; }
    public DateOnly? PlannedReliefDate { get; init; }
}

internal sealed record UpdateCrewRotationRequest
{
    public Guid? RelievingCrewMemberId { get; init; }
    public string? RotationPattern { get; init; }
    public DateOnly? PlannedReliefDate { get; init; }
}

internal sealed record PlanCrewRotationResponse(Guid Id);

internal sealed record UpdateCrewRotationResponse(Guid Id);

internal sealed record ExecuteCrewRotationResponse(
    Guid AssignmentId,
    DateOnly ActualSignOffDate,
    Guid ReplacementAssignmentId,
    DateOnly ReplacementSignOnDate,
    DateOnly ReplacementPlannedSignOffDate);
