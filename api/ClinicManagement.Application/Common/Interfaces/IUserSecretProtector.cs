namespace ClinicManagement.Application.Common.Interfaces;

/// <summary>
/// Encrypts a <b>clinic</b> user's TOTP secret at rest (<c>hosted-security-hardening</c> FR-1.3).
///
/// <para>⚠️ <b>A separate seam from <c>IPlatformSecretProtector</c>, with its own purpose string</b>, so a clinic
/// ciphertext and a console one are not interchangeable. The purpose is part of the key derivation, so that
/// separation is enforced by the framework rather than by convention.</para>
///
/// <para>⚠️ <b><see cref="TryUnprotect"/> returns a <c>bool</c> and not a nullable</b>, deliberately: a nullable
/// invites <c>?? something</c> at the call site, and the only safe « something » here does not exist. A secret
/// that cannot be decrypted must refuse the sign-in, never degrade to « no second factor required ».</para>
/// </summary>
public interface IUserSecretProtector
{
    string Protect(string secret);

    /// <summary>
    /// Decrypts, or reports failure without throwing and without ever yielding the input.
    ///
    /// <para>Implementations set <paramref name="secret"/> to empty <i>first</i>, so no path can leave a caller
    /// holding the ciphertext and believing it is the secret.</para>
    /// </summary>
    bool TryUnprotect(string protectedSecret, out string secret);
}
