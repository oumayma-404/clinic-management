namespace ClinicManagement.Application.Common.Interfaces;

/// <summary>
/// Encrypts/decrypts a clinic's own TTN « El Fatoora » secrets (the OAuth2 client secret and the signing
/// certificate's PFX password) so they are stored as ciphertext at rest. A thin seam over ASP.NET Core Data
/// Protection, implemented in Infrastructure — the same shape as <see cref="IReminderSecretProtector"/>.
///
/// <para>Separate from that one on purpose: a Data-Protection <i>purpose</i> is what stops ciphertext written
/// for one subsystem being read by another, so reusing the reminder purpose for a signing-certificate password
/// would be the one misuse the framework's model exists to prevent.</para>
///
/// <para>A failed <c>Unprotect</c> (rotated or unavailable key ring) throws; the resolver translates that into
/// « this clinic's identity is unusable » with an operator-readable reason, never a crash. That the key ring
/// survives a redeploy at all is what <c>DataProtection:KeyRingPath</c> guarantees — required in
/// <c>HostedMultiTenant</c> since US-6, which is why that step had to land before this one.</para>
/// </summary>
public interface ITtnSecretProtector
{
    string Protect(string plaintext);
    string Unprotect(string ciphertext);
}
