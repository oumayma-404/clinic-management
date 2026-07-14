using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ClinicManagement.Domain.Entities;

namespace ClinicManagement.Infrastructure.Persistence.Configurations;

public class NotificationReadConfiguration : IEntityTypeConfiguration<NotificationRead>
{
    public void Configure(EntityTypeBuilder<NotificationRead> builder)
    {
        builder.ToTable("NotificationReads");

        // Composite key: one read marker per (notification, user).
        builder.HasKey(r => new { r.NotificationId, r.UserId });

        builder.Property(r => r.UserId)
            .IsRequired()
            .HasMaxLength(255);

        builder.Property(r => r.ReadAt)
            .IsRequired();

        // FK to the notification; cascade so deleting a notification removes its read markers.
        builder.HasOne<StaffNotification>()
            .WithMany()
            .HasForeignKey(r => r.NotificationId)
            .OnDelete(DeleteBehavior.Cascade);

        // Read markers are always queried scoped by the current user.
        builder.HasIndex(r => r.UserId);
    }
}
