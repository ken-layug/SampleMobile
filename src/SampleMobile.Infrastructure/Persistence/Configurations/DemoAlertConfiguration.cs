using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SampleMobile.Domain.Entities;
using SampleMobile.Infrastructure.Identity;

namespace SampleMobile.Infrastructure.Persistence.Configurations;

internal sealed class DemoAlertConfiguration : IEntityTypeConfiguration<DemoAlert> {
    public void Configure(EntityTypeBuilder<DemoAlert> builder) {
        builder.ToTable("demo_alerts", AppDbContext.DemoSchema);
        builder.HasKey(alert => alert.Id);
        builder.Property(alert => alert.Id).ValueGeneratedNever();
        builder.Ignore(alert => alert.DomainEvents);

        builder.Property(alert => alert.Severity).HasConversion<string>().HasMaxLength(20);
        builder.Property(alert => alert.Message).HasMaxLength(DemoAlert.MaxMessageLength);

        builder.HasOne<AppUser>()
            .WithMany()
            .HasForeignKey(alert => alert.RecipientUserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
