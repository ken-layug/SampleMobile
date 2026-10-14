using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SampleMobile.Domain.Entities;
using SampleMobile.Infrastructure.Identity;

namespace SampleMobile.Infrastructure.Persistence.Configurations;

internal sealed class DeliveryJobConfiguration : IEntityTypeConfiguration<DeliveryJob> {
    /// <summary>Shadow property mapped to PostgreSQL's <c>xmin</c> system column for optimistic concurrency.</summary>
    public const string RowVersion = "RowVersion";

    public void Configure(EntityTypeBuilder<DeliveryJob> builder) {
        builder.ToTable("delivery_jobs", AppDbContext.NotificationSchema);
        builder.HasKey(job => job.Id);
        builder.Property(job => job.Id).ValueGeneratedNever();
        builder.Ignore(job => job.DomainEvents);
        builder.Ignore(job => job.IsTerminal);
        builder.Ignore(job => job.CanRequeue);
        builder.Ignore(job => job.CurrentAttempt);

        builder.Property(job => job.ProviderId).HasMaxLength(DeviceRegistration.MaxProviderIdLength);
        builder.Property(job => job.Status).HasConversion<string>().HasMaxLength(30);
        builder.Property(job => job.LastOutcome).HasConversion<string>().HasMaxLength(30);
        builder.Property(job => job.LastErrorCode).HasMaxLength(DeliveryAttempt.MaxErrorCodeLength);
        builder.Property(job => job.LastErrorMessage).HasMaxLength(DeliveryAttempt.MaxErrorMessageLength);
        builder.Property(job => job.ProviderMessageId).HasMaxLength(DeliveryAttempt.MaxProviderMessageIdLength);

        // A worker whose lease expired must not overwrite the result of the worker that took over.
        builder.Property<uint>(RowVersion).IsRowVersion().HasColumnName("xmin");

        // One job per notification per device: re-planning the same notification can't double-send.
        builder.HasIndex(job => new { job.NotificationId, job.DeviceRegistrationId }).IsUnique();
        builder.HasIndex(job => new { job.Status, job.NextAttemptAt });

        builder.HasOne<Notification>()
            .WithMany()
            .HasForeignKey(job => job.NotificationId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<DeviceRegistration>()
            .WithMany()
            .HasForeignKey(job => job.DeviceRegistrationId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<AppUser>()
            .WithMany()
            .HasForeignKey(job => job.RecipientUserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(job => job.Attempts)
            .WithOne()
            .HasForeignKey(attempt => attempt.DeliveryJobId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(job => job.Attempts).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class DeliveryAttemptConfiguration : IEntityTypeConfiguration<DeliveryAttempt> {
    public void Configure(EntityTypeBuilder<DeliveryAttempt> builder) {
        builder.ToTable("delivery_attempts", AppDbContext.NotificationSchema);
        builder.HasKey(attempt => attempt.Id);
        builder.Property(attempt => attempt.Id).ValueGeneratedNever();

        builder.Property(attempt => attempt.ProviderId).HasMaxLength(DeviceRegistration.MaxProviderIdLength);
        builder.Property(attempt => attempt.Status).HasConversion<string>().HasMaxLength(30);
        builder.Property(attempt => attempt.Outcome).HasConversion<string>().HasMaxLength(30);
        builder.Property(attempt => attempt.ProviderMessageId).HasMaxLength(DeliveryAttempt.MaxProviderMessageIdLength);
        builder.Property(attempt => attempt.ErrorCode).HasMaxLength(DeliveryAttempt.MaxErrorCodeLength);
        builder.Property(attempt => attempt.ErrorMessage).HasMaxLength(DeliveryAttempt.MaxErrorMessageLength);

        builder.HasIndex(attempt => new { attempt.DeliveryJobId, attempt.AttemptNumber }).IsUnique();
    }
}
