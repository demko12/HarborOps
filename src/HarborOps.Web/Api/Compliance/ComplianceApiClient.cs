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

    internal Task<ComplianceApiResult<RegisterPscInspectionResult>> RegisterPscInspectionAsync(
        RegisterPscInspectionRequest request, CancellationToken cancellationToken) =>
        SendAsync<RegisterPscInspectionResult>(
            () => httpClient.PostAsJsonAsync($"{BasePath}/psc-inspections", request, cancellationToken),
            HttpStatusCode.Created, "Unable to register PSC inspection", cancellationToken);

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
