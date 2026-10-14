using System.ComponentModel.DataAnnotations;
using SampleMobile.Domain.Entities;

namespace SampleMobile.Web.Components.Dashboard;

/// <summary>Form model for composing a notification by hand.</summary>
public sealed class ComposeNotificationModel {
    [Required(ErrorMessage = "Choose a recipient.")]
    public Guid? RecipientUserId { get; set; }

    [Required]
    public string Type { get; set; } = string.Empty;

    [Required]
    [MaxLength(Notification.MaxTitleLength)]
    public string Title { get; set; } = string.Empty;

    [Required]
    [MaxLength(Notification.MaxBodyLength)]
    public string Body { get; set; } = string.Empty;

    [MaxLength(Notification.MaxResourceIdLength)]
    public string? ResourceId { get; set; }

    /// <summary>One <c>key=value</c> pair per line.</summary>
    public string? MetadataText { get; set; }

    public HashSet<Guid> SelectedTargetIds { get; } = [];

    public IReadOnlyDictionary<string, string> ParseMetadata() {
        var metadata = new Dictionary<string, string>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(MetadataText)) {
            return metadata;
        }

        foreach (var line in MetadataText.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)) {
            var separator = line.IndexOf('=');
            if (separator <= 0) {
                continue;
            }

            metadata[line[..separator].Trim()] = line[(separator + 1)..].Trim();
        }

        return metadata;
    }
}

/// <summary>Form model for raising the sample domain event.</summary>
public sealed class RaiseDemoAlertModel {
    [Required(ErrorMessage = "Choose a recipient.")]
    public Guid? RecipientUserId { get; set; }

    public DemoAlertSeverity Severity { get; set; } = DemoAlertSeverity.Warning;

    [Required]
    [MaxLength(DemoAlert.MaxMessageLength)]
    public string Message { get; set; } = "Disk usage on the demo server crossed 90%.";
}
