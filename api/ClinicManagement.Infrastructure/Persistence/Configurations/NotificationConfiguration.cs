using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;

namespace ClinicManagement.Infrastructure.Persistence.Configurations;

public class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> builder)
    {
        builder.ToTable("Notifications");

        builder.HasKey(n => n.Id);

        builder.Property(n => n.Id)
            .ValueGeneratedNever();

        builder.Property(n => n.ClinicId);

        builder.Property(n => n.AppointmentId);

        builder.Property(n => n.PatientId);

        builder.Property(n => n.Type)
            .IsRequired()
            .HasConversion<int>();

        builder.Property(n => n.Subject)
            .IsRequired()
            .HasMaxLength(500);

        builder.Property(n => n.Message)
            .IsRequired()
            .HasColumnType("text");

        builder.Property(n => n.Status)
            .IsRequired()
            .HasConversion<int>();

        builder.Property(n => n.ScheduledFor)
            .IsRequired();

        builder.Property(n => n.SentAt);

        builder.Property(n => n.ErrorMessage)
            .HasMaxLength(1000);

        // The machine-readable half of ErrorMessage, so the un-park review can interrogate the reason instead of
        // the French sentence (clinic-subscription FR-8). Nullable; no writer until Part G.
        builder.Property(n => n.BlockedReason)
            .HasConversion<int>();

        builder.Property(n => n.RetryCount)
            .IsRequired()
            .HasDefaultValue(0);

        builder.Property(n => n.CreatedAt)
            .IsRequired();

        builder.HasOne(n => n.Appointment)
            .WithMany()
            .HasForeignKey(n => n.AppointmentId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(n => n.Patient)
            .WithMany()
            .HasForeignKey(n => n.PatientId)
            .OnDelete(DeleteBehavior.SetNull);

        // AC-P4.30 — the minutely dispatcher's only query is
        //   WHERE Status = Pending AND ScheduledFor <= now  ORDER BY ScheduledFor
        // and it ran unindexed against a table that has never been purged, so it degraded forever. Status
        // leads because it is the selective predicate (Pending is a shrinking minority once rows start
        // sending), and ScheduledFor second serves both the range and the ORDER BY from the same index.
        // Closest precedent is a status+due-instant outbox index — the same
        // outbox shape, deliberately mirrored rather than re-invented.
        builder.HasIndex(n => new { n.Status, n.ScheduledFor });
    }
}



