namespace ClinicManagement.Domain.Services;

/// <summary>
/// The single composer of a vault file's path inside the cabinet's coffre, and the counterpart of
/// Infrastructure's <c>ClinicStorageKey</c> for the other residency.
///
/// <para>⚠️ <b>Derived, never stored.</b> The path is a pure function of two ids the row already carries, so
/// « where is this file? » is answered by computing it and looking at the disk — the same reason the patient-file
/// mirror keeps no index beside its folder. A stored copy would be a second thing able to disagree with the row,
/// and it is the copy that would drift.</para>
///
/// <para>⚠️ <b>Machine-shaped on purpose.</b> Ids rather than patient names, exactly like
/// <c>clinics/{clinicId}/{guid}-{timestamp}</c>: a human-readable tree needs collision rules that depend on every
/// other row, and the coffre is the application's store rather than a folder anyone browses.</para>
/// </summary>
public static class VaultPath
{
    /// <summary>The folder the coffre owns, beside the archive's own files.</summary>
    public const string RootFolderName = "coffre";

    /// <summary>
    /// Where <paramref name="fileId"/>'s bytes sit, relative to the coffre's root. The extension is taken from
    /// the stored file name so it matches what the browser wrote.
    /// </summary>
    public static string For(Guid patientId, Guid fileId, string? extension)
    {
        if (patientId == Guid.Empty)
        {
            throw new ArgumentException("Un fichier du coffre doit nommer son patient.", nameof(patientId));
        }

        if (fileId == Guid.Empty)
        {
            throw new ArgumentException("Un fichier du coffre doit avoir un identifiant.", nameof(fileId));
        }

        var suffix = Normalize(extension);

        return $"{RootFolderName}/{patientId:D}/{fileId:D}{suffix}";
    }

    /// <summary>The extension of a stored file name, dot included and lower-cased; empty when it carries none.</summary>
    public static string ExtensionOf(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return string.Empty;
        }

        var dot = fileName.LastIndexOf('.');

        return dot > 0 && dot < fileName.Length - 1 ? fileName[dot..].ToLowerInvariant() : string.Empty;
    }

    private static string Normalize(string? extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
        {
            return string.Empty;
        }

        var trimmed = extension.Trim().TrimStart('.').ToLowerInvariant();

        return trimmed.Length == 0 ? string.Empty : $".{trimmed}";
    }
}
