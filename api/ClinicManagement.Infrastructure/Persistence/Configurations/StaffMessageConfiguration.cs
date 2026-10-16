using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ClinicManagement.Domain.Entities;

namespace ClinicManagement.Infrastructure.Persistence.Configurations;

public class StaffMessageConfiguration : IEntityTypeConfiguration<StaffMessage>
{
    public void Configure(EntityTypeBuilder<StaffMessage> builder)
    {
        builder.ToTable("StaffMessages");

        builder.HasKey(m => m.Id);

        builder.Property(m => m.Id)
            .ValueGeneratedNever();

        builder.Property(m => m.ClinicId)
            .IsRequired();

        builder.HasOne<Clinic>()
            .WithMany()
            .HasForeignKey(m => m.ClinicId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Property(m => m.ConversationKey)
            .IsRequired()
            .HasMaxLength(StaffMessage.ConversationKeyMaxLength);

        builder.Property(m => m.SenderUserId)
            .IsRequired()
            .HasMaxLength(StaffMessage.UserIdMaxLength);

        builder.Property(m => m.Body)
            .HasMaxLength(StaffMessage.BodyMaxLength);

        builder.Property(m => m.QuickMessageKey)
            .HasMaxLength(StaffMessage.QuickMessageKeyMaxLength);

        builder.Property(m => m.QuickMessageLabel)
            .HasMaxLength(StaffMessage.QuickMessageLabelMaxLength);

        builder.Property(m => m.ClaimedByUserId)
            .HasMaxLength(StaffMessage.UserIdMaxLength);

        builder.Property(m => m.SentAt)
            .IsRequired();

        // A thread page, a sender's usage count (quick-message ordering) and « already announced? » for an RDV.
        builder.HasIndex(m => new { m.ClinicId, m.ConversationKey, m.SentAt });
        builder.HasIndex(m => new { m.ClinicId, m.SenderUserId, m.SentAt });
        builder.HasIndex(m => new { m.ClinicId, m.AppointmentId });
    }
}
