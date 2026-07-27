using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;

namespace ClinicManagement.Infrastructure.Security;

/// <summary>The two PostgreSQL passwords a Local install persists so a reinstall can reuse them.</summary>
/// <param name="ClinicUserPassword">Password for the <c>clinic_user</c> login (also baked into the connection string).</param>
/// <param name="PostgresSuperPassword">Password for the <c>postgres</c> superuser.</param>
public sealed record DbCredentials(string ClinicUserPassword, string PostgresSuperPassword);

/// <summary>
/// Result of reading a credentials file: the recovered passwords, plus whether the file was still in the
/// pre-hardening <b>plaintext</b> form and therefore needs re-writing protected (spec AC-3.3).
/// </summary>
public sealed record DbCredentialFileRead(DbCredentials Credentials, bool WasLegacyPlaintext);

/// <summary>
/// Protects the per-install <c>.local/db-credentials</c> file at rest.
///
/// Before this, the file held both the <c>clinic_user</c> <b>and</b> the <c>postgres</c> superuser password
/// in cleartext under <c>Program Files</c> (audit § 2 finding 4). Tightened ACLs stop other local accounts
/// reading it, but not an admin-level foothold or a disk-level copy — so the payload is additionally
/// encrypted through ASP.NET Core Data Protection, whose key ring is itself machine-scoped DPAPI-protected
/// on the Local Windows install (see <see cref="LocalDataProtection"/>). Copying the file — or the whole
/// <c>.local/</c> folder — to another machine therefore yields nothing.
///
/// This type is deliberately pure string-in/string-out so it is unit-testable; file I/O lives in the
/// console verbs that call it.
/// </summary>
public sealed class DbCredentialProtector
{
    /// <summary>Data Protection purpose. Changing it invalidates existing ciphertext.</summary>
    public const string Purpose = "ClinicManagement.DbCredentials.v1";

    /// <summary>
    /// First line of a protected file. Its presence is how a protected file is told apart from a legacy
    /// plaintext one written by an earlier installer, which is what makes the upgrade migration possible.
    /// </summary>
    public const string CipherMarker = "CMDPAPI1";

    private readonly IDataProtector _protector;

    public DbCredentialProtector(IDataProtectionProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        _protector = provider.CreateProtector(Purpose);
    }

    /// <summary>True when <paramref name="fileContent"/> is already in the protected form.</summary>
    public static bool IsProtected(string? fileContent)
    {
        if (string.IsNullOrWhiteSpace(fileContent))
        {
            return false;
        }

        using var reader = new StringReader(fileContent);
        return string.Equals(reader.ReadLine()?.Trim(), CipherMarker, StringComparison.Ordinal);
    }

    /// <summary>Renders <paramref name="credentials"/> as the content of a protected credentials file.</summary>
    public string ProtectFileContent(DbCredentials credentials)
    {
        ArgumentNullException.ThrowIfNull(credentials);

        if (string.IsNullOrWhiteSpace(credentials.ClinicUserPassword) ||
            string.IsNullOrWhiteSpace(credentials.PostgresSuperPassword))
        {
            throw new InvalidOperationException(
                "Les deux mots de passe de la base (clinic_user et postgres) sont requis.");
        }

        var payload = credentials.ClinicUserPassword + "\n" + credentials.PostgresSuperPassword;
        return CipherMarker + "\r\n" + _protector.Protect(payload) + "\r\n";
    }

    /// <summary>
    /// Recovers the passwords from a credentials file in either form. A legacy plaintext file is read
    /// as-is and flagged so the caller can re-write it protected (AC-3.3).
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The file is empty, malformed, or protected but undecryptable on this machine (e.g. Windows was
    /// rebuilt, destroying the DPAPI key that guards the key ring — spec EC-4). The installer turns this
    /// into its existing "restore from backup, or deliberately delete pgdata" abort rather than silently
    /// regenerating passwords against a live cluster.
    /// </exception>
    public DbCredentialFileRead ReadFileContent(string? fileContent)
    {
        if (string.IsNullOrWhiteSpace(fileContent))
        {
            throw new InvalidOperationException("Le fichier d'identifiants de la base est vide.");
        }

        if (!IsProtected(fileContent))
        {
            return new DbCredentialFileRead(ParsePayload(fileContent), WasLegacyPlaintext: true);
        }

        // Skip the marker line; everything after it is the protected payload.
        var markerEnd = fileContent.IndexOf('\n');
        var ciphertext = markerEnd >= 0 ? fileContent[(markerEnd + 1)..].Trim() : string.Empty;

        if (ciphertext.Length == 0)
        {
            throw new InvalidOperationException(
                "Le fichier d'identifiants de la base est marqué comme chiffré mais ne contient aucune donnée.");
        }

        string payload;
        try
        {
            payload = _protector.Unprotect(ciphertext);
        }
        catch (CryptographicException ex)
        {
            throw new InvalidOperationException(
                "Impossible de déchiffrer le fichier d'identifiants de la base sur cette machine. " +
                "La clé de protection locale est absente ou a changé (par exemple après une réinstallation " +
                "de Windows). Restaurez le fichier depuis une sauvegarde, ou supprimez volontairement le " +
                "dossier « pgdata » pour repartir de zéro (les données existantes seront perdues).", ex);
        }

        return new DbCredentialFileRead(ParsePayload(payload), WasLegacyPlaintext: false);
    }

    /// <summary>Parses the two-line password payload shared by both file forms.</summary>
    private static DbCredentials ParsePayload(string payload)
    {
        var lines = payload
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.Length > 0)
            .ToArray();

        if (lines.Length < 2)
        {
            throw new InvalidOperationException(
                "Le fichier d'identifiants de la base est incomplet : deux mots de passe sont attendus.");
        }

        return new DbCredentials(lines[0], lines[1]);
    }
}
