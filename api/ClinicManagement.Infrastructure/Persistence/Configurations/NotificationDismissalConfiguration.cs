using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ClinicManagement.Domain.Entities;

namespace ClinicManagement.Infrastructure.Persistence.Configurations;

/// <summary>
/// <see cref="NotificationReadConfiguration"/>'s twin, shape for shape — the two markers answer different
/// questions about the same row and are stored the same way.
/// </summary>
public class NotificationDismissalConfiguration : IEntityTypeConfiguration<NotificationDismissal>
{
    public void Configure(EntityTypeBuilder<NotificationDismissal> builder)
    {
        builder.ToTable("NotificationDismissals");

        // Composite key: one dismissal marker per (notification, user).
        builder.HasKey(d => new { d.NotificationId, d.UserId });

        builder.Property(d => d.UserId)
            .IsRequired()
            .HasMaxLength(255);

        builder.Property(d => d.DismissedAt)
            .IsRequired();

        // FK to the notification; cascade so deleting a notification removes its dismissal markers. That
        // matters here more than for reads: `CancelPostVisitReviewAsync` really does delete rows when a fiche
        // is saved, and an orphaned marker would keep a *reused* id invisible.
        builder.HasOne<StaffNotification>()
            .WithMany()
            .HasForeignKey(d => d.NotificationId)
            .OnDelete(DeleteBehavior.Cascade);

        // Dismissal markers are always queried scoped by the current user.
        builder.HasIndex(d => d.UserId);
    }
}
