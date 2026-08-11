using ClinicManagement.Application.Common.Interfaces;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging;

namespace ClinicManagement.Infrastructure.Security;

/// <summary>
/// Encrypts the console's TOTP secret at rest over the deployment's Data Protection key ring (FR-1), on
/// <c>ReminderSecretProtector</c>'s pattern.
///
/// <para><b>Its own purpose string</b>, which is what keeps the two kinds of ciphertext from being
/// interchangeable: a reminder credential must not be decryptable by the code that reads a second factor, and
/// vice versa. The purpose is part of the key derivation, so this is enforced by the framework rather than by
/// convention.</para>
///
/// <para>⚠️ <b>A failed unprotect returns false; it never throws and never yields the input.</b> On the hosted
/// profile the key ring is already required to sit on a durable volume — but a ring that is nonetheless lost or
/// rotated now costs <i>sign-in itself</i>, not merely the reminder channels it protected before. The caller
/// refuses the sign-in and the operator recovers with <c>platform-account --reset-totp</c>; the alternative
/// degradation, « no second factor required », is the one this must never take.</para>
/// </summary>
public class PlatformSecretProtector : IPlatformSecretProtector
{
    private const string Purpose = "ClinicManagement.PlatformConsole.TotpSecret.v1";

    private readonly IDataProtector _protector;
    private readonly ILogger<PlatformSecretProtector> _logger;

    public PlatformSecretProtector(
        IDataProtectionProvider dataProtectionProvider,
        ILogger<PlatformSecretProtector> logger)
    {
        _protector = dataProtectionProvider.CreateProtector(Purpose);
        _logger = logger;
    }

    public string Protect(string secret) => _protector.Protect(secret);

    public bool TryUnprotect(string protectedSecret, out string secret)
    {
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
            // Logged at Error because the only cause is a key ring this deployment can no longer read, and the
            // symptom the operator sees — « code de vérification invalide » — points at the phone instead.
            _logger.LogError(ex,
                "Impossible de déchiffrer un secret de second facteur : la clé de protection des données a " +
                "changé ou est absente. Réémettez le secret avec « platform-account --reset-totp ».");
            return false;
        }
    }
}
