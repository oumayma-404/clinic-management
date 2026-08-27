namespace ClinicManagement.Application.Common.Files;

/// <summary>
/// The cross-check behind AC-2.3: « do these bytes positively claim to be some <b>other</b> format? »
///
/// <para>An entry whose rule is <c>Required</c> is verified against its own signature and needs nothing else. The
/// interesting case is an <c>Advisory</c> or <c>None</c> entry, where the absence of a marker proves nothing: a
/// <c>.txt</c> renamed to <c>.stl</c> must still be accepted (an ASCII STL <i>is</i> text), while a PDF renamed to
/// <c>.stl</c> must not be — it says what it is in its first five bytes.</para>
/// </summary>
public static class SignatureIndex
{
    private static readonly IReadOnlyList<FileTypeEntry> Identifiable = FileTypeCatalog.All
        .Where(entry => entry.Signature.Kind == SignatureKind.Required)
        .ToList();

    /// <summary>The entry whose required signature these bytes match, or <c>null</c> when they claim nothing.</summary>
    public static FileTypeEntry? IdentifyOrNull(ReadOnlySpan<byte> header)
    {
        foreach (var entry in Identifiable)
        {
            if (entry.Signature.Matches(header))
            {
                return entry;
            }
        }

        return null;
    }
}
