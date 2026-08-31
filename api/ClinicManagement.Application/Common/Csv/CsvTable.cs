using System.Globalization;
using System.Text;
using ClinicManagement.Domain.Services;

namespace ClinicManagement.Application.Common.Csv;

/// <summary>
/// The single CSV authority (L5). Every « Exporter » in the product builds one of these.
///
/// <para><b>Why the product needs it at all.</b> Before L5 there were zero occurrences of <c>csv</c> /
/// <c>xlsx</c> / <c>excel</c> anywhere in the repo and zero of « Exporter » in <c>web/</c>. The only way data
/// left the product was a <c>pg_dump</c>, which is readable by PostgreSQL tooling and nothing else — so the owner
/// could not leave with their own data in usable form, and could not hand their accountant anything.</para>
///
/// <para><b>Three decisions that make the file open correctly on a Tunisian clinic's PC</b>, all of which are
/// the difference between « it works » and « every accent is mojibake and every column is in one cell »:</para>
/// <list type="number">
///   <item><b>UTF-8 <i>with</i> a BOM.</b> Excel on Windows reads a BOM-less UTF-8 file in the system codepage,
///     which turns « Béchir » into « BÃ©chir » — in a product whose every label is French.</item>
///   <item><b><c>;</c> as the delimiter, not <c>,</c>.</b> Excel's list separator follows the Windows locale, and
///     in fr-TN/fr-FR that is a semicolon. A comma-delimited file opens as one column per row.</item>
///   <item><b>CRLF line endings</b>, for the same reason: it is the line ending Excel and Notepad expect.</item>
/// </list>
///
/// <para>⚠️ <b>Money and dates go through <see cref="CsvCell"/>, never through <c>ToString</c>.</b> A dinar has
/// three decimals and a comma — <c>toFixed(2)</c> drops the millime and a period is not the separator the rest of
/// the product prints. Because the delimiter is <c>;</c>, a decimal comma needs no quoting, which is the second
/// reason for that choice.</para>
/// </summary>
public sealed class CsvTable
{
    private const char Delimiter = ';';
    private const string LineEnding = "\r\n";

    private readonly IReadOnlyList<string> _headers;
    private readonly List<string?[]> _rows = new();

    private CsvTable(IReadOnlyList<string> headers)
    {
        _headers = headers;
    }

    /// <summary>Starts a table with its French column headings, in the order they will be written.</summary>
    public static CsvTable Create(params string[] headers) => new(headers);

    /// <summary>
    /// Appends a row. The cell count must match the header count — a mismatch is a programming error that would
    /// otherwise produce a file whose columns silently shift from one row onward, which is far worse to debug
    /// than an exception at the call site.
    /// </summary>
    public CsvTable Row(params string?[] cells)
    {
        if (cells.Length != _headers.Count)
        {
            throw new ArgumentException(
                $"CSV row has {cells.Length} cell(s) but the table declares {_headers.Count} column(s).",
                nameof(cells));
        }

        _rows.Add(cells);
        return this;
    }

    public int RowCount => _rows.Count;

    /// <summary>The file, ready to be returned as a download.</summary>
    public byte[] ToBytes()
    {
        var builder = new StringBuilder();
        AppendRow(builder, _headers.Select(h => (string?)h).ToArray());
        foreach (var row in _rows)
        {
            AppendRow(builder, row);
        }

        // ⚠️ The BOM must be written EXPLICITLY. `new UTF8Encoding(encoderShouldEmitUTF8Identifier: true)` only
        // changes what `GetPreamble()` returns — `GetBytes` never emits it — so the obvious spelling produces a
        // BOM-less file that Excel then reads in the system codepage, turning « Béchir » into « BÃ©chir ». That is
        // exactly what `CsvTableTests.The_File_Starts_With_A_Utf8_Bom` caught, and it is invisible to every other
        // kind of check: the file is valid UTF-8 and opens fine in anything that is not Excel.
        var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);
        var preamble = encoding.GetPreamble();
        var body = encoding.GetBytes(builder.ToString());

