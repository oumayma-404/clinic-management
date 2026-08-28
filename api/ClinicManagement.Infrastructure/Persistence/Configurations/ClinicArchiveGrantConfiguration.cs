using ClinicManagement.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ClinicManagement.Infrastructure.Persistence.Configurations;

/// <summary>
/// The archive device grants' schema (<c>clinic-archive-auto-copy</c>), on
/// <see cref="ClinicRecoveryPointConfiguration"/>'s shape.
/// </summary>
public class ClinicArchiveGrantConfiguration : IEntityTypeConfiguration<ClinicArchiveGrant>
{
    public void Configure(EntityTypeBuilder<ClinicArchiveGrant> builder)
    {
        builder.ToTable("ClinicArchiveGrants");

        builder.HasKey(g => g.Id);
        builder.Property(g => g.Id).ValueGeneratedNever();

        builder.HasOne<Clinic>()
            .WithMany()
            .HasForeignKey(g => g.ClinicId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Property(g => g.Label).IsRequired().HasMaxLength(120);

        // 64 hex characters of SHA-256, fixed width.
        builder.Property(g => g.SecretHash).IsRequired().HasMaxLength(64);

        builder.Property(g => g.CreatedByUserId).IsRequired().HasMaxLength(128);
        builder.Property(g => g.CreatedAtUtc).IsRequired();
        builder.Property(g => g.LastUsedAtUtc);
        builder.Property(g => g.RevokedAtUtc);

        // Unique, so two cabinets cannot collide on one secret and the lookup below cannot return the wrong row.
        builder.HasIndex(g => g.SecretHash).IsUnique();

        // The list read: a cabinet's grants, newest first.
        builder.HasIndex(g => new { g.ClinicId, g.CreatedAtUtc });
    }
}
