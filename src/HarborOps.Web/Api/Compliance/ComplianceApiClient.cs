using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace HarborOps.Web.Api.Compliance;

internal sealed class ComplianceApiClient(HttpClient httpClient)
{
    private const string BasePath = "api/compliance";
    private static readonly IReadOnlyDictionary<string, string[]> NoErrors =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);

    internal Task<ComplianceApiResult<ComplianceOverview>> GetOverviewAsync(Guid vesselId,
        CancellationToken cancellationToken) => SendAsync<ComplianceOverview>(
            () => httpClient.GetAsync($"{BasePath}/vessels/{vesselId}/overview",
                HttpCompletionOption.ResponseHeadersRead, cancellationToken),
            HttpStatusCode.OK, "Unable to load compliance overview", cancellationToken);

    internal Task<ComplianceApiResult<RegisterComplianceCertificateResult>> RegisterCertificateAsync(
        RegisterComplianceCertificateRequest request, CancellationToken cancellationToken) =>
        SendAsync<RegisterComplianceCertificateResult>(
            () => httpClient.PostAsJsonAsync($"{BasePath}/certificates", request, cancellationToken),
            HttpStatusCode.Created, "Unable to register certificate", cancellationToken);

    internal Task<ComplianceApiResult<ComplianceCertificateDetail>> GetCertificateAsync(
        Guid certificateId, CancellationToken cancellationToken) =>
        SendAsync<ComplianceCertificateDetail>(
            () => httpClient.GetAsync($"{BasePath}/certificates/{certificateId}",
                HttpCompletionOption.ResponseHeadersRead, cancellationToken),
            HttpStatusCode.OK, "Unable to load certificate", cancellationToken);

    internal Task<ComplianceApiResult<RenewComplianceCertificateResult>> RenewCertificateAsync(
        Guid certificateId, RenewComplianceCertificateRequest request, CancellationToken cancellationToken) =>
        SendAsync<RenewComplianceCertificateResult>(
            () => httpClient.PostAsJsonAsync($"{BasePath}/certificates/{certificateId}/renewals", request, cancellationToken),
            HttpStatusCode.Created, "Unable to renew certificate", cancellationToken);

    internal Task<ComplianceApiResult<RevokeComplianceCertificateResult>> RevokeCertificateAsync(
        Guid certificateId, RevokeComplianceCertificateRequest request, CancellationToken cancellationToken) =>
        SendAsync<RevokeComplianceCertificateResult>(
            () => httpClient.PostAsJsonAsync($"{BasePath}/certificates/{certificateId}/revoke", request, cancellationToken),
            HttpStatusCode.OK, "Unable to revoke certificate", cancellationToken);

    internal Task<ComplianceApiResult<RegisterPscInspectionResult>> RegisterPscInspectionAsync(
        RegisterPscInspectionRequest request, CancellationToken cancellationToken) =>
        SendAsync<RegisterPscInspectionResult>(
            () => httpClient.PostAsJsonAsync($"{BasePath}/psc-inspections", request, cancellationToken),
            HttpStatusCode.Created, "Unable to register PSC inspection", cancellationToken);

    internal Task<ComplianceApiResult<GetPscInspectionDetailResponse>> GetPscInspectionAsync(int inspectionId, CancellationToken cancellationToken) =>
        SendAsync<GetPscInspectionDetailResponse>(() => httpClient.GetAsync($"{BasePath}/psc-inspections/{inspectionId}", HttpCompletionOption.ResponseHeadersRead, cancellationToken), HttpStatusCode.OK, "Unable to load PSC inspection", cancellationToken);

    internal Task<ComplianceApiResult<GetPscDeficienciesResponse>> GetPscDeficienciesAsync(int inspectionId, CancellationToken cancellationToken) =>
        SendAsync<GetPscDeficienciesResponse>(() => httpClient.GetAsync($"{BasePath}/psc-inspections/{inspectionId}/deficiencies", HttpCompletionOption.ResponseHeadersRead, cancellationToken), HttpStatusCode.OK, "Unable to load PSC deficiencies", cancellationToken);

    internal Task<ComplianceApiResult<RegisterPscDeficiencyResult>> RegisterPscDeficiencyAsync(int inspectionId, RegisterPscDeficiencyRequest request, CancellationToken cancellationToken) =>
        SendAsync<RegisterPscDeficiencyResult>(() => httpClient.PostAsJsonAsync($"{BasePath}/psc-inspections/{inspectionId}/deficiencies", request, cancellationToken), HttpStatusCode.Created, "Unable to register PSC deficiency", cancellationToken);

    internal Task<ComplianceApiResult<object>> DetainPscInspectionAsync(int inspectionId, DetainPscInspectionRequest request, CancellationToken cancellationToken) =>
        SendNoContentAsync(() => httpClient.PostAsJsonAsync($"{BasePath}/psc-inspections/{inspectionId}/detain", request, cancellationToken), "Unable to record detention", cancellationToken);

    internal Task<ComplianceApiResult<object>> ReleasePscInspectionAsync(int inspectionId, ReleasePscInspectionRequest request, CancellationToken cancellationToken) =>
        SendNoContentAsync(() => httpClient.PostAsJsonAsync($"{BasePath}/psc-inspections/{inspectionId}/release", request, cancellationToken), "Unable to record release", cancellationToken);

    internal Task<ComplianceApiResult<RegisterNonConformityResult>> RegisterNonConformityAsync(
        RegisterNonConformityRequest request, CancellationToken cancellationToken) =>
        SendAsync<RegisterNonConformityResult>(
            () => httpClient.PostAsJsonAsync($"{BasePath}/non-conformities", request, cancellationToken),
            HttpStatusCode.Created, "Unable to register non-conformity", cancellationToken);

    internal Task<ComplianceApiResult<RegisterSurveyAuditResult>> RegisterSurveyAsync(RegisterSurveyAuditRequest request, CancellationToken cancellationToken) =>
        SendAsync<RegisterSurveyAuditResult>(() => httpClient.PostAsJsonAsync($"{BasePath}/surveys", request, cancellationToken), HttpStatusCode.Created, "Unable to register survey", cancellationToken);

    internal Task<ComplianceApiResult<SurveyAuditList>> GetSurveysAsync(string query, CancellationToken cancellationToken) =>
        SendAsync<SurveyAuditList>(() => httpClient.GetAsync($"{BasePath}/surveys?{query}", HttpCompletionOption.ResponseHeadersRead, cancellationToken), HttpStatusCode.OK, "Unable to load surveys", cancellationToken);

    internal Task<ComplianceApiResult<SurveyAuditDetails>> GetSurveyAsync(int surveyId, CancellationToken cancellationToken) =>
        SendAsync<SurveyAuditDetails>(() => httpClient.GetAsync($"{BasePath}/surveys/{surveyId}", HttpCompletionOption.ResponseHeadersRead, cancellationToken), HttpStatusCode.OK, "Unable to load survey details", cancellationToken);

    internal Task<ComplianceApiResult<RegisterInsurancePolicyResult>> RegisterInsurancePolicyAsync(
        RegisterInsurancePolicyRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<RegisterInsurancePolicyResult>(
            () => httpClient.PostAsJsonAsync($"{BasePath}/insurance-policies", request, cancellationToken),
            HttpStatusCode.Created,
            "Unable to register insurance policy",
            cancellationToken);

    internal Task<ComplianceApiResult<InsurancePoliciesPage>> GetInsurancePoliciesAsync(
        string query,
        CancellationToken cancellationToken) =>
        SendAsync<InsurancePoliciesPage>(
            () => httpClient.GetAsync($"{BasePath}/insurance-policies?{query}", HttpCompletionOption.ResponseHeadersRead, cancellationToken),
            HttpStatusCode.OK,
            "Unable to load insurance policies",
            cancellationToken);

    internal Task<ComplianceApiResult<GetNonConformitiesResponse>> GetNonConformitiesAsync(
        GetNonConformitiesRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<GetNonConformitiesResponse>(
            () => httpClient.GetAsync(
                $"{BasePath}/non-conformities?{BuildNonConformitiesQuery(request)}",
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken),
            HttpStatusCode.OK,
            "Unable to load non-conformities",
            cancellationToken);

    internal Task<ComplianceApiResult<NonConformitiesSummary>> GetNonConformitiesSummaryAsync(CancellationToken cancellationToken) =>
        SendAsync<NonConformitiesSummary>(
            () => httpClient.GetAsync(
                $"{BasePath}/non-conformities/summary",
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken),
            HttpStatusCode.OK,
            "Unable to load the non-conformity summary",
            cancellationToken);

    internal Task<ComplianceApiResult<GetNonConformityDetailResponse>> GetNonConformityDetailAsync(
        int nonConformityId,
        CancellationToken cancellationToken) =>
        SendAsync<GetNonConformityDetailResponse>(
            () => httpClient.GetAsync(
                $"{BasePath}/non-conformities/{nonConformityId}",
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken),
            HttpStatusCode.OK,
            "Unable to load non-conformity details",
            cancellationToken);

    internal Task<ComplianceApiResult<RecordCorrectiveActionResult>> RecordCorrectiveActionAsync(
        int nonConformityId,
        RecordCorrectiveActionRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<RecordCorrectiveActionResult>(
            () => httpClient.PostAsJsonAsync(
                $"{BasePath}/non-conformities/{nonConformityId}/corrective-actions", request, cancellationToken),
            HttpStatusCode.Created,
            "Unable to record corrective action",
            cancellationToken);

    internal Task<ComplianceApiResult<CloseNonConformityResult>> CloseNonConformityAsync(
        int nonConformityId,
        CloseNonConformityRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<CloseNonConformityResult>(
            () => httpClient.PostAsJsonAsync(
                $"{BasePath}/non-conformities/{nonConformityId}/close", request, cancellationToken),
            HttpStatusCode.OK,
            "Unable to close non-conformity",
            cancellationToken);

    internal Task<ComplianceApiResult<GetAuditPlansResponse>> GetAuditPlansAsync(
        GetAuditPlansRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<GetAuditPlansResponse>(
            () => httpClient.GetAsync(
                $"{BasePath}/audit-plans?{BuildAuditPlansQuery(request)}",
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken),
            HttpStatusCode.OK,
            "Unable to load audit plans",
            cancellationToken);

    internal Task<ComplianceApiResult<GetAuditPlanDetailResponse>> GetAuditPlanDetailAsync(
        Guid auditPlanId,
        CancellationToken cancellationToken) =>
        SendAsync<GetAuditPlanDetailResponse>(
            () => httpClient.GetAsync(
                $"{BasePath}/audit-plans/{auditPlanId}",
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken),
            HttpStatusCode.OK,
            "Unable to load audit plan details",
            cancellationToken);

    internal Task<ComplianceApiResult<RegisterAuditPlanResponse>> RegisterAuditPlanAsync(
        RegisterAuditPlanRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<RegisterAuditPlanResponse>(
            () => httpClient.PostAsJsonAsync($"{BasePath}/audit-plans", request, cancellationToken),
            HttpStatusCode.Created,
            "Unable to schedule audit plan",
            cancellationToken);

    internal Task<ComplianceApiResult<RescheduleAuditPlanResponse>> RescheduleAuditPlanAsync(
        Guid auditPlanId,
        RescheduleAuditPlanRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<RescheduleAuditPlanResponse>(
            () => httpClient.PutAsJsonAsync($"{BasePath}/audit-plans/{auditPlanId}/schedule", request, cancellationToken),
            HttpStatusCode.OK,
            "Unable to reschedule audit plan",
            cancellationToken);

    internal Task<ComplianceApiResult<AssignAuditPlanInspectorResponse>> AssignAuditPlanInspectorAsync(
        Guid auditPlanId,
        AssignAuditPlanInspectorRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<AssignAuditPlanInspectorResponse>(
            () => httpClient.PutAsJsonAsync($"{BasePath}/audit-plans/{auditPlanId}/assignment", request, cancellationToken),
            HttpStatusCode.OK,
            "Unable to assign inspector",
            cancellationToken);

    internal Task<ComplianceApiResult<StartAuditPlanResponse>> StartAuditPlanAsync(
        Guid auditPlanId,
        CancellationToken cancellationToken) =>
        SendAsync<StartAuditPlanResponse>(
            () => httpClient.PostAsync($"{BasePath}/audit-plans/{auditPlanId}/start", content: null, cancellationToken),
            HttpStatusCode.OK,
            "Unable to start audit plan",
            cancellationToken);

    internal Task<ComplianceApiResult<CompleteAuditPlanResponse>> CompleteAuditPlanAsync(
        Guid auditPlanId,
        CompleteAuditPlanRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<CompleteAuditPlanResponse>(
            () => httpClient.PostAsJsonAsync($"{BasePath}/audit-plans/{auditPlanId}/complete", request, cancellationToken),
            HttpStatusCode.OK,
            "Unable to complete audit plan",
            cancellationToken);

    internal Task<ComplianceApiResult<CancelAuditPlanResponse>> CancelAuditPlanAsync(
        Guid auditPlanId,
        CancelAuditPlanRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<CancelAuditPlanResponse>(
            () => httpClient.PostAsJsonAsync($"{BasePath}/audit-plans/{auditPlanId}/cancel", request, cancellationToken),
            HttpStatusCode.OK,
            "Unable to cancel audit plan",
            cancellationToken);

    private static string BuildAuditPlansQuery(GetAuditPlansRequest request)
    {
        var parameters = new List<string>
        {
            $"pageNumber={request.PageNumber}",
            $"pageSize={request.PageSize}",
        };

        if (request.VesselId is not null)
        {
            parameters.Add($"vesselId={request.VesselId}");
        }

        if (!string.IsNullOrWhiteSpace(request.Type))
        {
            parameters.Add($"type={Uri.EscapeDataString(request.Type)}");
        }

        if (!string.IsNullOrWhiteSpace(request.Status))
        {
            parameters.Add($"status={Uri.EscapeDataString(request.Status)}");
        }

        if (request.From is { } from)
        {
            parameters.Add($"from={from:yyyy-MM-dd}");
        }

        if (request.To is { } to)
        {
            parameters.Add($"to={to:yyyy-MM-dd}");
        }

        return string.Join("&", parameters);
    }

    private static string BuildNonConformitiesQuery(GetNonConformitiesRequest request)
    {
        var parameters = new List<string>
        {
            $"pageNumber={request.PageNumber}",
            $"pageSize={request.PageSize}",
        };

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            parameters.Add($"search={Uri.EscapeDataString(request.Search)}");
        }

        if (request.Severity is not null)
        {
            parameters.Add($"severity={Uri.EscapeDataString(request.Severity)}");
        }

        if (request.ClosureStatus is not null)
        {
            parameters.Add($"closureStatus={Uri.EscapeDataString(request.ClosureStatus)}");
        }

        if (request.Overdue is not null)
        {
            parameters.Add($"overdue={(request.Overdue.Value ? "true" : "false")}");
        }

        return string.Join("&", parameters);
    }

    private static async Task<ComplianceApiResult<T>> SendAsync<T>(Func<Task<HttpResponseMessage>> send,
        HttpStatusCode successStatus, string defaultTitle, CancellationToken cancellationToken)
        where T : class
    {
        try
        {
            using var response = await send();
            if (response.StatusCode != successStatus)
            {
                return new(null, await ReadFailureAsync(response, defaultTitle, cancellationToken));
            }

            return new(await response.Content.ReadFromJsonAsync<T>(cancellationToken), null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (HttpRequestException) { return new(null, Unavailable()); }
        catch (TaskCanceledException) { return new(null, Unavailable()); }
    }

    private static async Task<ComplianceApiResult<object>> SendNoContentAsync(Func<Task<HttpResponseMessage>> send, string defaultTitle, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await send();
            return response.StatusCode == HttpStatusCode.NoContent
                ? new(new object(), null)
                : new(null, await ReadFailureAsync(response, defaultTitle, cancellationToken));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (HttpRequestException) { return new(null, Unavailable()); }
        catch (TaskCanceledException) { return new(null, Unavailable()); }
    }

    private static async Task<ComplianceApiFailure> ReadFailureAsync(HttpResponseMessage response,
        string defaultTitle, CancellationToken cancellationToken)
    {
        ApiProblemDetails? problem = null;
        try { problem = await response.Content.ReadFromJsonAsync<ApiProblemDetails>(cancellationToken); }
        catch (JsonException) { }
        return new((int)response.StatusCode, problem?.Title ?? defaultTitle,
            problem?.Detail ?? "Please review the details and try again.", problem?.Errors ?? NoErrors);
    }

    private static ComplianceApiFailure Unavailable() => new(0, "HarborOps API is unavailable",
        "The compliance service cannot be reached right now.", NoErrors);
}
