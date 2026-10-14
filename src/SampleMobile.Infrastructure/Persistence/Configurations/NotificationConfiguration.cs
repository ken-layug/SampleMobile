using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SampleMobile.Domain.Entities;
using SampleMobile.Infrastructure.Identity;

namespace SampleMobile.Infrastructure.Persistence.Configurations;

internal sealed class NotificationConfiguration : IEntityTypeConfiguration<Notification> {
    public void Configure(EntityTypeBuilder<Notification> builder) {
        builder.ToTable("notifications", AppDbContext.NotificationSchema);
        builder.HasKey(notification => notification.Id);
        builder.Property(notification => notification.Id).ValueGeneratedNever();
        builder.Ignore(notification => notification.DomainEvents);

        builder.Property(notification => notification.Type)
            .HasConversion(type => type.Value, value => NotificationType.Create(value))
            .HasMaxLength(NotificationType.MaxLength);
        builder.Property(notification => notification.Title).HasMaxLength(Notification.MaxTitleLength);
        builder.Property(notification => notification.Body).HasMaxLength(Notification.MaxBodyLength);
        builder.Property(notification => notification.ResourceId).HasMaxLength(Notification.MaxResourceIdLength);
        builder.Property(notification => notification.DeduplicationKey).HasMaxLength(Notification.MaxDeduplicationKeyLength);

        builder.Property(notification => notification.Metadata)
            .HasColumnType("jsonb")
            .HasConversion(
                metadata => MetadataJson.Serialize(metadata),
                json => MetadataJson.Deserialize(json),
                new ValueComparer<IReadOnlyDictionary<string, string>>(
                    (left, right) => MetadataJson.AreEqual(left, right),
                    metadata => MetadataJson.GetHashCode(metadata),
                    metadata => new Dictionary<string, string>(metadata)));

        // Unique so a re-processed outbox event can never create a second notification.
        // PostgreSQL allows any number of NULLs, so hand-composed notifications are unaffected.
        builder.HasIndex(notification => notification.DeduplicationKey).IsUnique();
        builder.HasIndex(notification => notification.CreatedAt);

        builder.HasMany(notification => notification.Recipients)
            .WithOne()
            .HasForeignKey(recipient => recipient.NotificationId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(notification => notification.Recipients).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class NotificationRecipientConfiguration : IEntityTypeConfiguration<NotificationRecipient> {
    public void Configure(EntityTypeBuilder<NotificationRecipient> builder) {
        builder.ToTable("recipients", AppDbContext.NotificationSchema);
        builder.HasKey(recipient => recipient.Id);
        builder.Property(recipient => recipient.Id).ValueGeneratedNever();
        builder.Ignore(recipient => recipient.IsRead);

        builder.HasIndex(recipient => new { recipient.NotificationId, recipient.UserId }).IsUnique();
        builder.HasIndex(recipient => recipient.UserId);

        builder.HasOne<AppUser>()
            .WithMany()
            .HasForeignKey(recipient => recipient.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

/// <summary>JSON (de)serialization for the metadata column; kept out of expression trees for readability.</summary>
internal static class MetadataJson {
    public static string Serialize(IReadOnlyDictionary<string, string> metadata) => JsonSerializer.Serialize(metadata);

    public static IReadOnlyDictionary<string, string> Deserialize(string json) =>
        JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new Dictionary<string, string>();

    public static bool AreEqual(IReadOnlyDictionary<string, string>? left, IReadOnlyDictionary<string, string>? right) {
        if (ReferenceEquals(left, right)) {
            return true;
        }

        if (left is null || right is null || left.Count != right.Count) {
            return false;
        }

        return left.All(pair => right.TryGetValue(pair.Key, out var value) && value == pair.Value);
    }

    public static int GetHashCode(IReadOnlyDictionary<string, string> metadata) =>
        metadata.OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Aggregate(0, (hash, pair) => HashCode.Combine(hash, pair.Key, pair.Value));
}
