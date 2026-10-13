using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using SampleMobile.Application.Common;

namespace SampleMobile.Infrastructure.BackgroundServices;

/// <summary>
/// Holds one dedicated connection that <c>LISTEN</c>s on the work channels and turns PostgreSQL
/// notifications into in-process <see cref="WorkSignal"/> wake-ups. This is what lets a worker in
/// one process react immediately to jobs committed by another. If the connection drops, it
/// reconnects with backoff and wakes everyone once to catch up on anything missed.
/// </summary>
public sealed class PostgresWorkListener(
    NpgsqlDataSource dataSource,
    WorkSignal signal,
    IOptions<DeliveryOptions> deliveryOptions,
    IOptions<OutboxOptions> outboxOptions,
    ILogger<PostgresWorkListener> logger) : BackgroundService {

    private static readonly TimeSpan MaxReconnectDelay = TimeSpan.FromSeconds(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        if (!deliveryOptions.Value.WorkerEnabled && !outboxOptions.Value.Enabled) {
            return;
        }

        var reconnectDelay = TimeSpan.FromSeconds(1);
        while (!stoppingToken.IsCancellationRequested) {
            try {
                await using var connection = await dataSource.OpenConnectionAsync(stoppingToken);
                connection.Notification += (_, notification) => signal.Notify(notification.Channel);

                await using (var listen = connection.CreateCommand()) {
                    listen.CommandText = string.Join(' ', WorkChannels.All.Select(channel => $"LISTEN {channel};"));
                    await listen.ExecuteNonQueryAsync(stoppingToken);
                }

                logger.LogInformation("Listening for work notifications on {Channels}", string.Join(", ", WorkChannels.All));
                signal.NotifyAll();
                reconnectDelay = TimeSpan.FromSeconds(1);

                while (!stoppingToken.IsCancellationRequested) {
                    await connection.WaitAsync(stoppingToken);
                }
            } catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) {
                break;
            } catch (Exception exception) {
                logger.LogWarning(
                    exception,
                    "Work notification listener lost its connection; reconnecting in {Delay}. Processors fall back to timed wake-ups meanwhile",
                    reconnectDelay);
                await Task.Delay(reconnectDelay, stoppingToken);
                reconnectDelay = reconnectDelay * 2 < MaxReconnectDelay ? reconnectDelay * 2 : MaxReconnectDelay;
            }
        }
    }
}
