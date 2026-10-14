using Microsoft.EntityFrameworkCore;
using Npgsql;
using SampleMobile.Application.Abstractions;
using SampleMobile.Application.Common;
using SampleMobile.Domain.Primitives;
using SampleMobile.Infrastructure.Persistence.Outbox;

namespace SampleMobile.Infrastructure.Persistence;

/// <summary>
/// Saves the scope's pending changes and, in the same transaction, one outbox row per queued domain event.
///
/// The ordering is load-bearing. Events are <b>peeked</b> before the save, not dequeued: if the save
/// throws, they stay queued on the aggregates (and the outbox rows are detached), so a retried save
/// records them exactly once. They are dequeued only after the commit succeeded. Nothing is dispatched
/// in-process: the outbox processor reads the rows back after commit, so an event can't be lost to a
/// crash between "saved" and "handled".
/// </summary>
public sealed class UnitOfWork(AppDbContext db, TimeProvider timeProvider) : IUnitOfWork {
    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) {
        var aggregates = db.ChangeTracker.Entries<IHasDomainEvents>()
            .Where(entry => entry.Entity.DomainEvents.Count > 0)
            .Select(entry => entry.Entity)
            .ToList();

        var now = timeProvider.GetUtcNow();
        var outboxMessages = aggregates
            .SelectMany(aggregate => aggregate.DomainEvents)
            .Select(domainEvent => OutboxEventSerializer.ToOutboxMessage(domainEvent, now))
            .ToList();
        db.OutboxMessages.AddRange(outboxMessages);

        int written;
        try {
            written = await db.SaveChangesAsync(cancellationToken);
        } catch (DbUpdateConcurrencyException exception) {
            Detach(outboxMessages);
            throw new ConcurrencyConflictException("The data was changed by someone else after it was loaded.", exception);
        } catch (DbUpdateException exception) when (exception.InnerException is PostgresException {
            SqlState: PostgresErrorCodes.UniqueViolation
        } postgresException) {
            Detach(outboxMessages);
            throw new UniqueConstraintViolationException(postgresException.ConstraintName, exception);
        } catch {
            Detach(outboxMessages);
            throw;
        }

        foreach (var aggregate in aggregates) {
            aggregate.DequeueDomainEvents();
        }

        return written;
    }

    private void Detach(IEnumerable<OutboxMessage> outboxMessages) {
        foreach (var message in outboxMessages) {
            db.Entry(message).State = EntityState.Detached;
        }
    }
}
