using ClinicManagement.Application.Common.Interfaces;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging;

namespace ClinicManagement.Infrastructure.Security;

/// <summary>
/// Encrypts a clinic user's TOTP secret at rest over the deployment's Data Protection key ring
/// (<c>hosted-security-hardening</c> FR-1.3), on <see cref="PlatformSecretProtector"/>'s pattern.
///
/// <para><b>Its own purpose string</b> — a clinic second factor must not be decryptable by the code that reads a
/// console one, or a reminder credential. The purpose feeds the key derivation, so the framework enforces it.</para>
///
/// <para>⚠️ <b>A failed unprotect returns false; it never throws and never yields the input.</b> The consequence
/// of a lost or rotated ring is now <i>sign-in itself</i> for every administrator on the deployment, not merely
/// the reminder channels it used to protect — so the recovery path is named in the log line, and the alternative
/// degradation (« no second factor required ») is the one this must never take. Recovery is
/// <c>reset-user-totp --email &lt;address&gt;</c>, or an administrator's reset from « Utilisateurs ».</para>
///
/// <para>⚠️ Registered <c>AddSingleton</c> inside <c>AddInfrastructure</c>, which is what lets the console verb —
/// whose container is that method alone — resolve it.</para>
/// </summary>
public class UserSecretProtector : IUserSecretProtector
{
    private const string Purpose = "ClinicManagement.User.TotpSecret.v1";

    private readonly IDataProtector _protector;
    private readonly ILogger<UserSecretProtector> _logger;

    public UserSecretProtector(
        IDataProtectionProvider dataProtectionProvider,
        ILogger<UserSecretProtector> logger)
    {
        _protector = dataProtectionProvider.CreateProtector(Purpose);
        _logger = logger;
    }

    public string Protect(string secret) => _protector.Protect(secret);

    public bool TryUnprotect(string protectedSecret, out string secret)
    {
        // Set first, so no exit path can leave the caller holding the ciphertext.
        secret = string.Empty;

        if (string.IsNullOrEmpty(protectedSecret))
        {
            return false;
        }

        try
        {
            secret = _protector.Unprotect(protectedSecret);
            return true;
        }
        catch (Exception ex)
        {
            // Error, because the only cause is a key ring this deployment can no longer read, while the symptom
            // the user meets — « code de vérification invalide » — points at their phone instead.
            _logger.LogError(ex,
                "Impossible de déchiffrer un secret de second facteur : la clé de protection des données a " +
                "changé ou est absente. Réémettez-le avec « reset-user-totp --email <adresse> ».");
            return false;
        }
    }
}
