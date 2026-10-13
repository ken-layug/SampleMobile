namespace SampleMobile.Application.Common;

public enum OperationStatus {
    Success,
    Unauthorized,
    Invalid,
    NotFound,
    Conflict
}

public sealed record OperationError(string Code, string Description);

/// <summary>
/// Outcome of a use case. Expected business failures ("not found", "invalid input") are returned,
/// not thrown; the Web layer maps the status to an HTTP response in one place.
/// </summary>
public class OperationResult {
    protected OperationResult(OperationStatus status, IReadOnlyList<OperationError>? errors = null) {
        Status = status;
        Errors = errors ?? [];
    }

    public OperationStatus Status { get; }
    public IReadOnlyList<OperationError> Errors { get; }

    public bool Succeeded => Status == OperationStatus.Success;

    public static OperationResult Success() => new(OperationStatus.Success);
    public static OperationResult Unauthorized(string description) =>
        new(OperationStatus.Unauthorized, [new OperationError("unauthorized", description)]);
    public static OperationResult Invalid(string code, string description) =>
        new(OperationStatus.Invalid, [new OperationError(code, description)]);
    public static OperationResult Invalid(IReadOnlyList<OperationError> errors) =>
        new(OperationStatus.Invalid, errors);
    public static OperationResult NotFound(string description) =>
        new(OperationStatus.NotFound, [new OperationError("not_found", description)]);
    public static OperationResult Conflict(string description) =>
        new(OperationStatus.Conflict, [new OperationError("conflict", description)]);
}

/// <summary>An <see cref="OperationResult"/> carrying a value on success.</summary>
public sealed class OperationResult<T> : OperationResult {
    private OperationResult(OperationStatus status, T? value, IReadOnlyList<OperationError>? errors = null)
        : base(status, errors) {
        Value = value;
    }

    public T? Value { get; }

    public static OperationResult<T> Success(T value) => new(OperationStatus.Success, value);
    public static new OperationResult<T> Unauthorized(string description) =>
        new(OperationStatus.Unauthorized, default, [new OperationError("unauthorized", description)]);
    public static new OperationResult<T> Invalid(string code, string description) =>
        new(OperationStatus.Invalid, default, [new OperationError(code, description)]);
    public static new OperationResult<T> Invalid(IReadOnlyList<OperationError> errors) =>
        new(OperationStatus.Invalid, default, errors);
    public static new OperationResult<T> NotFound(string description) =>
        new(OperationStatus.NotFound, default, [new OperationError("not_found", description)]);
    public static new OperationResult<T> Conflict(string description) =>
        new(OperationStatus.Conflict, default, [new OperationError("conflict", description)]);

    /// <summary>Re-types a failure so it can be returned from a handler with a different result type.</summary>
    public static OperationResult<T> FromFailure(OperationResult failure) {
        if (failure.Succeeded) {
            throw new ArgumentException("Only failed results can be converted.", nameof(failure));
        }

        return new OperationResult<T>(failure.Status, default, failure.Errors);
    }
}
