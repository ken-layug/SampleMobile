namespace SampleMobile.Application.Common;

/// <summary>
/// A unique constraint rejected a write. Infrastructure translates the database error into this
/// so use cases never inspect provider-specific SQL error codes.
/// </summary>
public sealed class UniqueConstraintViolationException(string? constraintName, Exception innerException)
    : Exception($"A unique constraint was violated ({constraintName ?? "unknown"}).", innerException) {
    public string? ConstraintName { get; } = constraintName;
}

/// <summary>
/// The row changed since it was loaded (optimistic concurrency). For delivery jobs this means
/// another worker took over the job after this worker's lease expired.
/// </summary>
public sealed class ConcurrencyConflictException(string message, Exception innerException)
    : Exception(message, innerException);
