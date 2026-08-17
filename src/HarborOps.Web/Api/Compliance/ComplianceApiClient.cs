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
