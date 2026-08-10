using ClinicManagement.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ClinicManagement.Infrastructure.Persistence.Configurations;

/// <summary>
/// Mapping for the single-use recovery codes.
///
/// <para>The <c>(PlatformAccountId, IsUsed)</c> index serves the only question ever asked of this table — « does
/// this account have an unused code matching X? » — which runs on a sign-in and must not scan the account's
/// whole history of spent codes.</para>
/// </summary>
public class PlatformRecoveryCodeConfiguration : IEntityTypeConfiguration<PlatformRecoveryCode>
{
    public void Configure(EntityTypeBuilder<PlatformRecoveryCode> builder)
    {
        builder.ToTable("PlatformRecoveryCodes");

        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).ValueGeneratedNever();

        // 64 hex characters of SHA-256, fixed width by construction.
        builder.Property(c => c.CodeHash).IsRequired().HasMaxLength(64);
        builder.Property(c => c.IsUsed).IsRequired();
        builder.Property(c => c.CreatedAt).IsRequired();

        builder.HasIndex(c => new { c.PlatformAccountId, c.IsUsed })
            .HasDatabaseName("IX_PlatformRecoveryCodes_PlatformAccountId_IsUsed");
    }
}
