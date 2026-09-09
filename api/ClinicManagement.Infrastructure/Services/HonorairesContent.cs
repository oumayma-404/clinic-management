using System.Globalization;
using System.Text.Json;

namespace ClinicManagement.Infrastructure.Services;

/// <summary>One priced line of a note d'honoraires document, with its total already computed.</summary>
public sealed record HonorairesLine(string Designation, decimal Quantity, decimal UnitPrice, decimal Total);

/// <summary>The composed body: the priced lines, their sum, and the free note printed under them.</summary>
public sealed record HonorairesBody(IReadOnlyList<HonorairesLine> Lines, decimal Total, string? Note);

/// <summary>
/// Builds the body of a « note d'honoraires » <b>document</b> from its <c>ContentJson</c> — the sibling of
/// <see cref="PrescriptionContent"/> and <see cref="ExamenContent"/>, pure and deterministic so it is
/// unit-testable without rendering a PDF.
///
/// <para>
/// ⚠️ <b>This is a document, not an <c>Invoice</c>, and the distinction is the whole reason the type came
/// back.</b> Nothing here reaches the money path: it mints no number, it is not summed by « Solde patient »,
/// « Créances », la caisse or the dashboard, and saving one moves no figure anywhere in the product. It is a
/// sheet a practitioner types, prints and hands over — exactly like an ordonnance or a certificat. The fiscal
/// note d'honoraires, the one that carries a per-clinic-per-year number and enters every balance, is an
/// <c>Invoice</c> raised in the Factures module, and the two must never be conflated: that conflation is what
/// made the documents module a second, unwatched writer of the ledger.
/// </para>
///
/// <para>
/// ⚠️ <b>The arithmetic lives here and nowhere else on the server.</b> A line total is quantité × prix unitaire
/// and the document total is their sum — computed at render time from the stored quantities and prices rather
/// than read from a stored total, so a figure on the paper can never disagree with the lines above it.
/// </para>
/// </summary>
public static class HonorairesContent
{
    /// <summary>The <c>ContentJson</c> key holding the priced lines, as an array of <c>{ designation, quantity, unitPrice }</c>.</summary>
    public const string ActsKey = "acts";

    /// <summary>The <c>ContentJson</c> key holding the free note printed under the table.</summary>
    public const string NoteKey = "note";

    public static HonorairesBody Build(IReadOnlyDictionary<string, string> content)
    {
        ArgumentNullException.ThrowIfNull(content);

        var lines = ParseLines(content.GetValueOrDefault(ActsKey));
        var note = content.GetValueOrDefault(NoteKey)?.Trim();
        return new HonorairesBody(lines, lines.Sum(l => l.Total), string.IsNullOrEmpty(note) ? null : note);
    }

    /// <summary>
    /// Parses the acts blob. Malformed JSON yields no lines rather than throwing, for <see cref="ExamenContent"/>'s
    /// reason: a document that renders its header and signature with an empty table is recoverable, one that
    /// throws is a sheet the practitioner cannot get out of the app at all.
    /// </summary>
    private static IReadOnlyList<HonorairesLine> ParseLines(string? acts)
    {
        if (string.IsNullOrWhiteSpace(acts) || !acts.TrimStart().StartsWith('['))
        {
            return Array.Empty<HonorairesLine>();
        }

        List<ActEntry>? entries;
        try
        {
            entries = JsonSerializer.Deserialize<List<ActEntry>>(
                acts, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (JsonException)
        {
            return Array.Empty<HonorairesLine>();
        }

        if (entries == null)
        {
            return Array.Empty<HonorairesLine>();
        }

        var lines = new List<HonorairesLine>();
        foreach (var entry in entries)
        {
            var designation = entry.Designation?.Trim();
            if (string.IsNullOrEmpty(designation))
            {
                // A line with no désignation bills nothing and would print as an empty row with a price on it.
                continue;
            }

            // Quantité defaults to 1, not 0: an unparsable one must never silently zero a priced act.
            var quantity = ParseNumber(entry.Quantity) ?? 1m;
            var unitPrice = ParseNumber(entry.UnitPrice) ?? 0m;
            lines.Add(new HonorairesLine(designation, quantity, unitPrice, quantity * unitPrice));
        }

        return lines;
    }

    /// <summary>
    /// Reads a decimal the browser may have written three ways round: as a JSON number, as « 75.000 », or as the
    /// Tunisian « 75,000 » the app's own money inputs produce. A parse that accepts only one of them turns a real
    /// fee into 0 with nothing on screen saying so, which is the quietest way a printed sheet can lie.
    /// </summary>
    private static decimal? ParseNumber(JsonElement raw)
    {
        if (raw.ValueKind == JsonValueKind.Number)
        {
            return raw.GetDecimal();
        }

        if (raw.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var text = raw.GetString();
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        // Every space the French grouping may use is stripped — ordinary, no-break and narrow no-break alike.
        var normalised = new string(text.Where(c => !char.IsWhiteSpace(c)).ToArray()).Replace(",", ".");
        return decimal.TryParse(normalised, NumberStyles.Any, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
    }

    /// <summary>The wire shape of one entry inside the <c>acts</c> JSON array.</summary>
    private sealed class ActEntry
    {
        public string? Designation { get; set; }

        /// <summary>A <c>JsonElement</c>, not a string: the editor writes these as text and a caller may send numbers.</summary>
        public JsonElement Quantity { get; set; }

        /// <inheritdoc cref="Quantity"/>
        public JsonElement UnitPrice { get; set; }
    }
}
