using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using SampleMobile.Domain.Entities;
using SampleMobile.Infrastructure.Identity;
using SampleMobile.Infrastructure.Persistence.Outbox;

namespace SampleMobile.Infrastructure.Persistence;

/// <summary>
/// One context for Identity, notification and demo data, so a domain write and its outbox row
/// always share a transaction. Tables are snake_case and split across PostgreSQL schemas.
/// </summary>
public sealed class AppDbContext(DbContextOptions<AppDbContext> options)
    : IdentityDbContext<AppUser, IdentityRole<Guid>, Guid>(options) {
    public const string IdentitySchema = "identity";
    public const string NotificationSchema = "notification";
    public const string DemoSchema = "demo";

    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<NotificationRecipient> NotificationRecipients => Set<NotificationRecipient>();
    public DbSet<DeviceRegistration> DeviceRegistrations => Set<DeviceRegistration>();
    public DbSet<DeliveryJob> DeliveryJobs => Set<DeliveryJob>();
    public DbSet<DeliveryAttempt> DeliveryAttempts => Set<DeliveryAttempt>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
    public DbSet<DemoAlert> DemoAlerts => Set<DemoAlert>();

    protected override void OnModelCreating(ModelBuilder builder) {
        base.OnModelCreating(builder);

        builder.Entity<AppUser>(user => {
            user.ToTable("users", IdentitySchema);
            user.Property(u => u.DisplayName).HasMaxLength(200);
        });
        builder.Entity<IdentityRole<Guid>>().ToTable("roles", IdentitySchema);
        builder.Entity<IdentityUserRole<Guid>>().ToTable("user_roles", IdentitySchema);
        builder.Entity<IdentityUserClaim<Guid>>().ToTable("user_claims", IdentitySchema);
        builder.Entity<IdentityUserLogin<Guid>>().ToTable("user_logins", IdentitySchema);
        builder.Entity<IdentityUserToken<Guid>>().ToTable("user_tokens", IdentitySchema);
        builder.Entity<IdentityRoleClaim<Guid>>().ToTable("role_claims", IdentitySchema);

        builder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }
}
