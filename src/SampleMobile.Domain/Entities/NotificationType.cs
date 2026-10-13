using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

namespace SampleMobile.Domain.Entities;

/// <summary>
/// A notification category such as <c>demo.alert</c>: lower-case, dot-separated segments.
/// Clients use it to decide how to render or route a notification.
/// </summary>
public sealed partial record NotificationType {
    public const int MaxLength = 100;

    private NotificationType(string value) {
        Value = value;
    }

    public string Value { get; }

    /// <summary>Creates a type, throwing <see cref="ArgumentException"/> when the value is malformed.</summary>
    public static NotificationType Create(string value) {
        if (!TryCreate(value, out var type)) {
            throw new ArgumentException(
                $"'{value}' is not a valid notification type. Use lower-case dot-separated segments, e.g. 'demo.alert'.",
                nameof(value));
        }

        return type;
    }

    public static bool TryCreate(string? value, [NotNullWhen(true)] out NotificationType? type) {
        type = null;
        if (string.IsNullOrWhiteSpace(value)) {
            return false;
        }

        var normalized = value.Trim();
        if (normalized.Length > MaxLength || !Pattern().IsMatch(normalized)) {
            return false;
        }

        type = new NotificationType(normalized);
        return true;
    }

    public override string ToString() => Value;

    [GeneratedRegex("^[a-z0-9]+(?:[._-][a-z0-9]+)*$")]
    private static partial Regex Pattern();
}
