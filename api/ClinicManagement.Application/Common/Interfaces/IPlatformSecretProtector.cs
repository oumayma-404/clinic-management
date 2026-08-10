namespace ClinicManagement.Application.Common.Interfaces;

/// <summary>
/// Encrypts the console's TOTP secret at rest (FR-1), over the deployment's Data Protection key ring.
///
/// <para>A seam of its own rather than a reuse of <c>IReminderSecretProtector</c>: Data Protection purposes are
/// what keep two kinds of ciphertext from being interchangeable, so a reminder credential must not be decryptable
/// by the code that reads a second factor and vice versa.</para>
///
/// <para>⚠️ <b>An undecryptable secret is a refusal, never a bypass.</b> A rotated or lost key ring makes
/// <see cref="TryUnprotect"/> return false, and the sign-in then refuses as if the code were wrong — the operator
/// recovers with the bootstrap verb's <c>--reset-totp</c>. The reminder side degrades to « channel non configuré »
/// for the same class of failure; here the equivalent degradation would be « no second factor required », which is
/// why this returns a bool the caller has to read rather than a nullable somebody could <c>??</c> past.</para>
/// </summary>
public interface IPlatformSecretProtector
{
    string Protect(string secret);

    bool TryUnprotect(string protectedSecret, out string secret);
}
