using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ClinicManagement.API.Startup;

/// <summary>
/// Encrypts every Google Calendar refresh token still held in the clear (<c>hosted-security-hardening</c>
/// FR-3.4). It was the last credential in this database stored as plaintext.
///
/// <para><b>Why a startup pass and not SQL in the migration.</b> Encrypting needs the Data Protection key ring,
/// which a migration cannot reach — raw SQL there could only copy the plaintext across, which encrypts nothing
/// and would let <c>google-token-protected</c> report success over a column still readable off a stolen disk.
/// So the migration adds the column and this moves the values, on the same
/// <c>RunsStartupBackfills</c> pass as the catalog seeder and the admin backfill.</para>
///
/// <para><b>Idempotent</b> — it selects only rows that still hold a plaintext token, and each one it converts
/// stops matching. A second boot converts nothing.</para>
///
/// <para>⚠️ <b>The plaintext is cleared as each row is converted, and that is what makes the check meaningful.</b>
/// <c>verify-schema</c>'s <c>google-token-protected</c> counts rows still holding cleartext, so leaving the
/// original beside the ciphertext would peg that figure at « every connected clinic » for ever — and the column
/// could never be dropped, because dropping it is gated on that figure reading zero.</para>
///
/// <para>⚠️ <b>A row that already carries ciphertext is left alone even if plaintext sits beside it.</b> That
/// combination can only come from a re-connect that happened between the migration and this pass; the ciphertext
/// is the newer of the two, and overwriting it with the older plaintext would silently restore a revoked token.</para>
/// </summary>
public static class GoogleTokenProtectionBackfill
{
    public static async Task<int> RunAsync(
        ApplicationDbContext context,
        IGoogleTokenProtector protector,
        IUnitOfWork unitOfWork,
        CancellationToken cancellationToken = default)
    {
        var clinics = await context.Clinics
            .Where(c => c.GoogleRefreshToken != null && c.GoogleRefreshTokenProtected == null)
            .ToListAsync(cancellationToken);

        foreach (var clinic in clinics)
        {
            clinic.SetProtectedGoogleRefreshToken(protector.Protect(clinic.GoogleRefreshToken!));
        }

        if (clinics.Count > 0)
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return clinics.Count;
    }
}
