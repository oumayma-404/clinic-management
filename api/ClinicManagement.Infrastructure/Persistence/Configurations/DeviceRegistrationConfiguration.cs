using ClinicManagement.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ClinicManagement.Infrastructure.Persistence.Configurations;

public class DeviceRegistrationConfiguration : IEntityTypeConfiguration<DeviceRegistration>
{
    public void Configure(EntityTypeBuilder<DeviceRegistration> builder)
    {
        builder.ToTable("DeviceRegistrations");

        builder.HasKey(d => d.Id);

        builder.Property(d => d.Id)
            .ValueGeneratedNever();

        builder.Property(d => d.ClinicId)
            .IsRequired();

        builder.HasOne<Clinic>()
            .WithMany()
            .HasForeignKey(d => d.ClinicId)
            .OnDelete(DeleteBehavior.Cascade);

        // 255 to match every other User.Id column in the schema (the Auth0 sub / local|{guid}). No FK: a token
        // rebinding moves this value, and the row must survive the account being deleted so « why did this
        // device stop receiving? » is still answerable — the same reasoning AuditEntry's actor columns carry.
        builder.Property(d => d.UserId)
            .IsRequired()
            .HasMaxLength(255);

        builder.Property(d => d.Platform)
            .IsRequired()
            .HasConversion<int>();

        // FCM tokens run to ~200 characters and APNs' to 64 hex, but both are documented as variable-length and
        // opaque, so this is deliberately generous rather than measured against today's values.
        builder.Property(d => d.Token)
            .IsRequired()
            .HasMaxLength(512);

        builder.Property(d => d.ShellVersion)
            .HasMaxLength(50);

        builder.Property(d => d.LastSeenAt)
            .IsRequired();

        builder.Property(d => d.IsActive)
            .IsRequired();

        builder.Property(d => d.CreatedAt)
            .IsRequired();

        // ⚠️ UNIQUE, and it is what makes rebinding one deterministic write rather than a conflict (AC-41).
        // Not filtered on IsActive: a deactivated row still owns its token — a reinstall presents the same one
        // and must reactivate that row, and two rows for one physical device is the state this forbids.
        builder.HasIndex(d => d.Token)
            .IsUnique();

        // The fan-out's audience read: this clinic's active devices for a set of users.
        builder.HasIndex(d => new { d.ClinicId, d.UserId, d.IsActive });
    }
}