        var bytes = new byte[preamble.Length + body.Length];
        preamble.CopyTo(bytes, 0);
        body.CopyTo(bytes, preamble.Length);
        return bytes;
    }

    private static void AppendRow(StringBuilder builder, string?[] cells)
    {
        for (var i = 0; i < cells.Length; i++)
        {
            if (i > 0)
            {
                builder.Append(Delimiter);
            }

            builder.Append(Escape(cells[i]));
        }

        builder.Append(LineEnding);
    }

    /// <summary>
    /// RFC 4180 quoting, applied only when needed: a cell containing the delimiter, a quote, a newline — or
    /// <b>leading/trailing whitespace</b>, which a spreadsheet otherwise silently trims (a phone number typed
    /// with a trailing space would round-trip differently from the stored value).
    ///
    /// <para>⚠️ <b>Quoting is not protection against a formula, which is why <see cref="Neutralise"/> runs
    /// first.</b> Excel strips the quotes on import and still evaluates what is inside, so a patient named
    /// <c>=WEBSERVICE(…)</c> executes in the file the cabinet hands its accountant. Every export in the product
    /// funnels through here, so this is the one place it can be stopped.</para>
    /// </summary>
    private static string Escape(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        value = Neutralise(value);

        var needsQuotes = value.Contains(Delimiter)
                          || value.Contains('"')
                          || value.Contains('\n')
                          || value.Contains('\r')
                          || value != value.Trim();

        return needsQuotes ? '"' + value.Replace("\"", "\"\"") + '"' : value;
    }

    /// <summary>
    /// The characters a spreadsheet reads as « this cell is code »: <c>=</c> opens a formula, <c>+</c> and
    /// <c>@</c> are accepted as formula leaders by Excel, and a leading TAB or CR shifts what the parser sees
    /// so the next character becomes the leader instead.
    /// </summary>
    private const string FormulaLeaders = "=+@\t\r";

    /// <summary>Same culture <see cref="CsvCell.Money"/> writes with — see <see cref="Neutralise"/>.</summary>
    private static readonly CultureInfo CellCulture = CultureInfo.GetCultureInfo("fr-FR");

    /// <summary>
    /// Prefixes a cell that a spreadsheet would execute with an apostrophe, which marks it as text.
    ///
    /// <para>⚠️ <b><c>-</c> is deliberately NOT neutralised unconditionally</b>, because <see cref="CsvCell.Money"/>
    /// writes a negative dinar as <c>-1234,500</c> and the whole reason the accountant asked for this file is
    /// that the column sums. So a leading <c>-</c> is left alone when the rest of the cell parses as a number in
    /// the culture the file is written in, and neutralised when it does not — <c>-2+3</c> and
    /// <c>-cmd|'/c calc'!A1</c> are not numbers and do not survive.</para>
    ///
    /// <para>The cost is cosmetic and paid only by the pathological cell: Excel imports the apostrophe literally
    /// from a CSV rather than consuming it as the text marker it is during typing, so such a value displays with
    /// a leading <c>'</c>. A name that genuinely starts with <c>=</c> reading slightly wrong is the right trade
    /// against the same name executing.</para>
    /// </summary>
    private static string Neutralise(string value)
    {
        var leader = value[0];

        if (FormulaLeaders.Contains(leader, StringComparison.Ordinal))
        {
            return "'" + value;
        }

        if (leader == '-' && !decimal.TryParse(value, NumberStyles.Number, CellCulture, out _))
        {
            return "'" + value;
        }

        return value;
    }
}

/// <summary>
/// How each kind of value is written into a cell. One place, so an export cannot invent its own money format —
/// the § 8.5 defect (« never hand-format a dinar ») in a new file.
/// </summary>
public static class CsvCell
{
    private static readonly CultureInfo FrCulture = CultureInfo.GetCultureInfo("fr-FR");

    /// <summary>
    /// A dinar amount: three decimals, a comma, no thousands separator.
    ///
    /// <para>Rounded through <see cref="InvoiceCalculator.RoundMoney"/> — the solution's single rounding
    /// authority — so an exported figure equals the one on the screen it came from to the millime. No thousands
    /// separator, deliberately: a space or a non-breaking space is what makes a spreadsheet read the cell as
    /// <i>text</i> and refuse to sum the column, which is the entire reason an accountant asked for the file.</para>
    /// </summary>
    public static string Money(decimal amount) =>
        InvoiceCalculator.RoundMoney(amount).ToString("0.000", FrCulture);

    public static string Money(decimal? amount) => amount.HasValue ? Money(amount.Value) : string.Empty;

    /// <summary>
    /// A calendar day, French order. ⚠️ Converted through <see cref="ClinicClock"/> first: the instant is stored
    /// UTC and Tunisia is UTC+1, so a payment recorded at 00:30 local would export under the previous day —
    /// finding #20 in a new file, and in the one artefact an accountant reconciles against a bank statement.
    /// </summary>
    public static string Date(DateTime value) =>
        ClinicClock.ToClinicLocal(value).ToString("dd/MM/yyyy", FrCulture);

    public static string Date(DateTime? value) => value.HasValue ? Date(value.Value) : string.Empty;

    /// <summary>A day and time, clinic-local — for an appointment or a movement, where the hour matters.</summary>
    public static string Moment(DateTime value) =>
        ClinicClock.ToClinicLocal(value).ToString("dd/MM/yyyy HH:mm", FrCulture);

    public static string Moment(DateTime? value) => value.HasValue ? Moment(value.Value) : string.Empty;

    /// <summary>
    /// A bare date already held as a calendar day (a date of birth, an échéance), with <b>no</b> zone
    /// conversion — converting one would move it by a day for half the values, which for a date of birth is
    /// simply a wrong date.
    /// </summary>
    public static string CalendarDay(DateTime value) => value.ToString("dd/MM/yyyy", FrCulture);

    public static string CalendarDay(DateTime? value) => value.HasValue ? CalendarDay(value.Value) : string.Empty;

    public static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);

    public static string Number(int? value) => value.HasValue ? Number(value.Value) : string.Empty;

    /// <summary>« Oui » / « Non ». A French file for French readers; `True`/`False` is not a translation.</summary>
    public static string YesNo(bool value) => value ? "Oui" : "Non";

    /// <summary>
    /// Free text. Newlines are kept (the quoting handles them) rather than stripped: a clinical note's line
    /// breaks are part of what it says, and a spreadsheet renders them fine inside a quoted cell.
    /// </summary>
    public static string Text(string? value) => value ?? string.Empty;
}
