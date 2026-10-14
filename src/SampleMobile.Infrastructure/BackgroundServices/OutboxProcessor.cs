using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SampleMobile.Application.Abstractions.Events;
using SampleMobile.Domain.Services;
using SampleMobile.Infrastructure.Persistence;
using SampleMobile.Infrastructure.Persistence.Outbox;

namespace SampleMobile.Infrastructure.BackgroundServices;

public sealed class OutboxOptions {
    public const string SectionName = "Outbox";

    public bool Enabled { get; set; } = true;
    public int MaxAttempts { get; set; } = 10;
    public TimeSpan RetryBaseDelay { get; set; } = TimeSpan.FromSeconds(5);
    public TimeSpan RetryMaxDelay { get; set; } = TimeSpan.FromMinutes(5);
    public TimeSpan MaxIdleWait { get; set; } = TimeSpan.FromSeconds(30);
}

/// <summary>
/// Turns committed outbox rows into handler invocations. Each message is processed in its own
/// transaction that holds a <c>FOR UPDATE SKIP LOCKED</c> row lock: the handlers' writes and the
/// "processed" mark commit together, and no other processor can pick the same row meanwhile.
/// A crash before commit leaves the row pending, so handlers run at least once — and are idempotent.
/// </summary>
public sealed class OutboxProcessor(
    IServiceScopeFactory scopeFactory,
    WorkSignal signal,
    TimeProvider timeProvider,
    IOptions<OutboxOptions> options,
    ILogger<OutboxProcessor> logger) : BackgroundService {

    private readonly DeliveryRetryPolicy _retryPolicy =
        new(options.Value.MaxAttempts, options.Value.RetryBaseDelay, options.Value.RetryMaxDelay);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        if (!options.Value.Enabled) {
            logger.LogInformation("Outbox processor is disabled");
            return;
        }

        while (!stoppingToken.IsCancellationRequested) {
            try {
                if (await ProcessNextAsync(stoppingToken)) {
                    continue;
                }

                await signal.WaitAsync(WorkChannels.Outbox, await GetIdleWaitAsync(stoppingToken), stoppingToken);
            } catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) {
                break;
            } catch (Exception exception) {
                logger.LogError(exception, "Outbox processing loop failed; retrying shortly");
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }

    /// <summary>Processes one due message. Returns <c>false</c> when nothing was due.</summary>
    internal async Task<bool> ProcessNextAsync(CancellationToken cancellationToken) {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = timeProvider.GetUtcNow();
        var pending = nameof(OutboxMessageStatus.Pending);

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var messages = await db.OutboxMessages
            .FromSql($"""
                SELECT * FROM notification.outbox_messages
                WHERE status = {pending} AND next_attempt_at <= {now}
                ORDER BY created_at
                LIMIT 1
                FOR UPDATE SKIP LOCKED
                """)
            .ToListAsync(cancellationToken);

        if (messages is not [var message]) {
            await transaction.CommitAsync(cancellationToken);
            return false;
        }

        if (!OutboxEventSerializer.TryDeserialize(message, out var domainEvent)) {
            logger.LogError("Outbox message {MessageId} has unknown event type {EventType}; giving up", message.Id, message.EventType);
            message.RecordFailure($"Unknown event type '{message.EventType}'.", retryAt: null, now);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return true;
        }

        try {
            var dispatcher = scope.ServiceProvider.GetRequiredService<IDomainEventDispatcher>();
            await dispatcher.DispatchAsync(domainEvent, cancellationToken);

            message.MarkProcessed(timeProvider.GetUtcNow());
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            logger.LogInformation("Processed outbox message {MessageId} ({EventType})", message.Id, message.EventType);
        } catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested) {
            await transaction.RollbackAsync(CancellationToken.None);
            // The failed scope's change tracker holds the rolled-back writes; record the failure from a clean one.
            await RecordFailureAsync(message.Id, message.AttemptCount, exception, cancellationToken);
        }

        return true;
    }

    private async Task RecordFailureAsync(Guid messageId, int previousAttempts, Exception exception, CancellationToken cancellationToken) {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var message = await db.OutboxMessages.FirstOrDefaultAsync(candidate => candidate.Id == messageId, cancellationToken);
        if (message is null) {
            return;
        }

        var now = timeProvider.GetUtcNow();
        var delay = _retryPolicy.GetDelayAfter(previousAttempts + 1);
        message.RecordFailure($"{exception.GetType().Name}: {exception.Message}", delay is { } backoff ? now + backoff : null, now);
        await db.SaveChangesAsync(cancellationToken);

        logger.LogWarning(
            exception,
            "Outbox message {MessageId} ({EventType}) failed on attempt {Attempt}; {Next}",
            message.Id, message.EventType, message.AttemptCount,
            delay is null ? "giving up" : $"retrying at {message.NextAttemptAt:O}");
    }

    private async Task<TimeSpan> GetIdleWaitAsync(CancellationToken cancellationToken) {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var nextDue = await db.OutboxMessages
            .Where(message => message.Status == OutboxMessageStatus.Pending)
            .MinAsync(message => (DateTimeOffset?)message.NextAttemptAt, cancellationToken);

        return WaitPolicy.Until(nextDue, timeProvider.GetUtcNow(), options.Value.MaxIdleWait);
    }
}
