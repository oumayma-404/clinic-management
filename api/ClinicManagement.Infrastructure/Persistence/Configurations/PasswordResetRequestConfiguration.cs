using ClinicManagement.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ClinicManagement.Infrastructure.Persistence.Configurations;

/// <summary>
/// ⚠️ <b>No <c>ClinicId</c> and therefore no query filter</b> — the two endpoints that read this table are
/// anonymous, so no tenant scope is ever established, and a filtered read under an <c>Unset</c> scope returns zero
/// rows with no error. <c>TenantScopeFilterTests</c> derives its clinic-owned set from the presence of that column,
/// so this table is outside it by construction rather than by exemption, exactly as
/// <see cref="ClinicSignupConfiguration"/> is.
///
/// <para>⚠️ <b>And no foreign key to <c>Users</c> either</b>, despite <see cref="PasswordResetRequest.UserId"/>
/// naming one. A cascade from that side would delete the audit of a reset along with the account, and a restrict
/// would make deactivating an account fail on a stale row nobody remembers. The <c>UserId</c> is resolved by the
/// handler on every read, so a row pointing at an account that no longer exists simply fails to complete — which
/// is the correct outcome and needs no constraint to produce.</para>
/// </summary>
public class PasswordResetRequestConfiguration : IEntityTypeConfiguration<PasswordResetRequest>
{
    public void Configure(EntityTypeBuilder<PasswordResetRequest> builder)
    {
        builder.ToTable("PasswordResetRequests");

        builder.HasKey(r => r.Id);

        builder.Property(r => r.Id)
            .ValueGeneratedNever();

        // `User.Id` is a string — an Auth0 sub, or `local|{guid}` — and its own column is 200 wide.
        builder.Property(r => r.UserId)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(r => r.Email)
            .IsRequired()
            .HasMaxLength(PasswordResetRequest.MaxEmailLength);

        // 64 hex characters — SHA-256. Fixed by the algorithm, so the length is a statement rather than a guess.
        builder.Property(r => r.TokenHash)
            .IsRequired()
            .HasMaxLength(64);

        builder.Property(r => r.ExpiresAtUtc)
            .IsRequired();

        builder.Property(r => r.CreatedAtUtc)
            .IsRequired();

        builder.Property(r => r.EmailSendAttempts)
            .IsRequired();

        // UNIQUE, and that is what makes « one row per account » an invariant the database holds rather than a race
        // the handler hopes to win. Two simultaneous requests for one account would otherwise leave two live
        // tokens, and the cooldown — which reads the single row — would then throttle neither of them.
        builder.HasIndex(r => r.UserId)
            .IsUnique();

        // The completion lookup's only index. Unique too: a SHA-256 collision here is not a thing that happens, and
        // two rows sharing one hash would make « whose password does this link replace? » ambiguous.
        builder.HasIndex(r => r.TokenHash)
            .IsUnique();

        // The opportunistic purge scans on these two together.
        builder.HasIndex(r => new { r.ConsumedAtUtc, r.ExpiresAtUtc });
    }
}
