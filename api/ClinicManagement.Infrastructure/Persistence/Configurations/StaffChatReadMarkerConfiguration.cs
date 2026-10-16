using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ClinicManagement.Domain.Entities;

namespace ClinicManagement.Infrastructure.Persistence.Configurations;

public class StaffChatReadMarkerConfiguration : IEntityTypeConfiguration<StaffChatReadMarker>
{
    public void Configure(EntityTypeBuilder<StaffChatReadMarker> builder)
    {
        builder.ToTable("StaffChatReadMarkers");

        builder.HasKey(r => new { r.ClinicId, r.UserId, r.ConversationKey });

        builder.Property(r => r.UserId)
            .IsRequired()
            .HasMaxLength(StaffMessage.UserIdMaxLength);

        builder.Property(r => r.ConversationKey)
            .IsRequired()
            .HasMaxLength(StaffMessage.ConversationKeyMaxLength);

        builder.Property(r => r.LastReadAt)
            .IsRequired();

        builder.HasOne<Clinic>()
            .WithMany()
            .HasForeignKey(r => r.ClinicId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
