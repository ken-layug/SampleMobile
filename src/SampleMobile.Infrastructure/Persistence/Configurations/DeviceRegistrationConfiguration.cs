using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SampleMobile.Domain.Entities;
using SampleMobile.Infrastructure.Identity;

namespace SampleMobile.Infrastructure.Persistence.Configurations;

internal sealed class DeviceRegistrationConfiguration : IEntityTypeConfiguration<DeviceRegistration> {
    public void Configure(EntityTypeBuilder<DeviceRegistration> builder) {
        builder.ToTable("device_registrations", AppDbContext.NotificationSchema);
        builder.HasKey(registration => registration.Id);
        builder.Property(registration => registration.Id).ValueGeneratedNever();
        builder.Ignore(registration => registration.DomainEvents);

        builder.Property(registration => registration.InstallationId).HasMaxLength(DeviceRegistration.MaxInstallationIdLength);
        builder.Property(registration => registration.ProviderId).HasMaxLength(DeviceRegistration.MaxProviderIdLength);
        builder.Property(registration => registration.Address).HasMaxLength(DeviceRegistration.MaxAddressLength);
        builder.Property(registration => registration.Platform).HasConversion<string>().HasMaxLength(20);
        builder.Property(registration => registration.DisabledReason).HasConversion<string>().HasMaxLength(40);

        // The idempotency key for registration: one row per installation per provider.
        builder.HasIndex(registration => new { registration.InstallationId, registration.ProviderId }).IsUnique();
        builder.HasIndex(registration => registration.UserId);
        builder.HasIndex(registration => new { registration.ProviderId, registration.Address });

        builder.HasOne<AppUser>()
            .WithMany()
            .HasForeignKey(registration => registration.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
