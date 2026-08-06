using ClinicManagement.Application.Common.Exceptions;
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
/// <para>⚠️ <b>A clinic's own identity is not a partial fall-back, and « own identity » is any of the four columns
/// — not just the certificate</b> (review finding 3). <c>Clinic.SetTtnIdentity</c> deliberately allows a TTN account
/// with no certificate yet, since the signing half and the submitting half are provisioned separately; keying the
/// precedence on <c>TtnCertificateKey</c> alone sent exactly that clinic down the install branch, which returns the
/// install's <b>credentials</b> too — filing the declaration under the install-wide matricule. That is the
/// « signed as clinic A, filed under clinic B » state this class exists to make unreachable, so a clinic carrying
/// any part of an identity is refused rather than substituted.</para>
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

        if (clinic.TtnCertificateKey != null)
        {
            return await ResolveClinicIdentityAsync(clinic, cancellationToken);
        }

        if (HasPartialOwnIdentity(clinic))
        {
            throw new TtnIdentityUnavailableException(
                "Ce cabinet a un compte TTN mais pas encore de certificat de signature électronique. "
                + "Déposez son certificat qualifié (PFX) et son mot de passe pour pouvoir déclarer ses factures — "
                + "le certificat de l'installation ne peut pas signer sous le matricule d'un autre.");
        }

        return ResolveInstallIdentity(clinic);
    }

    /// <summary>
    /// True when the clinic was given part of an identity but not a certificate. Read over <b>all four</b> columns
    /// because any one of them is an operator saying « this clinic files under its own name ».
    /// </summary>
    private static bool HasPartialOwnIdentity(Clinic clinic) =>
        clinic.TtnUsername != null
        || clinic.TtnApiSecretEncrypted != null
        || clinic.TtnCertificatePasswordEncrypted != null;

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
            throw new TtnIdentityUnavailableException(
                "Ce cabinet n'a pas de certificat de signature électronique. Sur un hébergement multi-cabinets, "
                + "chaque cabinet doit fournir son propre certificat qualifié et son propre compte TTN — "
                + "le certificat de l'installation ne peut pas signer à sa place.");
        }

        var certPath = TtnConfig.CertificatePath(_configuration);
        if (!File.Exists(certPath))
        {
            throw new TtnIdentityUnavailableException(
                "Certificat de signature électronique introuvable. Déposez le certificat qualifié (PFX) dans le "
                + "dossier .local/ avant l'envoi à El Fatoora.");
        }

        _logger.LogInformation(
            "Clinic {ClinicId} has no El Fatoora identity of its own; using the per-install certificate.", clinic.Id);

        return new ResolvedTtnIdentity(
            ReadInstallCertificate(certPath),
            TtnConfig.CertificatePassword(_configuration),
            TtnConfig.Username(_configuration),
            TtnConfig.ApiSecret(_configuration),
            TtnIdentitySource.Install);
    }

    /// <summary>
    /// Reads the per-install PFX, wrapped like <see cref="DownloadCertificateAsync"/>. Unwrapped, a permissions or
    /// IO error escaped this class's stated contract as an <c>IOException</c> and landed in
    /// <c>EInvoiceService</c>'s generic catch, replacing the reason on the invoice row with « Erreur lors de l'envoi
    /// à El Fatoora. » — on a queue that retries, telling the operator nothing about what to fix.
    /// </summary>
    private byte[] ReadInstallCertificate(string certPath)
    {
        try
        {
            return File.ReadAllBytes(certPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not read the per-install TTN signing certificate at {CertPath}.", certPath);
            throw new TtnIdentityUnavailableException(
                "Le certificat de signature de l'installation est illisible. Vérifiez les droits d'accès au "
                + "fichier PFX dans le dossier .local/.", ex);
        }
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
            throw new TtnIdentityUnavailableException(
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
            throw new TtnIdentityUnavailableException(
                $"Impossible de déchiffrer {what} de ce cabinet (clé de protection indisponible ou changée). "
                + "Ressaisissez-le dans les paramètres El Fatoora.", ex);
        }
    }
}
