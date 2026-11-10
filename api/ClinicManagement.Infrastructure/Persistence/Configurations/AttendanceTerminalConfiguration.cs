using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ClinicManagement.Domain.Entities;

namespace ClinicManagement.Infrastructure.Persistence.Configurations;

public class AttendanceTerminalConfiguration : IEntityTypeConfiguration<AttendanceTerminal>
{
    public void Configure(EntityTypeBuilder<AttendanceTerminal> builder)
    {
        builder.ToTable("AttendanceTerminals");

        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).ValueGeneratedNever();

        builder.Property(t => t.ClinicId).IsRequired();
        builder.HasOne<Clinic>()
            .WithMany()
            .HasForeignKey(t => t.ClinicId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Property(t => t.Name).IsRequired().HasMaxLength(AttendanceTerminal.MaxNameLength);
        builder.Property(t => t.SecretHash).HasMaxLength(64);
        builder.Property(t => t.PendingSessionFamilyId);
        builder.Property(t => t.DeviceDescription).HasMaxLength(260);
        builder.Property(t => t.CreatedByUserId).IsRequired().HasMaxLength(255);
        builder.Property(t => t.CreatedAt).IsRequired();
        builder.Property(t => t.LastUsedAt);
        builder.Property(t => t.RevokedAt);
        builder.Property(t => t.ActivatedAtUtc);

        builder.Ignore(t => t.IsActive);
        builder.Ignore(t => t.IsPending);
        builder.Ignore(t => t.IsLive);

        builder.HasIndex(t => t.SecretHash).IsUnique();
        builder.HasIndex(t => t.ClinicId);
        builder.HasIndex(t => t.PendingSessionFamilyId).HasFilter("\"PendingSessionFamilyId\" IS NOT NULL");
    }
}
