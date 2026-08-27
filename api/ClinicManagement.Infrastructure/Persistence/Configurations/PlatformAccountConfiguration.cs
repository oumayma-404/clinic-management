using ClinicManagement.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ClinicManagement.Infrastructure.Persistence.Configurations;

/// <summary>
/// Mapping for the console identity population.
///
/// <para>⚠️ <b>The unique index is on <c>Email</c> and is <i>not</i> filtered</b>, unlike <c>User</c>'s — that one
/// is partial on <c>PasswordHash IS NOT NULL</c> because a Cloud user legitimately has none. Every console
/// account has a password by construction (<see cref="PlatformAccount.Create"/> refuses without one), so the
/// unconditional index is the honest statement, and it is what makes « one account per address » a fact the
/// database holds rather than one a handler wins a race for.</para>
/// </summary>
public class PlatformAccountConfiguration : IEntityTypeConfiguration<PlatformAccount>
{
    public void Configure(EntityTypeBuilder<PlatformAccount> builder)
    {
        builder.ToTable("PlatformAccounts");

        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).ValueGeneratedNever();

        builder.Property(a => a.Email).IsRequired().HasMaxLength(256);
        builder.Property(a => a.FullName).IsRequired().HasMaxLength(200);
        builder.Property(a => a.PasswordHash).IsRequired().HasMaxLength(512);

        // Opaque ciphertext, not a base32 secret — Data Protection payloads are several hundred characters.
        builder.Property(a => a.ProtectedTotpSecret).HasMaxLength(1024);

        builder.Property(a => a.IsActive).IsRequired();
        builder.Property(a => a.MustChangePassword).IsRequired();
        builder.Property(a => a.TokenVersion).IsRequired();
        builder.Property(a => a.FailedLoginAttempts).IsRequired();
        builder.Property(a => a.CreatedAt).IsRequired();

        builder.HasIndex(a => a.Email)
            .IsUnique()
            .HasDatabaseName("IX_PlatformAccounts_Email");

        builder.HasMany(a => a.RecoveryCodes)
            .WithOne(c => c.Account)
            .HasForeignKey(c => c.PlatformAccountId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
