using ClinicManagement.Domain.ValueObjects;

namespace ClinicManagement.UnitTests.Domain;

/// <summary>
/// The agenda-colour palette went from a flat ten hexes to twelve hue families × three nuances, because an act
/// catalogue outgrows ten colours long before it outgrows twelve hues.
///
/// <para>
/// These hold the two properties the expansion could break quietly. **No existing act loses its colour** — every
/// <c>ProcedureType</c> row and every <c>ProcedureTypeCatalogSeed</c> entry holds one of the original ten, so
/// retiring one would make those rows unloadable through the ctor and unsaveable through the form, and nothing
/// else in this suite would see it. And **the grouped view and the flat allow-list stay one set** — the picker
/// offers what the grouping contains while <see cref="ColorHex"/> validates against the flattening of it, so a
/// family the flattening missed would be offered and then refused on save.
/// </para>
/// </summary>
public class ColorHexTests
{
    /// <summary>The pre-expansion palette, verbatim. Kept as literals on purpose: derived from the new palette it
    /// would assert nothing.</summary>
    public static TheoryData<string> LegacyPalette() => new()
    {
        "#4F83CC", "#2A9D8F", "#6BAA75", "#9B8EDC", "#E9A23B",
        "#E76F51", "#6C757D", "#60A5FA", "#5EEAD4", "#FB7185",
    };

    [Theory]
    [MemberData(nameof(LegacyPalette))]
    public void A_Colour_From_The_Original_Palette_Is_Still_Accepted(string hex)
    {
        Assert.True(ColorHex.IsValid(hex));
        Assert.Equal(hex.ToUpperInvariant(), ColorHex.FromString(hex).Value);
    }

    [Fact]
    public void The_Grouped_Palette_And_The_Flat_List_Are_The_Same_Set()
    {
        var grouped = ColorHex.GetPalette().SelectMany(f => f.Tones).Select(t => t.Hex).ToList();

        Assert.Equal(ColorHex.GetAvailableColors(), grouped);
    }

    /// <summary>
    /// A hex repeated across two families makes the picker's active family ambiguous — the nuance strip would open
    /// on whichever family the lookup happened to reach first. The count assertion is there so « found nothing »
    /// cannot read as « nothing was wrong »: a palette that failed to build would satisfy every uniqueness check.
    /// </summary>
    [Fact]
    public void No_Hex_Appears_Twice_And_Every_Family_Is_Named_Once()
    {
        var palette = ColorHex.GetPalette();
        var hexes = palette.SelectMany(f => f.Tones).Select(t => t.Hex).ToList();

        Assert.True(palette.Count >= 12, $"the palette should offer at least twelve hues, found {palette.Count}");
        Assert.True(hexes.Count >= 30, $"the palette should offer at least thirty colours, found {hexes.Count}");
        Assert.Equal(hexes.Count, hexes.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal(palette.Count, palette.Select(f => f.Key).Distinct().Count());
        Assert.Equal(palette.Count, palette.Select(f => f.LabelFr).Distinct().Count());
    }

    /// <summary>
    /// The picker falls back to a family's first nuance, so this is not load-bearing for correctness — but a family
    /// with no « Moyen » would represent itself in the swatch row with a pale or a very dark tone.
    /// </summary>
    [Fact]
    public void Every_Family_Offers_A_Mid_Tone()
    {
        Assert.All(ColorHex.GetPalette(), family => Assert.Contains(family.Tones, t => t.ToneFr == "Moyen"));
    }

    /// <summary>Every hex is `#RRGGBB` uppercase, which is what the ctor normalises to and the browser is handed.</summary>
    [Fact]
    public void Every_Hex_Is_Stored_In_The_Normalised_Form()
    {
        Assert.All(
            ColorHex.GetPalette().SelectMany(f => f.Tones),
            tone => Assert.Equal(tone.Hex, ColorHex.FromString(tone.Hex).Value));
    }

    [Fact]
    public void A_Colour_Outside_The_Palette_Is_Still_Refused()
    {
        Assert.False(ColorHex.IsValid("#123456"));
        Assert.Throws<ArgumentException>(() => ColorHex.FromString("#123456"));
    }
}
