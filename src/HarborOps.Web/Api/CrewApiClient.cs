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

internal sealed record CrewApiFailure(int StatusCode, string Title, string Message);
