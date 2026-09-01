using ClinicManagement.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ClinicManagement.Infrastructure.Persistence.Configurations;

/// <summary>
/// Mapping for one device's chain of refresh credentials (<c>hosted-security-hardening</c> FR-1.6).
///
/// <para>⚠️ <b><c>CurrentCredentialHash</c> is UNIQUE</b>, and that is the lookup: a presented credential
/// resolves to at most one family. It is what makes « which device is this? » a single indexed read on the
/// hottest authenticated path in the product.</para>
///
/// <para>⚠️ <b><c>PreviousCredentialHash</c> is indexed but NOT unique</b>, deliberately. Rotation copies the
/// current hash into it, so for one instant the same value legitimately sits in both columns of the same row —
/// and a unique index across the pair would refuse the rotation itself.</para>
///
/// <para>⚠️ <b>Cascade from <c>Users</c></b>: a deleted account's sessions cannot outlive it.</para>
/// </summary>
public class SessionFamilyConfiguration : IEntityTypeConfiguration<SessionFamily>
{
    public void Configure(EntityTypeBuilder<SessionFamily> builder)
    {
        builder.ToTable("SessionFamilies");

        builder.HasKey(f => f.Id);
        builder.Property(f => f.Id).ValueGeneratedNever();

        builder.Property(f => f.UserId).IsRequired().HasMaxLength(128);

        // 64 hex characters of SHA-256, fixed width by construction.
        builder.Property(f => f.CurrentCredentialHash).IsRequired().HasMaxLength(64);
        builder.Property(f => f.PreviousCredentialHash).HasMaxLength(64);

        builder.Property(f => f.DeviceLabel).HasMaxLength(200);
        builder.Property(f => f.EndedReason).HasMaxLength(200);

        // Additive with a `false` default, so every session already in flight on the deploy that introduces it
        // keeps the ordinary 12-hour lifetime rather than silently becoming a month-long one.
        builder.Property(f => f.IsTrusted).IsRequired().HasDefaultValue(false);

        builder.Property(f => f.CreatedAt).IsRequired();
        builder.Property(f => f.LastRotatedAt).IsRequired();
        builder.Property(f => f.ExpiresAtUtc).IsRequired();

        builder.HasOne(f => f.User)
            .WithMany()
            .HasForeignKey(f => f.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(f => f.CurrentCredentialHash)
            .IsUnique()
            .HasDatabaseName("IX_SessionFamilies_CurrentCredentialHash");

        builder.HasIndex(f => f.PreviousCredentialHash)
            .HasDatabaseName("IX_SessionFamilies_PreviousCredentialHash");

        // The purge's own predicate, and « this account's live devices » for the notification.
        builder.HasIndex(f => new { f.UserId, f.ExpiresAtUtc })
            .HasDatabaseName("IX_SessionFamilies_UserId_ExpiresAtUtc");
    }
}
