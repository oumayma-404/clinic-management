using ClinicManagement.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ClinicManagement.Infrastructure.Persistence.Configurations;

public class PushDeliveryConfiguration : IEntityTypeConfiguration<PushDelivery>
{
    public void Configure(EntityTypeBuilder<PushDelivery> builder)
    {
        builder.ToTable("PushDeliveries");

        builder.HasKey(p => p.Id);

        builder.Property(p => p.Id)
            .ValueGeneratedNever();

        builder.Property(p => p.ClinicId)
            .IsRequired();

        builder.HasOne<Clinic>()
            .WithMany()
            .HasForeignKey(p => p.ClinicId)
            .OnDelete(DeleteBehavior.Cascade);

        // Cascade: a device that is gone has no queued sends worth keeping — unlike a deactivated one, whose row
        // survives and whose queued rows the dispatcher still evaluates and fails with a stated reason.
        builder.HasOne<DeviceRegistration>()
            .WithMany()
            .HasForeignKey(p => p.DeviceRegistrationId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Property(p => p.RecipientUserId)
            .IsRequired()
            .HasMaxLength(255);

        builder.Property(p => p.Category)
            .IsRequired()
            .HasConversion<int>();

        builder.Property(p => p.Label)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(p => p.AppointmentId);

        builder.Property(p => p.SendNotBefore)
            .IsRequired();

        builder.Property(p => p.Status)
            .IsRequired()
            .HasConversion<int>();

        builder.Property(p => p.AttemptCount)
            .IsRequired();

        builder.Property(p => p.FailureReason)
            .HasMaxLength(500);

        builder.Property(p => p.CreatedAt)
            .IsRequired();

        // The dispatch scan's own predicate AND its ordering, so both come off one index — the omission
        // IX_Notifications_Status_ScheduledFor had to be added later to fix on the reminder queue.
        builder.HasIndex(p => new { p.Status, p.SendNotBefore });
    }
}
