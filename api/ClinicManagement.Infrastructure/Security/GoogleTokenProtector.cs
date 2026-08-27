using ClinicManagement.Application.Common.Interfaces;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging;

namespace ClinicManagement.Infrastructure.Security;

/// <summary>
/// <see cref="IGoogleTokenProtector"/> over the deployment's Data Protection key ring, on
/// <see cref="UserSecretProtector"/>'s pattern (FR-3.4).
///
/// <para>⚠️ <b>A failed unprotect returns false and the sync refuses.</b> The tempting degradation — fall back to
/// the legacy plaintext column, or treat the clinic as « not connected » — is what FR-3.3 rules out: the first
/// keeps the credential readable off a stolen disk for ever, and the second makes a broken key ring look
/// identical to a practice that never connected Google, which nobody would investigate. Recovery is for the
/// clinic to re-connect from « Paramètres → Google Agenda », and the log line says so.</para>
/// </summary>
public class GoogleTokenProtector : IGoogleTokenProtector
{
    private const string Purpose = "ClinicManagement.Clinic.GoogleRefreshToken.v1";

    private readonly IDataProtector _protector;
    private readonly ILogger<GoogleTokenProtector> _logger;

    public GoogleTokenProtector(IDataProtectionProvider dataProtectionProvider, ILogger<GoogleTokenProtector> logger)
    {
        _protector = dataProtectionProvider.CreateProtector(Purpose);
        _logger = logger;
    }

    public string Protect(string refreshToken) => _protector.Protect(refreshToken);

    public bool TryUnprotect(string protectedRefreshToken, out string refreshToken)
    {
        // Set first, so no exit path can leave the caller holding the ciphertext.
        refreshToken = string.Empty;

        if (string.IsNullOrEmpty(protectedRefreshToken))
        {
            return false;
        }

        try
        {
            refreshToken = _protector.Unprotect(protectedRefreshToken);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Impossible de déchiffrer le jeton Google Agenda d'un cabinet : la clé de protection des " +
                "données a changé ou est absente. Le cabinet doit reconnecter son agenda depuis " +
                "« Paramètres → Google Agenda ».");
            return false;
        }
    }
}
