namespace ClinicManagement.Application.Common.Interfaces;

/// <summary>
/// Encrypts/decrypts per-clinic reminder secret credentials (SMS API key, WhatsApp access token) so they
/// are stored as ciphertext at rest. A thin seam over ASP.NET Core Data Protection (implemented in
/// Infrastructure) — the write path encrypts an incoming secret; the resolver decrypts it in-process at send
/// time. Ciphertext is opaque; a failed <see cref="Unprotect"/> (e.g. rotated/unavailable key) throws.
/// </summary>
public interface IReminderSecretProtector
{
    string Protect(string plaintext);
    string Unprotect(string ciphertext);
}
