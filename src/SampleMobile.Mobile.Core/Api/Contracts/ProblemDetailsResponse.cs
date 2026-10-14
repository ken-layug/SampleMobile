namespace SampleMobile.Mobile.Core.Api.Contracts;

/// <summary>RFC 9457 problem details as the backend writes them, including validation <see cref="Errors"/>.</summary>
public sealed record ProblemDetailsResponse(
    string? Type,
    string? Title,
    int? Status,
    string? Detail,
    string? TraceId,
    IReadOnlyDictionary<string, string[]>? Errors);
