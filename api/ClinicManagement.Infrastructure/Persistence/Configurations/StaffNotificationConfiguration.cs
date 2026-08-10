using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ClinicManagement.Domain.Entities;

namespace ClinicManagement.Infrastructure.Persistence.Configurations;

public class StaffNotificationConfiguration : IEntityTypeConfiguration<StaffNotification>
{
    public void Configure(EntityTypeBuilder<StaffNotification> builder)
    {
        builder.ToTable("StaffNotifications");

        builder.HasKey(n => n.Id);

        builder.Property(n => n.Id)
            .ValueGeneratedNever();

        builder.Property(n => n.ClinicId)
            .IsRequired();

        builder.HasOne<Clinic>()
            .WithMany()
            .HasForeignKey(n => n.ClinicId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Property(n => n.Category)
            .IsRequired()
            .HasConversion<int>();

        builder.Property(n => n.Title)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(n => n.Message)
            .IsRequired()
            .HasColumnType("text");

        builder.Property(n => n.EffectiveFeedTime)
            .IsRequired();

        builder.Property(n => n.ActorUserId)
            .HasMaxLength(255);

        builder.Property(n => n.TargetUserId)
            .HasMaxLength(255);

        builder.Property(n => n.TargetKind)
            .IsRequired()
            .HasConversion<int>();

        builder.Property(n => n.AppointmentId);
        builder.Property(n => n.StockItemId);

        // The (clinic, threshold) dedupe key for the four expiry warnings (clinic-subscription FR-5). A column
        // rather than a French message prefix; no writer until Part E.
        builder.Property(n => n.SubscriptionThresholdDays);

        builder.Property(n => n.CreatedAt)
            .IsRequired();

        // The ordered/capped read path filters by clinic and orders by EffectiveFeedTime desc.
        builder.HasIndex(n => new { n.ClinicId, n.EffectiveFeedTime });

        // Reminder suppression/move looks a notification up by its appointment.
        builder.HasIndex(n => n.AppointmentId);
    }
}
