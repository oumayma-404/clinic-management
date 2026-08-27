using ClinicManagement.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ClinicManagement.Infrastructure.Persistence.Configurations;

/// <summary>
/// Mapping for a clinic user's single-use recovery codes (<c>hosted-security-hardening</c> FR-1.4).
///
/// <para>The <c>(UserId, IsUsed)</c> index serves the only question ever asked of this table — « does this
/// account have an unused code matching X? » — which runs on a sign-in and must not scan the account's whole
/// history of spent codes.</para>
///
/// <para>⚠️ <b>Cascade from <c>Users</c></b>: a deleted account's codes are meaningless, and they are the one
/// child here that could otherwise outlive the credential they belong to.</para>
/// </summary>
public class UserRecoveryCodeConfiguration : IEntityTypeConfiguration<UserRecoveryCode>
{
    public void Configure(EntityTypeBuilder<UserRecoveryCode> builder)
    {
        builder.ToTable("UserRecoveryCodes");

        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).ValueGeneratedNever();

        // `User` is keyed by string, unlike every other aggregate here.
        builder.Property(c => c.UserId).IsRequired().HasMaxLength(128);

        // 64 hex characters of SHA-256, fixed width by construction.
        builder.Property(c => c.CodeHash).IsRequired().HasMaxLength(64);
        builder.Property(c => c.IsUsed).IsRequired();
        builder.Property(c => c.CreatedAt).IsRequired();

        builder.HasOne(c => c.User)
            .WithMany(u => u.RecoveryCodes)
            .HasForeignKey(c => c.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(c => new { c.UserId, c.IsUsed })
            .HasDatabaseName("IX_UserRecoveryCodes_UserId_IsUsed");
    }
}
