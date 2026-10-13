namespace SampleMobile.Application.Abstractions;

/// <summary>
/// Commits the current scope's changes. Domain events queued on tracked aggregates are written
/// to the transactional outbox in the same commit, so no write path can persist state and lose
/// the events it raised.
/// </summary>
public interface IUnitOfWork {
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
