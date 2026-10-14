namespace SampleMobile.Mobile.Core.Api;

/// <summary>Why a backend call did not succeed. Expected failures are values, never exceptions.</summary>
public enum ApiFailureKind {
    None,
    /// <summary>401: missing, expired or invalid token (or wrong login credentials).</summary>
    Unauthorized,
    /// <summary>404: the resource doesn't exist or isn't the caller's.</summary>
    NotFound,
    /// <summary>400: validation failed; see <see cref="ApiResult.Errors"/>.</summary>
    Invalid,
    /// <summary>409: concurrent registration conflict.</summary>
    Conflict,
    /// <summary>5xx, 408 or 429: the backend is temporarily unable to answer.</summary>
    Server,
    /// <summary>The backend could not be reached (offline, DNS, TLS, timeout).</summary>
    Network,
    /// <summary>A success status with a body that doesn't match the contract.</summary>
    InvalidResponse,
    /// <summary>No backend URL has been configured yet.</summary>
    NotConfigured,
    /// <summary>Any other status code.</summary>
    Unexpected
}

/// <summary>Outcome of a backend call without a response body.</summary>
public class ApiResult {
    private static readonly IReadOnlyDictionary<string, string[]> NoErrors = new Dictionary<string, string[]>();

    protected ApiResult(ApiFailureKind failure, int? statusCode, string? message, IReadOnlyDictionary<string, string[]>? errors, string? traceId) {
        Failure = failure;
        StatusCode = statusCode;
        Message = message;
        Errors = errors ?? NoErrors;
        TraceId = traceId;
    }

    public ApiFailureKind Failure { get; }

    public int? StatusCode { get; }

    /// <summary>Problem <c>detail</c>/<c>title</c>, or a description of the transport failure.</summary>
    public string? Message { get; }

    /// <summary>Validation errors keyed by JSON property name (camelCase), as the backend reports them.</summary>
    public IReadOnlyDictionary<string, string[]> Errors { get; }

    /// <summary>Server trace id, worth quoting when reporting a 500.</summary>
    public string? TraceId { get; }

    public bool Succeeded => Failure == ApiFailureKind.None;

    /// <summary>True when retrying later may succeed without any change on the client.</summary>
    public bool IsTransient => Failure is ApiFailureKind.Network or ApiFailureKind.Server or ApiFailureKind.Conflict;

    public static ApiResult Success(int statusCode) => new(ApiFailureKind.None, statusCode, null, null, null);

    public static ApiResult Fail(ApiFailureKind failure, string? message, int? statusCode = null, IReadOnlyDictionary<string, string[]>? errors = null, string? traceId = null) =>
        new(failure, statusCode, message, errors, traceId);

    /// <summary>A one-line description for status labels.</summary>
    public string Describe() {
        if (Succeeded) {
            return StatusCode is { } code ? $"OK ({code})" : "OK";
        }

        var status = StatusCode is { } failedCode ? $" ({failedCode})" : string.Empty;
        var detail = Errors.Count > 0
            ? string.Join(" ", Errors.SelectMany(error => error.Value))
            : Message;
        return string.IsNullOrWhiteSpace(detail) ? $"{Failure}{status}" : $"{Failure}{status}: {detail}";
    }
}

/// <summary>Outcome of a backend call that returns <typeparamref name="T"/> on success.</summary>
public sealed class ApiResult<T> : ApiResult {
    private ApiResult(T? value, ApiFailureKind failure, int? statusCode, string? message, IReadOnlyDictionary<string, string[]>? errors, string? traceId)
        : base(failure, statusCode, message, errors, traceId) {
        Value = value;
    }

    /// <summary>The response body; non-null when <see cref="ApiResult.Succeeded"/>.</summary>
    public T? Value { get; }

    public static ApiResult<T> Success(T value, int statusCode) => new(value, ApiFailureKind.None, statusCode, null, null, null);

    public static new ApiResult<T> Fail(ApiFailureKind failure, string? message, int? statusCode = null, IReadOnlyDictionary<string, string[]>? errors = null, string? traceId = null) =>
        new(default, failure, statusCode, message, errors, traceId);

    /// <summary>Re-types a failure.</summary>
    public static ApiResult<T> From(ApiResult failure) =>
        new(default, failure.Failure, failure.StatusCode, failure.Message, failure.Errors, failure.TraceId);
}
