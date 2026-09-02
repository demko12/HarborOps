namespace HarborOps.Api.Composition.Notifications;

public sealed class ExpiryAlertHostedService(IServiceScopeFactory scopes, ILogger<ExpiryAlertHostedService> logger, Microsoft.Extensions.Options.IOptions<ExpiryAlertOptions> options) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(Math.Clamp(options.Value.IntervalMinutes, 1, 1440)));
        do { await RunAsync(stoppingToken); } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
    private async Task RunAsync(CancellationToken cancellationToken)
    {
        try { using var scope = scopes.CreateScope(); await scope.ServiceProvider.GetRequiredService<ExpiryAlertGenerator>().GenerateAsync(cancellationToken); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception exception) { logger.LogError(exception, "Expiry alert generation failed."); }
    }
}
