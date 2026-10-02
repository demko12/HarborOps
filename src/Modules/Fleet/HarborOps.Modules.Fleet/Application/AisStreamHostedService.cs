using System.Net.WebSockets;
using System.Text.Json;
using HarborOps.Modules.Fleet.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HarborOps.Modules.Fleet.Application;

internal sealed class AisStreamHostedService(
    IServiceScopeFactory scopeFactory,
    IOptions<AisStreamOptions> options,
    AisTrackingState state,
    TimeProvider timeProvider,
    ILogger<AisStreamHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        if (!settings.Enabled) return;
        if (string.IsNullOrWhiteSpace(settings.ApiKey)) { state.SetStatus("NotConfigured"); return; }
        if (settings.BoundingBoxes.Length == 0 || settings.BoundingBoxes.Any(box =>
            box.Length != 2 || box.Any(point => point.Length != 2 || !double.IsFinite(point[0])
                || !double.IsFinite(point[1]) || point[0] is < -90 or > 90 || point[1] is < -180 or > 180)))
        {
            state.SetStatus("NotConfigured");
            logger.LogWarning("AISStream bounding boxes must contain two valid latitude/longitude corners.");
            return;
        }

        var processor = new AisMessageProcessor(state, timeProvider);
        var failures = 0;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RefreshFleetAsync(processor, stoppingToken);
                var mmsis = processor.TrackedMmsis;
                if (mmsis.Length is 0 or > 200)
                {
                    state.SetStatus(mmsis.Length == 0 ? "AwaitingMmsi" : "FleetLimitExceeded");
                    await Task.Delay(TimeSpan.FromMinutes(1), timeProvider, stoppingToken);
                    continue;
                }
                using var socket = new ClientWebSocket();
                socket.Options.DangerousDeflateOptions = new WebSocketDeflateOptions();
                socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);
                socket.Options.KeepAliveTimeout = TimeSpan.FromSeconds(20);
                state.SetStatus("Connecting");
                using var connectionTimeout = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                connectionTimeout.CancelAfter(TimeSpan.FromSeconds(30));
                await socket.ConnectAsync(new Uri("wss://stream.aisstream.io/v0/stream"), connectionTimeout.Token);
                await SubscribeAsync(socket, settings, mmsis, stoppingToken);

                var nextDirectoryRefresh = timeProvider.GetUtcNow().AddMinutes(1);
                var buffer = new byte[16 * 1024];
                while (socket.State == WebSocketState.Open)
                {
                    using var payload = new MemoryStream();
                    ValueWebSocketReceiveResult result;
                    do
                    {
                        result = await socket.ReceiveAsync(buffer.AsMemory(), stoppingToken);
                        if (result.MessageType == WebSocketMessageType.Close) throw new WebSocketException("AIS stream closed.");
                        if (payload.Length + result.Count > 256 * 1024) throw new WebSocketException("AIS message exceeds the size limit.");
                        payload.Write(buffer, 0, result.Count);
                    } while (!result.EndOfMessage);
                    try
                    {
                        if (processor.Process(payload.ToArray()) is { } observation)
                        {
                            await using var scope = scopeFactory.CreateAsyncScope();
                            await scope.ServiceProvider.GetRequiredService<RecordAisPositionService>()
                                .RecordAsync(observation, stoppingToken);
                        }
                    }
                    catch (JsonException) { logger.LogDebug("Ignored malformed AIS JSON."); }
                    if (state.Status.ConnectionState == "Connected") failures = 0;
                    if (timeProvider.GetUtcNow() >= nextDirectoryRefresh)
                    {
                        await RefreshFleetAsync(processor, stoppingToken);
                        var refreshedMmsis = processor.TrackedMmsis;
                        if (refreshedMmsis.Length is 0 or > 200) break;
                        if (!mmsis.SequenceEqual(refreshedMmsis))
                        {
                            state.SetStatus("Connecting");
                            await SubscribeAsync(socket, settings, refreshedMmsis, stoppingToken);
                            mmsis = refreshedMmsis;
                        }
                        nextDirectoryRefresh = timeProvider.GetUtcNow().AddMinutes(1);
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception exception)
            {
                state.SetStatus("Reconnecting");
                // Do not log provider payloads, subscription data or the key.
                logger.LogWarning("AISStream connection interrupted ({FailureType}); reconnecting.", exception.GetType().Name);
            }

            var seconds = Math.Min(60, Math.Pow(2, Math.Min(++failures, 6))) + Random.Shared.NextDouble();
            try { await Task.Delay(TimeSpan.FromSeconds(seconds), timeProvider, stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
        state.SetStatus("Disconnected");
    }

    private static async Task SubscribeAsync(ClientWebSocket socket, AisStreamOptions settings, string[] mmsis, CancellationToken cancellationToken)
    {
        var subscription = JsonSerializer.SerializeToUtf8Bytes(new
        {
            APIKey = settings.ApiKey,
            settings.BoundingBoxes,
            FiltersShipMMSI = mmsis,
            FilterMessageTypes = new[] { "PositionReport", "StandardClassBPositionReport", "ExtendedClassBPositionReport" },
        });
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(2));
        await socket.SendAsync(subscription.AsMemory(), WebSocketMessageType.Text, true, timeout.Token);
    }

    private async Task RefreshFleetAsync(AisMessageProcessor processor, CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<DbContext>();
        var vessels = await database.Set<Vessel>().AsNoTracking()
            .Select(vessel => new { vessel.ImoNumber, vessel.Mmsi }).ToListAsync(cancellationToken);
        processor.SetFleet(vessels.Select(vessel => (vessel.ImoNumber, vessel.Mmsi)));
    }
}
