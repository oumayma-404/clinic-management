using ClinicManagement.Domain.Common;

namespace ClinicManagement.Domain.ValueObjects;

public class ColorHex : ValueObject
{
    // Curated palette of valid HEX colors - must match frontend COLOR_PALETTE
    private static readonly HashSet<string> ValidColors = new(StringComparer.OrdinalIgnoreCase)
    {
        "#4F83CC", // Soft Blue
        "#2A9D8F", // Teal
        "#6BAA75", // Muted Green
        "#9B8EDC", // Lavender
        "#E9A23B", // Warm Amber
        "#E76F51", // Coral
        "#6C757D", // Slate
        "#60A5FA", // Sky Blue
        "#5EEAD4", // Mint
        "#FB7185"  // Rose
    };

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

    public static IEnumerable<string> GetAvailableColors() => ValidColors.OrderBy(c => c);
}

