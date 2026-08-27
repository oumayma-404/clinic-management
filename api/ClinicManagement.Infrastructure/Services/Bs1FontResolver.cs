using PdfSharp.Fonts;

namespace ClinicManagement.Infrastructure.Services;

// Process-wide PdfSharp font resolver used only by the CNAM BS1 overlay renderer. The core PdfSharp
// package ships no fonts, so a resolver must supply the bytes for any text we stamp onto the form.
// We map every request to a single sans-serif face (regular + bold), loading it from the OS font
// store — Windows first (the Local/offline-LAN deployment target), then common Linux paths. If none
// is found we fail fast with a clear French operator message rather than emit a fontless PDF.
internal sealed class Bs1FontResolver : IFontResolver
{
    private const string RegularFaceName = "bs1-sans";
    private const string BoldFaceName = "bs1-sans-bold";

    private static readonly object Gate = new();
    // volatile: the fast-path read of _installed runs outside the lock, so its write must act as a
    // release barrier that publishes _regularBytes/_boldBytes before any lock-free observer sees true.
    private static volatile bool _installed;
    private static byte[]? _regularBytes;
    private static byte[]? _boldBytes;

    private static readonly string[] RegularCandidates =
    {
        @"C:\Windows\Fonts\arial.ttf",
        "/usr/share/fonts/truetype/liberation/LiberationSans-Regular.ttf",
        "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf",
        "/Library/Fonts/Arial.ttf",
    };

    private static readonly string[] BoldCandidates =
    {
        @"C:\Windows\Fonts\arialbd.ttf",
        "/usr/share/fonts/truetype/liberation/LiberationSans-Bold.ttf",
        "/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf",
        "/Library/Fonts/Arial Bold.ttf",
    };

    // Idempotently load the font bytes and register this resolver with PdfSharp. Safe to call before
    // every render; the actual install happens once.
    public static void EnsureInstalled()
    {
        if (_installed)
        {
            return;
        }

        lock (Gate)
        {
            if (_installed)
            {
                return;
            }

            _regularBytes = LoadFirstAvailable(RegularCandidates, out var regularError);
            _boldBytes = LoadFirstAvailable(BoldCandidates, out _) ?? _regularBytes;

            if (_regularBytes == null)
            {
                // Distinguish "no candidate present" from "a candidate exists but is unreadable" so the
                // operator fixes the right thing (install a font vs. check file permissions/locks).
                var detail = regularError != null
                    ? $" (une police candidate a été trouvée mais est illisible : {regularError.Message})"
                    : string.Empty;
                throw new InvalidOperationException(
                    "Génération du bulletin CNAM impossible : aucune police système (Arial, Liberation ou DejaVu) "
                    + "n'a été trouvée pour composer le formulaire. Installez une police sans-serif standard sur le serveur."
                    + detail);
            }

            // ??= keeps any resolver already registered process-wide. If a foreign resolver won, our
            // bs1-sans faces are unresolvable and the AC-6 fail-fast would degrade into an opaque
            // render-time failure — so surface it here instead.
            GlobalFontSettings.FontResolver ??= new Bs1FontResolver();
            if (GlobalFontSettings.FontResolver is not Bs1FontResolver)
            {
                throw new InvalidOperationException(
                    "Génération du bulletin CNAM impossible : un autre résolveur de polices est déjà enregistré "
                    + "pour PdfSharp, empêchant le chargement de la police du formulaire BS1.");
            }

            _installed = true;
        }
    }

    public FontResolverInfo? ResolveTypeface(string familyName, bool isBold, bool isItalic)
        => new FontResolverInfo(isBold ? BoldFaceName : RegularFaceName);

    public byte[]? GetFont(string faceName)
        => faceName == BoldFaceName ? _boldBytes : _regularBytes;

    private static byte[]? LoadFirstAvailable(string[] paths, out Exception? lastError)
    {
        lastError = null;
        foreach (var path in paths)
        {
            try
            {
                if (File.Exists(path))
                {
                    return File.ReadAllBytes(path);
                }
            }
            catch (Exception ex)
            {
                // Unreadable candidate (locked / permission) — remember why and try the next one, so a
                // present-but-unreadable font isn't collapsed into the "none found" message.
                lastError = ex;
            }
        }

        return null;
    }
}
