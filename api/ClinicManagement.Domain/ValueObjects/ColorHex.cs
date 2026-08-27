using ClinicManagement.Domain.Common;

namespace ClinicManagement.Domain.ValueObjects;

/// <summary>One nuance of a hue family: the hex, and which of « Clair / Moyen / Foncé » it is.</summary>
public sealed record ColorTone(string Hex, string ToneFr);

/// <summary>
/// A hue family and its nuances — the unit the picker offers, so choosing a colour stays two gestures
/// however large the palette grows.
/// </summary>
public sealed record ColorFamily(string Key, string LabelFr, IReadOnlyList<ColorTone> Tones);

public class ColorHex : ValueObject
{
    private const string Clair = "Clair";
    private const string Moyen = "Moyen";
    private const string Fonce = "Foncé";

    /// <summary>
    /// The curated palette: 12 hue families × 3 nuances. Grouping is the point — an act catalogue outgrows ten
    /// colours long before it outgrows twelve hues, and 36 loose swatches is a wall rather than a choice.
    /// </summary>
    /// <remarks>
    /// ⚠️ **All ten hexes of the original flat palette are still here**, each as its family's nuance. Every
    /// existing <c>ProcedureType</c> row and every <c>ProcedureTypeCatalogSeed</c> colour holds one of them, and
    /// retiring a hex would make those rows unloadable through this ctor and unsaveable through the form — the
    /// drift the served endpoint exists to prevent, in the one direction it cannot report.
    /// ⚠️ <c>Key</c> is an English slug and <c>LabelFr</c> its French name: the key is what a client groups on,
    /// the label is what it prints — the storage-key/display-map convention the rest of the product follows.
    /// The nuances stay inside a mid lightness band, because a hue here is spent as an agenda border, a dot and a
    /// badge outline; <c>#5EEAD4</c> is the one pale outlier and is kept only because it already ships.
    /// </remarks>
    private static readonly ColorFamily[] Palette =
    {
        new("blue", "Bleu", new ColorTone[]
            { new("#7FA6DC", Clair), new("#4F83CC", Moyen), new("#2F5FA3", Fonce) }),
        new("sky", "Ciel", new ColorTone[]
            { new("#8AB6F0", Clair), new("#60A5FA", Moyen), new("#2563EB", Fonce) }),
        new("indigo", "Indigo", new ColorTone[]
            { new("#9AA6F5", Clair), new("#6366F1", Moyen), new("#4338CA", Fonce) }),
        new("violet", "Violet", new ColorTone[]
            { new("#B7A6F7", Clair), new("#9B8EDC", Moyen), new("#6D48C7", Fonce) }),
        new("teal", "Sarcelle", new ColorTone[]
            { new("#5FBFB3", Clair), new("#2A9D8F", Moyen), new("#1B6F65", Fonce) }),
        new("mint", "Menthe", new ColorTone[]
            { new("#5EEAD4", Clair), new("#2DD4BF", Moyen), new("#0F9488", Fonce) }),
        new("green", "Vert", new ColorTone[]
            { new("#93C79C", Clair), new("#6BAA75", Moyen), new("#42804E", Fonce) }),
        new("olive", "Olive", new ColorTone[]
            { new("#A3C46A", Clair), new("#7E9E3C", Moyen), new("#5A7327", Fonce) }),
        new("amber", "Ambre", new ColorTone[]
            { new("#F3BE72", Clair), new("#E9A23B", Moyen), new("#B87613", Fonce) }),
        new("coral", "Corail", new ColorTone[]
            { new("#F0A090", Clair), new("#E76F51", Moyen), new("#B44A32", Fonce) }),
        new("rose", "Rose", new ColorTone[]
            { new("#F79AA6", Clair), new("#FB7185", Moyen), new("#BE3455", Fonce) }),
        new("slate", "Ardoise", new ColorTone[]
            { new("#9AA3AB", Clair), new("#6C757D", Moyen), new("#495057", Fonce) }),
    };

    private static readonly HashSet<string> ValidColors =
        new(Palette.SelectMany(f => f.Tones).Select(t => t.Hex), StringComparer.OrdinalIgnoreCase);

    public string Value { get; private set; }

    private ColorHex() { } // For EF Core

    public ColorHex(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Color hex value cannot be null or empty", nameof(value));

        // Remove # if present and normalize
        var normalized = value.Trim().ToUpperInvariant();
        if (!normalized.StartsWith("#"))
            normalized = "#" + normalized;

        // Validate format: #RRGGBB (6 hex digits)
        if (!System.Text.RegularExpressions.Regex.IsMatch(normalized, @"^#[0-9A-F]{6}$"))
            throw new ArgumentException($"Invalid HEX color format: {value}. Must be in format #RRGGBB", nameof(value));

        // Validate against curated palette
        if (!ValidColors.Contains(normalized))
            throw new ArgumentException($"Color {normalized} is not in the curated palette. Please select from available colors.", nameof(value));

        Value = normalized;
    }

    public static ColorHex FromString(string value) => new(value);

    public override string ToString() => Value;

    protected override IEnumerable<object> GetEqualityComponents()
    {
        yield return Value;
    }

    public static bool IsValid(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var normalized = value.Trim().ToUpperInvariant();
        if (!normalized.StartsWith("#"))
            normalized = "#" + normalized;

        return System.Text.RegularExpressions.Regex.IsMatch(normalized, @"^#[0-9A-F]{6}$") &&
               ValidColors.Contains(normalized);
    }

    /// <summary>The palette as the picker consumes it — hue families, each with its nuances.</summary>
    public static IReadOnlyList<ColorFamily> GetPalette() => Palette;

    /// <summary>
    /// Every accepted hex, flat, in **palette order** (hue then nuance) — never alphabetically, which is what
    /// this returned while a flat grid was the only consumer and which now scatters each family's nuances.
    /// </summary>
    public static IEnumerable<string> GetAvailableColors() => Palette.SelectMany(f => f.Tones).Select(t => t.Hex);
}
