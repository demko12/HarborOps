namespace HarborOps.Web.Api;

internal sealed class HealthApiClient(
    HttpClient httpClient,
    ILogger<HealthApiClient> logger)
{
    internal async Task<bool> IsAvailableAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var response = await httpClient.GetAsync(
                "health",
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                return true;
            }

            logger.LogWarning(
                "HarborOps API health check returned status code {StatusCode}.",
                (int)response.StatusCode);
        }
        catch (OperationCanceledException exception)
            when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(exception, "HarborOps API health check timed out.");
        }
        catch (HttpRequestException exception)
        {
            logger.LogWarning(exception, "HarborOps API health check failed.");
        }

        return false;
    }
}
