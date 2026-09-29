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

    internal async Task<CrewApiWriteResult<UpdateCrewMemberResponse>> UpdateProfileAsync(
        Guid crewMemberId,
        UpdateCrewMemberRequest request,
        CancellationToken cancellationToken)
    {
        return await PutAsync<UpdateCrewMemberRequest, UpdateCrewMemberResponse>(
            $"api/crew/members/{crewMemberId}", request, cancellationToken);
    }

    internal async Task<CrewApiWriteResult<ChangeCrewMemberEmploymentStatusResponse>> ChangeEmploymentStatusAsync(
        Guid crewMemberId,
        ChangeCrewMemberEmploymentStatusRequest request,
        CancellationToken cancellationToken)
    {
        return await PostAsync<ChangeCrewMemberEmploymentStatusRequest, ChangeCrewMemberEmploymentStatusResponse>(
            $"api/crew/members/{crewMemberId}/employment-status-changes", request, cancellationToken);
    }

    internal async Task<CrewApiResult<GetCrewEmploymentStatusHistoryResponse>> GetEmploymentStatusHistoryAsync(
        Guid crewMemberId,
        CancellationToken cancellationToken)
    {
        try
        {
            using var response = await httpClient.GetAsync(
                $"api/crew/members/{crewMemberId}/employment-status-history",
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                return new CrewApiResult<GetCrewEmploymentStatusHistoryResponse>(
                    await response.Content.ReadFromJsonAsync<GetCrewEmploymentStatusHistoryResponse>(cancellationToken), null);
            }

            var problem = await TryReadProblemAsync(response, cancellationToken);
            var isForbidden = response.StatusCode == HttpStatusCode.Forbidden;
            var isNotFound = response.StatusCode == HttpStatusCode.NotFound;
            return new CrewApiResult<GetCrewEmploymentStatusHistoryResponse>(null, new CrewApiFailure(
                (int)response.StatusCode,
                problem?.Title ?? (isForbidden ? "Crew access denied" : isNotFound ? "Crew member not found" : "Unable to load employment history"),
                problem?.Detail ?? (isForbidden ? "Your account does not have permission to view this history." : "Please try again.")));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException)
        {
            return new CrewApiResult<GetCrewEmploymentStatusHistoryResponse>(null, new CrewApiFailure(0, "HarborOps API is unavailable", "Please try again."));
        }
    }

    internal async Task<CrewApiResult<CrewPositionsResponse>> GetPositionsAsync(
        string? search,
        int pageSize,
        CancellationToken cancellationToken,
        string? department = null,
        int pageNumber = 1)
    {
        var path = $"api/crew/positions?search={Uri.EscapeDataString(search ?? string.Empty)}" +
            $"&department={Uri.EscapeDataString(department ?? string.Empty)}" +
            $"&pageNumber={pageNumber}&pageSize={pageSize}";
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

    internal async Task<CrewApiWriteResult<RegisterCrewPositionResponse>> RegisterPositionAsync(
        RegisterCrewPositionRequest request, CancellationToken cancellationToken)
    {
        return await PostAsync<RegisterCrewPositionRequest, RegisterCrewPositionResponse>(
            "api/crew/positions", request, cancellationToken);
    }

    internal async Task<CrewApiWriteResult<UpdateCrewPositionResponse>> UpdatePositionAsync(
        Guid positionId, UpdateCrewPositionRequest request, CancellationToken cancellationToken)
    {
        return await PutAsync<UpdateCrewPositionRequest, UpdateCrewPositionResponse>(
            $"api/crew/positions/{positionId}", request, cancellationToken);
    }

    internal async Task<CrewApiWriteResult<RegisterCrewCertificateResponse>> RegisterCertificateAsync(
        Guid crewMemberId,
        RegisterCrewCertificateRequest request,
        CancellationToken cancellationToken)
    {
        return await PostAsync<RegisterCrewCertificateRequest, RegisterCrewCertificateResponse>(
            $"api/crew/members/{crewMemberId}/certificates", request, cancellationToken);
    }

    internal Task<CrewApiWriteResult<RenewCrewCertificateResponse>> RenewCertificateAsync(
        Guid certificateId,
        RenewCrewCertificateRequest request,
        CancellationToken cancellationToken)
        => PostAsync<RenewCrewCertificateRequest, RenewCrewCertificateResponse>(
            $"api/crew/certificates/{certificateId}/renew", request, cancellationToken);

    internal Task<CrewApiWriteResult<RevokeCrewCertificateResponse>> RevokeCertificateAsync(
        Guid certificateId,
        RevokeCrewCertificateRequest request,
        CancellationToken cancellationToken)
        => PostAsync<RevokeCrewCertificateRequest, RevokeCrewCertificateResponse>(
            $"api/crew/certificates/{certificateId}/revoke", request, cancellationToken);

    internal Task<CrewApiResult<GetCrewCertificateHistoryResponse>> GetCertificateHistoryAsync(
        Guid certificateId,
        CancellationToken cancellationToken)
        => GetJsonAsync<GetCrewCertificateHistoryResponse>(
            $"api/crew/certificates/{certificateId}/history", "Unable to load certificate history", cancellationToken);

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

    internal async Task<CrewApiResult<GetCrewAvailabilityResponse>> GetAvailabilityAsync(
        Guid? crewMemberId,
        DateOnly? from,
        DateOnly? to,
        string? type,
        string? status,
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var query = new List<string> { $"pageNumber={pageNumber}", $"pageSize={pageSize}" };

        if (crewMemberId is { } member)
        {
            query.Add($"crewMemberId={member}");
        }

        if (from is { } fromDate)
        {
            query.Add($"from={FormatIsoDate(fromDate)}");
        }

        if (to is { } toDate)
        {
            query.Add($"to={FormatIsoDate(toDate)}");
        }

        if (!string.IsNullOrWhiteSpace(type))
        {
            query.Add($"type={Uri.EscapeDataString(type)}");
        }

        if (!string.IsNullOrWhiteSpace(status))
        {
            query.Add($"status={Uri.EscapeDataString(status)}");
        }

        return await GetJsonAsync<GetCrewAvailabilityResponse>(
            $"api/crew/availability?{string.Join('&', query)}", "Unable to load crew availability", cancellationToken);
    }

    internal Task<CrewApiResult<GetCrewMemberAvailabilityResponse>> GetMemberAvailabilityAsync(
        Guid crewMemberId,
        CancellationToken cancellationToken)
        => GetJsonAsync<GetCrewMemberAvailabilityResponse>(
            $"api/crew/members/{crewMemberId}/availability", "Unable to load crew availability", cancellationToken);

    internal Task<CrewApiResult<GetCrewAvailabilityConflictsResponse>> GetAvailabilityConflictsAsync(
        Guid crewMemberId,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken)
        => GetJsonAsync<GetCrewAvailabilityConflictsResponse>(
            $"api/crew/availability/conflicts?crewMemberId={crewMemberId}&from={FormatIsoDate(from)}&to={FormatIsoDate(to)}",
            "Unable to check crew availability",
            cancellationToken);

    internal Task<CrewApiWriteResult<CreateCrewAvailabilityRequestResponse>> RequestAvailabilityAsync(
        Guid crewMemberId,
        CreateCrewAvailabilityRequestRequest request,
        CancellationToken cancellationToken)
        => PostAsync<CreateCrewAvailabilityRequestRequest, CreateCrewAvailabilityRequestResponse>(
            $"api/crew/members/{crewMemberId}/availability-requests", request, cancellationToken);

    internal Task<CrewApiWriteResult<CrewAvailabilityDecisionResponse>> ApproveAvailabilityRequestAsync(
        Guid requestId,
        CancellationToken cancellationToken)
        => PostAsync<object?, CrewAvailabilityDecisionResponse>(
            $"api/crew/availability-requests/{requestId}/approve", null, cancellationToken);

    internal Task<CrewApiWriteResult<CrewAvailabilityDecisionResponse>> RejectAvailabilityRequestAsync(
        Guid requestId,
        RejectCrewAvailabilityRequestRequest request,
        CancellationToken cancellationToken)
        => PostAsync<RejectCrewAvailabilityRequestRequest, CrewAvailabilityDecisionResponse>(
            $"api/crew/availability-requests/{requestId}/reject", request, cancellationToken);

    internal Task<CrewApiWriteResult<CrewAvailabilityDecisionResponse>> CancelAvailabilityRequestAsync(
        Guid requestId,
        CancelCrewAvailabilityRequestRequest request,
        CancellationToken cancellationToken)
        => PostAsync<CancelCrewAvailabilityRequestRequest, CrewAvailabilityDecisionResponse>(
            $"api/crew/availability-requests/{requestId}/cancel", request, cancellationToken);

    internal Task<CrewApiResult<VesselManningPlansPageResponse>> GetManningPlansAsync(
        Guid? vesselId,
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var query = new List<string> { $"pageNumber={pageNumber}", $"pageSize={pageSize}" };

        if (vesselId is { } vessel)
        {
            query.Add($"vesselId={vessel}");
        }

        return GetJsonAsync<VesselManningPlansPageResponse>(
            $"api/crew/manning-plans?{string.Join('&', query)}", "Unable to load manning plans", cancellationToken);
    }

    internal Task<CrewApiResult<VesselManningPlanDetailResponse>> GetManningPlanDetailAsync(
        Guid planId,
        CancellationToken cancellationToken)
        => GetJsonAsync<VesselManningPlanDetailResponse>(
            $"api/crew/manning-plans/{planId}", "Unable to load the manning plan", cancellationToken);

    internal Task<CrewApiWriteResult<CreateVesselManningPlanResponse>> CreateManningPlanAsync(
        CreateVesselManningPlanRequest request,
        CancellationToken cancellationToken)
        => PostAsync<CreateVesselManningPlanRequest, CreateVesselManningPlanResponse>(
            "api/crew/manning-plans", request, cancellationToken);

    internal Task<CrewApiWriteResult<ReplaceVesselManningPlanLinesResponse>> ReplaceManningPlanLinesAsync(
        Guid planId,
        ReplaceVesselManningPlanLinesRequest request,
        CancellationToken cancellationToken)
        => PutAsync<ReplaceVesselManningPlanLinesRequest, ReplaceVesselManningPlanLinesResponse>(
            $"api/crew/manning-plans/{planId}/lines", request, cancellationToken);

    internal Task<CrewApiWriteResult<EndVesselManningPlanResponse>> EndManningPlanAsync(
        Guid planId,
        EndVesselManningPlanRequest request,
        CancellationToken cancellationToken)
        => PostAsync<EndVesselManningPlanRequest, EndVesselManningPlanResponse>(
            $"api/crew/manning-plans/{planId}/end-date", request, cancellationToken);

    internal Task<CrewApiResult<GetCrewManningCoverageResponse>> GetManningCoverageAsync(
        Guid vesselId,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken)
        => GetJsonAsync<GetCrewManningCoverageResponse>(
            $"api/crew/manning-coverage?vesselId={vesselId}&from={FormatIsoDate(from)}&to={FormatIsoDate(to)}",
            "Unable to load manning coverage",
            cancellationToken);

    internal Task<CrewApiResult<GetQualificationPoliciesResponse>> GetQualificationPoliciesAsync(
        Guid? crewPositionId,
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var query = new List<string> { $"pageNumber={pageNumber}", $"pageSize={pageSize}" };

        if (crewPositionId is { } id)
        {
            query.Add($"crewPositionId={id}");
        }

        return GetJsonAsync<GetQualificationPoliciesResponse>(
            $"api/crew/qualification-policies?{string.Join('&', query)}", "Unable to load qualification policies", cancellationToken);
    }

    internal Task<CrewApiResult<GetQualificationPolicyDetailResponse>> GetQualificationPolicyDetailAsync(
        Guid policyId,
        CancellationToken cancellationToken)
        => GetJsonAsync<GetQualificationPolicyDetailResponse>(
            $"api/crew/qualification-policies/{policyId}", "Unable to load the qualification policy", cancellationToken);

    internal Task<CrewApiWriteResult<CreateQualificationPolicyResponse>> CreateQualificationPolicyAsync(
        CreateQualificationPolicyRequest request,
        CancellationToken cancellationToken)
        => PostAsync<CreateQualificationPolicyRequest, CreateQualificationPolicyResponse>(
            "api/crew/qualification-policies", request, cancellationToken);

    internal Task<CrewApiWriteResult<UpdateQualificationPolicyResponse>> UpdateQualificationPolicyAsync(
        Guid policyId,
        UpdateQualificationPolicyRequest request,
        CancellationToken cancellationToken)
        => PutAsync<UpdateQualificationPolicyRequest, UpdateQualificationPolicyResponse>(
            $"api/crew/qualification-policies/{policyId}", request, cancellationToken);

    internal Task<CrewApiResult<GetCrewQualificationEligibilityResponse>> GetQualificationEligibilityAsync(
        Guid crewMemberId,
        Guid rankId,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken)
        => GetJsonAsync<GetCrewQualificationEligibilityResponse>(
            $"api/crew/qualification-eligibility?crewMemberId={crewMemberId}&rankId={rankId}&from={FormatIsoDate(from)}&to={FormatIsoDate(to)}",
            "Unable to check crew qualification",
            cancellationToken);

    private async Task<CrewApiResult<T>> GetJsonAsync<T>(
        string path,
        string failureTitle,
        CancellationToken cancellationToken)
    {
        try
        {
            using var response = await httpClient.GetAsync(path, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                return new CrewApiResult<T>(
                    await response.Content.ReadFromJsonAsync<T>(cancellationToken), null);
            }

            var problem = await TryReadProblemAsync(response, cancellationToken);
            var isForbidden = response.StatusCode == HttpStatusCode.Forbidden;
            var isNotFound = response.StatusCode == HttpStatusCode.NotFound;

            return new CrewApiResult<T>(default, new CrewApiFailure(
                (int)response.StatusCode,
                problem?.Title ?? (isForbidden ? "Crew access denied" : isNotFound ? "Not found" : failureTitle),
                problem?.Detail ?? (isForbidden
                    ? "Your account does not have permission to view crew availability."
                    : "Please try again."),
                problem?.Errors ?? EmptyErrors));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException)
        {
            return new CrewApiResult<T>(default, new CrewApiFailure(0, "HarborOps API is unavailable", "Please try again.", EmptyErrors));
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
    string FirstName,
    string LastName,
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

internal sealed record UpdateCrewMemberRequest
{
    public string? FirstName { get; init; }
    public string? LastName { get; init; }
    public DateOnly? DateOfBirth { get; init; }
    public string? Nationality { get; init; }
    public string? PassportNumber { get; init; }
    public DateOnly? PassportExpiry { get; init; }
    public string? CdcNumber { get; init; }
    public DateOnly? HireDate { get; init; }
    public Guid? BaseRankId { get; init; }
}

internal sealed record UpdateCrewMemberResponse(Guid Id);

internal sealed record ChangeCrewMemberEmploymentStatusRequest
{
    public string? Status { get; init; }
    public string? Notes { get; init; }
}

internal sealed record ChangeCrewMemberEmploymentStatusResponse(
    Guid Id,
    Guid CrewMemberId,
    string PreviousStatus,
    string NewStatus,
    Guid ChangedByUserId,
    string? Notes,
    DateTime ChangedAtUtc);

internal sealed record CrewEmploymentStatusHistoryItemResponse(
    Guid Id,
    string Status,
    Guid ChangedByUserId,
    string? Notes,
    DateTime ChangedAtUtc);

internal sealed record GetCrewEmploymentStatusHistoryResponse(IReadOnlyList<CrewEmploymentStatusHistoryItemResponse> Items);

internal sealed record CrewCertificateProfileItemResponse(
    Guid CertificateId,
    string Type,
    string Name,
    string IssuingAuthority,
    string LicenseNumber,
    DateOnly IssueDate,
    DateOnly ExpiryDate,
    string Status,
    bool IsRevoked,
    Guid? PredecessorCertificateId,
    Guid? SuccessorCertificateId,
    Guid ConcurrencyToken);

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

internal sealed record CrewPositionsResponse(
    IReadOnlyList<CrewPositionListItemResponse> Items,
    int PageNumber,
    int PageSize,
    int TotalCount);

internal sealed record CrewPositionListItemResponse(
    Guid CrewPositionId,
    string RankName,
    string Department,
    string SeniorityLevel,
    int CrewMemberUsageCount,
    int AssignmentUsageCount);

internal sealed record RegisterCrewPositionRequest
{
    public string? RankName { get; init; }
    public string? Department { get; init; }
    public string? SeniorityLevel { get; init; }
}

internal sealed record RegisterCrewPositionResponse(Guid Id);

internal sealed record UpdateCrewPositionRequest
{
    public string? RankName { get; init; }
    public string? Department { get; init; }
    public string? SeniorityLevel { get; init; }
}

internal sealed record UpdateCrewPositionResponse(Guid Id);

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

internal sealed record RenewCrewCertificateRequest
{
    public string? Name { get; init; }
    public string? IssuingAuthority { get; init; }
    public string? LicenseNumber { get; init; }
    public DateOnly? IssueDate { get; init; }
    public DateOnly? ExpiryDate { get; init; }
}

internal sealed record RenewCrewCertificateResponse(
    Guid Id,
    Guid PredecessorCertificateId,
    Guid CrewMemberId,
    string Type,
    string Status,
    Guid ConcurrencyToken);

internal sealed record RevokeCrewCertificateRequest
{
    public string? Reason { get; init; }
}

internal sealed record AffectedCertificateAssignmentResponse(Guid AssignmentId, Guid VesselId, Guid RankId);

internal sealed record RevokeCrewCertificateResponse(
    Guid Id,
    DateTime RevokedAtUtc,
    Guid ConcurrencyToken,
    IReadOnlyList<AffectedCertificateAssignmentResponse> AffectedActiveAssignments);

internal sealed record CrewCertificateHistoryItemResponse(
    Guid Id,
    Guid CertificateId,
    string EventType,
    Guid ActorUserId,
    DateTime OccurredAtUtc,
    string? Reason,
    Guid? PredecessorCertificateId);

internal sealed record GetCrewCertificateHistoryResponse(
    Guid CertificateId,
    IReadOnlyList<Guid> ChainCertificateIds,
    IReadOnlyList<CrewCertificateHistoryItemResponse> History);

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

internal sealed record CreateCrewAvailabilityRequestRequest
{
    public string? Type { get; init; }
    public DateOnly? StartDate { get; init; }
    public DateOnly? EndDate { get; init; }
    public string? Reason { get; init; }
    public string? Notes { get; init; }
}

internal sealed record CreateCrewAvailabilityRequestResponse(Guid Id);

internal sealed record RejectCrewAvailabilityRequestRequest
{
    public string? Reason { get; init; }
}

internal sealed record CancelCrewAvailabilityRequestRequest
{
    public string? Reason { get; init; }
}

internal sealed record CrewAvailabilityDecisionResponse(
    Guid Id,
    Guid CrewMemberId,
    string PreviousStatus,
    string NewStatus,
    Guid ActorUserId,
    string? Reason,
    DateTime OccurredAtUtc);

internal sealed record CrewAvailabilityListItemResponse(
    Guid Id,
    Guid CrewMemberId,
    string CrewMemberName,
    string Type,
    string Status,
    DateOnly StartDate,
    DateOnly EndDate,
    string Reason,
    string? Notes,
    Guid RequestedByUserId,
    DateTime RequestedAtUtc,
    Guid? ReviewedByUserId,
    DateTime? ReviewedAtUtc,
    string? DecisionReason);

internal sealed record GetCrewAvailabilityResponse(
    IReadOnlyList<CrewAvailabilityListItemResponse> Items,
    int PageNumber,
    int PageSize,
    int TotalCount);

internal sealed record CrewAvailabilityTransitionItemResponse(
    Guid Id,
    string EventType,
    string? Reason,
    DateTime OccurredAtUtc,
    string PreviousStatus,
    string NewStatus);

internal sealed record CrewMemberAvailabilityItemResponse(
    Guid Id,
    string Type,
    string Status,
    DateOnly StartDate,
    DateOnly EndDate,
    string Reason,
    string? Notes,
    DateTime RequestedAtUtc,
    DateTime? ReviewedAtUtc,
    string? DecisionReason,
    IReadOnlyList<CrewAvailabilityTransitionItemResponse> History);

internal sealed record GetCrewMemberAvailabilityResponse(IReadOnlyList<CrewMemberAvailabilityItemResponse> Items);

internal sealed record CrewAvailabilityConflictResponse(
    string RecordType,
    string Status,
    DateOnly StartDate,
    DateOnly EndDate,
    string? Detail);

internal sealed record GetCrewAvailabilityConflictsResponse(IReadOnlyList<CrewAvailabilityConflictResponse> Conflicts);

internal sealed record ManningPlanLineInputRequest
{
    public Guid? RankId { get; init; }
    public int? RequiredHeadcount { get; init; }
}

internal sealed record VesselManningPlanLineItemResponse(Guid Id, Guid RankId, string RankName, int RequiredHeadcount);

internal sealed record VesselManningPlanHistoryItemResponse(
    Guid Id,
    string EventType,
    string? Reason,
    Guid ActorUserId,
    DateTime OccurredAtUtc,
    DateOnly? PreviousEffectiveEndDate,
    DateOnly? NewEffectiveEndDate);

internal sealed record VesselManningPlanListItemResponse(
    Guid Id,
    Guid VesselId,
    DateOnly EffectiveStartDate,
    DateOnly EffectiveEndDate,
    int LineCount,
    Guid CreatedByUserId,
    DateTime CreatedAtUtc,
    Guid ConcurrencyToken);

internal sealed record VesselManningPlansPageResponse(
    IReadOnlyList<VesselManningPlanListItemResponse> Items, int PageNumber, int PageSize, int TotalCount);

internal sealed record VesselManningPlanDetailResponse(
    Guid Id,
    Guid VesselId,
    DateOnly EffectiveStartDate,
    DateOnly EffectiveEndDate,
    Guid CreatedByUserId,
    DateTime CreatedAtUtc,
    Guid ConcurrencyToken,
    IReadOnlyList<VesselManningPlanLineItemResponse> Lines,
    IReadOnlyList<VesselManningPlanHistoryItemResponse> History);

internal sealed record CreateVesselManningPlanRequest
{
    public Guid? VesselId { get; init; }
    public DateOnly? EffectiveStartDate { get; init; }
    public DateOnly? EffectiveEndDate { get; init; }
    public IReadOnlyList<ManningPlanLineInputRequest>? Lines { get; init; }
}

internal sealed record CreateVesselManningPlanResponse(
    Guid Id,
    Guid VesselId,
    DateOnly EffectiveStartDate,
    DateOnly EffectiveEndDate,
    Guid ConcurrencyToken,
    IReadOnlyList<VesselManningPlanLineItemResponse> Lines);

internal sealed record ReplaceVesselManningPlanLinesRequest
{
    public IReadOnlyList<ManningPlanLineInputRequest>? Lines { get; init; }
    public string? Reason { get; init; }
}

internal sealed record ReplaceVesselManningPlanLinesResponse(
    Guid Id, Guid ConcurrencyToken, IReadOnlyList<VesselManningPlanLineItemResponse> Lines);

internal sealed record EndVesselManningPlanRequest
{
    public DateOnly? NewEndDate { get; init; }
    public string? Reason { get; init; }
}

internal sealed record EndVesselManningPlanResponse(Guid Id, DateOnly EffectiveEndDate, Guid ConcurrencyToken);

internal sealed record CoverageIneligibleMemberResponse(Guid CrewMemberId, string Reason);

internal sealed record CoverageUnqualifiedMemberResponse(Guid CrewMemberId, IReadOnlyList<string> MissingTypes);

internal sealed record CoverageDayItemResponse(
    DateOnly Date,
    Guid RankId,
    string RankName,
    int? RequiredHeadcount,
    int ProjectedAssignedHeadcount,
    int EligibleHeadcount,
    int? Shortage,
    IReadOnlyList<CoverageIneligibleMemberResponse> IneligibleMembers,
    IReadOnlyList<Guid> AssignmentIds,
    int? QualifiedHeadcount,
    IReadOnlyList<CoverageUnqualifiedMemberResponse> UnqualifiedMembers);

internal sealed record CoverageShortageIntervalResponse(
    Guid RankId, string RankName, DateOnly StartDate, DateOnly EndDate, int RequiredHeadcount, int Shortage);

internal sealed record GetCrewManningCoverageResponse(
    Guid VesselId,
    DateOnly From,
    DateOnly To,
    IReadOnlyList<CoverageDayItemResponse> Days,
    IReadOnlyList<CoverageShortageIntervalResponse> ShortageIntervals);

internal sealed record QualificationRequirementInputRequest
{
    public string? RequiredType { get; init; }
}

internal sealed record CreateQualificationPolicyRequest
{
    public Guid? CrewPositionId { get; init; }
    public bool? Enabled { get; init; }
    public IReadOnlyList<QualificationRequirementInputRequest>? Requirements { get; init; }
}

internal sealed record CreateQualificationPolicyResponse(
    Guid Id, Guid CrewPositionId, bool Enabled, Guid ConcurrencyToken, IReadOnlyList<string> RequiredTypes);

internal sealed record UpdateQualificationPolicyRequest
{
    public bool? Enabled { get; init; }
    public IReadOnlyList<QualificationRequirementInputRequest>? Requirements { get; init; }
}

internal sealed record UpdateQualificationPolicyResponse(
    Guid Id, bool Enabled, Guid ConcurrencyToken, IReadOnlyList<string> RequiredTypes);

internal sealed record QualificationPolicyListItemResponse(
    Guid Id, Guid CrewPositionId, string RankName, bool Enabled, int RequiredTypeCount, Guid ConcurrencyToken);

internal sealed record GetQualificationPoliciesResponse(
    IReadOnlyList<QualificationPolicyListItemResponse> Items, int PageNumber, int PageSize, int TotalCount);

internal sealed record QualificationPolicyHistoryItemResponse(
    Guid Id,
    string EventType,
    Guid ActorUserId,
    DateTime OccurredAtUtc,
    bool? PreviousEnabled,
    bool? NewEnabled,
    IReadOnlyList<string>? PreviousRequiredTypes,
    IReadOnlyList<string>? NewRequiredTypes);

internal sealed record GetQualificationPolicyDetailResponse(
    Guid Id,
    Guid CrewPositionId,
    string RankName,
    bool Enabled,
    Guid ConcurrencyToken,
    IReadOnlyList<string> RequiredTypes,
    IReadOnlyList<QualificationPolicyHistoryItemResponse> History);

internal sealed record QualificationUncoveredRangeResponse(string RequiredType, DateOnly StartDate, DateOnly EndDate);

internal sealed record GetCrewQualificationEligibilityResponse(
    Guid CrewMemberId,
    Guid RankId,
    DateOnly From,
    DateOnly To,
    string Status,
    IReadOnlyList<string> MissingTypes,
    IReadOnlyList<QualificationUncoveredRangeResponse> UncoveredDates,
    IReadOnlyList<Guid> CertificateLinks);
