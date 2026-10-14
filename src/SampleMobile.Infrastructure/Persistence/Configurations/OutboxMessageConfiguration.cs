using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SampleMobile.Infrastructure.Persistence.Outbox;

namespace SampleMobile.Infrastructure.Persistence.Configurations;

internal sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage> {
    public void Configure(EntityTypeBuilder<OutboxMessage> builder) {
        builder.ToTable("outbox_messages", AppDbContext.NotificationSchema);
        builder.HasKey(message => message.Id);
        builder.Property(message => message.Id).ValueGeneratedNever();

        builder.Property(message => message.EventType).HasMaxLength(OutboxMessage.MaxEventTypeLength);
        builder.Property(message => message.Payload).HasColumnType("jsonb");
        builder.Property(message => message.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(message => message.LastError).HasMaxLength(OutboxMessage.MaxErrorLength);

        builder.HasIndex(message => new { message.Status, message.NextAttemptAt });
    }
}
