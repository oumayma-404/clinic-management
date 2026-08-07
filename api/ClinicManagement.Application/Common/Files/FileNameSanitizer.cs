using System.Text;

namespace ClinicManagement.Application.Common.Files;

/// <summary>
/// AC-2.10 — what a client-supplied file name is allowed to become before it is stored.
///
/// <para>The name was persisted verbatim and handed straight to <c>File(..., fileDownloadName)</c>. A browser
/// sends whatever the operating system had, which on a shared scanner is routinely a full path, and a name is the
/// one part of an upload the validator cannot refuse without refusing the file — so it is repaired, not rejected.</para>
/// </summary>
public static class FileNameSanitizer
{
    /// <summary>Long enough for a real scan name, short enough to survive every filesystem it is saved onto.</summary>
    public const int MaxLength = 180;

    private const string Fallback = "fichier";

    /// <summary>
    /// Strips path segments and control characters, collapses whitespace, and bounds the length while keeping the
    /// extension — truncating the whole name would silently change the format the file is stored as.
    /// </summary>
    public static string Sanitize(string? fileName)
    {
        var leaf = (fileName ?? string.Empty)
            .Split('/', '\\')
            .LastOrDefault() ?? string.Empty;

        var cleaned = new StringBuilder(leaf.Length);
        foreach (var character in leaf)
        {
            if (char.IsControl(character) || character == '"' || character == '|'
                || character == '<' || character == '>' || character == ':' || character == '*'
                || character == '?')
            {
                continue;
            }

            cleaned.Append(character == '\t' ? ' ' : character);
        }

        var name = string.Join(' ', cleaned.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries)).Trim('.', ' ');
        if (name.Length == 0)
        {
            return Fallback;
        }

        var extension = ExtensionOf(name);
        if (extension.Length == 0)
        {
            return name.Length > MaxLength ? name[..MaxLength] : name;
        }

        var baseName = name[..(name.Length - extension.Length - 1)];
        if (baseName.Length == 0)
        {
            baseName = Fallback;
        }

        var room = MaxLength - extension.Length - 1;
        if (baseName.Length > room)
        {
            baseName = baseName[..Math.Max(1, room)];
        }

        return $"{baseName}.{extension}";
    }

    /// <summary>
    /// The same repair applied to a rename's <b>base</b> name (AC-4.1/AC-4.6), bounded so that
    /// <c>base.extension</c> still fits <see cref="MaxLength"/>. Returns an empty string when nothing usable
    /// survives — the caller refuses rather than silently storing « fichier », since here the user typed
    /// something and deserves to be told it was not a name.
    /// </summary>
    public static string SanitizeBaseName(string? baseName, string extension)
    {
        var room = extension.Length == 0 ? MaxLength : MaxLength - extension.Length - 1;
        var sanitized = Sanitize(baseName);
        if (sanitized == Fallback && string.IsNullOrWhiteSpace(baseName))
        {
            return string.Empty;
        }

        return sanitized.Length > room ? sanitized[..Math.Max(1, room)] : sanitized;
    }

    /// <summary>The lower-case extension without its dot, or an empty string when the name carries none.</summary>
    public static string ExtensionOf(string? fileName)
    {
        var name = fileName ?? string.Empty;
        var dot = name.LastIndexOf('.');
        if (dot <= 0 || dot == name.Length - 1)
        {
            return string.Empty;
        }

        var extension = name[(dot + 1)..].ToLowerInvariant();
        return extension.All(char.IsLetterOrDigit) ? extension : string.Empty;
    }
}
