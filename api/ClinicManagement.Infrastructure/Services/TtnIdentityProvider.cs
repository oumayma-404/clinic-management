using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Infrastructure.Deployment;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace ClinicManagement.Infrastructure.Services;

/// <summary>
/// The one implementation of « whose certificate signs this clinic's invoices » (multi-tenant-cloud US-4).
///
/// <para>Precedence: the clinic's own identity when it has one, otherwise the per-install pair — and that
/// second branch exists only where <c>DeploymentProfile.SharesInstallWideTtnIdentity</c> holds, i.e. where the
/// install serves one clinic and « per install » and « per clinic » name the same thing.</para>
///
/// <para>⚠️ <b>A clinic's own certificate is not a partial fall-back.</b> If the clinic has a certificate key,
/// the per-install certificate is never reached — not even when the blob turns out to be missing. Quietly
/// substituting another identity for a clinic that was explicitly given one is how the wrong practice's name
/// ends up on a validated, irreversible declaration; the refusal is the correct outcome.</para>
/// </summary>
public class TtnIdentityProvider : ITtnIdentityProvider
{
    private readonly IClinicRepository _clinicRepository;
    private readonly IFileStorage _fileStorage;
    private readonly ITtnSecretProtector _protector;
    private readonly DeploymentProfile _profile;
    private readonly IConfiguration _configuration;
    private readonly ILogger<TtnIdentityProvider> _logger;

    public TtnIdentityProvider(
        IClinicRepository clinicRepository,
        IFileStorage fileStorage,
        ITtnSecretProtector protector,
        DeploymentProfile profile,
        IConfiguration configuration,
        ILogger<TtnIdentityProvider> logger)
    {
        _clinicRepository = clinicRepository;
        _fileStorage = fileStorage;
        _protector = protector;
        _profile = profile;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<ResolvedTtnIdentity> ResolveAsync(Guid clinicId, CancellationToken cancellationToken = default)
    {
        var clinic = await _clinicRepository.GetByIdAsync(clinicId, cancellationToken)
            ?? throw new InvalidOperationException("Cabinet introuvable pour la résolution de l'identité El Fatoora.");

        return clinic.TtnCertificateKey != null
            ? await ResolveClinicIdentityAsync(clinic, cancellationToken)
            : ResolveInstallIdentity(clinic);
    }

    private async Task<ResolvedTtnIdentity> ResolveClinicIdentityAsync(Clinic clinic, CancellationToken cancellationToken)
    {
        var certificateBytes = await DownloadCertificateAsync(clinic.TtnCertificateKey!, cancellationToken);

        var identity = new ResolvedTtnIdentity(
            certificateBytes,
            Decrypt(clinic.TtnCertificatePasswordEncrypted, "le mot de passe du certificat"),
            clinic.TtnUsername,
            Decrypt(clinic.TtnApiSecretEncrypted, "le secret d'API TTN"),
            TtnIdentitySource.Clinic);

        _logger.LogInformation(
            "Resolved the clinic's own El Fatoora identity for clinic {ClinicId} ({ByteCount} bytes of certificate).",
            clinic.Id, certificateBytes.Length);

        return identity;
    }

    private ResolvedTtnIdentity ResolveInstallIdentity(Clinic clinic)
    {
        if (!_profile.SharesInstallWideTtnIdentity)
        {
            throw new InvalidOperationException(
                "Ce cabinet n'a pas de certificat de signature électronique. Sur un hébergement multi-cabinets, "
                + "chaque cabinet doit fournir son propre certificat qualifié et son propre compte TTN — "
                + "le certificat de l'installation ne peut pas signer à sa place.");
        }

        var certPath = TtnConfig.CertificatePath(_configuration);
        if (!File.Exists(certPath))
        {
            throw new InvalidOperationException(
                "Certificat de signature électronique introuvable. Déposez le certificat qualifié (PFX) dans le "
                + "dossier .local/ avant l'envoi à El Fatoora.");
        }

        _logger.LogInformation(
            "Clinic {ClinicId} has no El Fatoora identity of its own; using the per-install certificate.", clinic.Id);

        return new ResolvedTtnIdentity(
            File.ReadAllBytes(certPath),
            TtnConfig.CertificatePassword(_configuration),
            TtnConfig.Username(_configuration),
            TtnConfig.ApiSecret(_configuration),
            TtnIdentitySource.Install);
    }

    private async Task<byte[]> DownloadCertificateAsync(string storageKey, CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = await _fileStorage.DownloadAsync(storageKey, cancellationToken);
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer, cancellationToken);
            return buffer.ToArray();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // The key is safe to log; it names a blob, not a secret.
            _logger.LogError(ex, "Could not read the TTN signing certificate at storage key {StorageKey}.", storageKey);
            throw new InvalidOperationException(
                "Le certificat de signature du cabinet est introuvable ou illisible dans le stockage de fichiers.", ex);
        }
    }

    /// <summary>
    /// Decrypts one stored secret. A rotated or unavailable key ring makes this unrecoverable, so it is
    /// reported rather than swallowed: the reminder channels can degrade to « non configuré » and merely stop
    /// nudging, but signing with a certificate whose password will not open is not a lesser service — it is a
    /// declaration that never leaves, and the operator has to know which secret to re-enter.
    /// </summary>
    private string? Decrypt(string? ciphertext, string what)
    {
        if (string.IsNullOrWhiteSpace(ciphertext))
        {
            return null;
        }

        try
        {
            return _protector.Unprotect(ciphertext);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Impossible de déchiffrer {what} de ce cabinet (clé de protection indisponible ou changée). "
                + "Ressaisissez-le dans les paramètres El Fatoora.", ex);
        }
    }
}
