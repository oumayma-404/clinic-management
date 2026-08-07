using System.Text;

namespace ClinicManagement.Application.Common.Csv;

/// <summary>One parsed row, carrying the <b>file line</b> it came from so a refusal can name it.</summary>
/// <param name="LineNumber">
/// 1-based line in the file, header included — so the first data row is line 2, which is what the user sees in
/// Excel's own gutter. A row index would be a different number from the one they are looking at, and « ligne 47 »
/// is the only way an import report is actionable on a 3 000-row file.
/// </param>
public sealed record CsvRow(int LineNumber, IReadOnlyList<string> Cells)
{
    /// <summary>
    /// The cell at <paramref name="index"/>, or <c>""</c> when the row is short. A short row is <b>not</b> an
    /// error here: spreadsheets routinely omit trailing empty cells, and refusing the row would reject a file
    /// whose last three columns are simply blank.
    /// </summary>
    public string Cell(int index) =>
        index >= 0 && index < Cells.Count ? Cells[index] : string.Empty;
}

/// <summary>Headers, rows, and what the reader decided about the file's shape.</summary>
/// <param name="Truncated">
/// True when the file held more rows than <see cref="CsvReader.MaxRows"/>. Surfaced rather than silently obeyed —
/// an import that quietly stops at row 5 000 of 8 000 is the « no silent caps » rule in its most expensive form,
/// since the operator would believe they had migrated their practice.
/// </param>
public sealed record CsvDocument(
    IReadOnlyList<string> Headers,
    IReadOnlyList<CsvRow> Rows,
    char Delimiter,
    string Encoding,
    bool Truncated);

/// <summary>
/// Reads a CSV the way a Tunisian clinic's spreadsheet actually arrives (L5, import half) — the counterpart to
/// <see cref="CsvTable"/>, and deliberately in the same folder so the two halves of the round trip are read together.
///
/// <para><b>Nothing here is symmetrical with the writer, and that is the point.</b> The writer produces exactly one
/// shape (UTF-8 + BOM, <c>;</c>, CRLF) because it controls the file. The reader is handed whatever the previous
/// practice-management software, or the receptionist's own Excel, produced — so three things are <b>detected</b>
/// rather than assumed:</para>
/// <list type="number">
///   <item><b>The delimiter.</b> <c>;</c> is what this product writes and what a French Windows Excel writes, but
///     « CSV (comma delimited) » and a tab-separated paste are both common. Guessing wrong produces a single
///     column, which reads to the user as « the file is empty ».</item>
///   <item><b>The encoding.</b> A BOM-less file saved by Excel on a French Windows is <b>cp1252</b>, not UTF-8, so
///     decoding it as UTF-8 fails on the first « é ». Invalid UTF-8 therefore falls back to Latin-1 — the mirror
///     image of the writer's BOM decision, and for the same reason: every name in this product can carry an accent.
///     ⚠️ Latin-1 rather than cp1252 because .NET Core ships Latin-1 and needs a package for 1252; they differ only
///     in <c>0x80–0x9F</c> (typographic quotes, €), none of which is a French letter.</item>
///   <item><b>The line ending.</b> CRLF, LF and a lone CR all end a record.</item>
/// </list>
///
/// <para>Quoting is RFC 4180: a quoted field may contain the delimiter, a newline, and <c>""</c> for a literal
/// quote. That matters for real data — « Rue de la Liberté, imm. 3 » in an address column is one field.</para>
/// </summary>
public static class CsvReader
{
    /// <summary>
    /// The most rows one import may carry. The spec's motivating case is « a dentist arriving with 3 000
    /// patients », so the cap is comfortably above it while still bounding the work a single request can ask for.
    /// Exceeding it is <b>reported</b> (<see cref="CsvDocument.Truncated"/>), never silently applied.
    /// </summary>
    public const int MaxRows = 5000;

    private static readonly char[] CandidateDelimiters = { ';', ',', '\t' };

    /// <summary>
    /// Parses the uploaded bytes. Throws <see cref="InvalidOperationException"/> with a <b>French</b> message when
    /// the file has no header row — the one failure the caller must surface rather than treat as zero rows, since
    /// « 0 patients » and « this is not a CSV » lead to completely different next actions.
    /// </summary>
    public static CsvDocument Read(byte[] bytes)
    {
        var (text, encoding) = Decode(bytes);

        // A BOM survives decoding as U+FEFF and would otherwise become part of the first header's name, so the
        // first column of our own export would never match its own auto-detection.
        text = text.TrimStart('﻿');

        if (string.IsNullOrWhiteSpace(text))
        {
            throw new InvalidOperationException("Le fichier est vide.");
        }

        var delimiter = DetectDelimiter(text);
        var records = ParseRecords(text, delimiter);

        if (records.Count == 0)
        {
            throw new InvalidOperationException("Le fichier ne contient aucune ligne d'en-tête.");
        }

        var headers = records[0].Cells.Select(h => h.Trim()).ToList();
        if (headers.All(string.IsNullOrWhiteSpace))
        {
            throw new InvalidOperationException(
                "La première ligne du fichier doit contenir les noms des colonnes (Nom, Prénom, …).");
        }

        // A row that is entirely empty is dropped rather than reported: a trailing blank line is what almost every
        // spreadsheet writes, and reporting it as « ligne 3001 invalide » on every single import would train the
        // operator to ignore the report.
        var dataRows = records.Skip(1)
            .Where(r => r.Cells.Any(c => !string.IsNullOrWhiteSpace(c)))
            .ToList();

        var truncated = dataRows.Count > MaxRows;

        return new CsvDocument(
            headers,
            truncated ? dataRows.Take(MaxRows).ToList() : dataRows,
            delimiter,
            encoding,
            truncated);
    }

    private static (string Text, string Encoding) Decode(byte[] bytes)
    {
        try
        {
            // `throwOnInvalidBytes` is the whole mechanism: it is what distinguishes a genuine UTF-8 file from a
            // cp1252 one. Without it, .NET substitutes U+FFFD and « Béchir » silently becomes « B?chir ».
            var strict = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
            return (strict.GetString(bytes), "UTF-8");
        }
        catch (DecoderFallbackException)
        {
            return (System.Text.Encoding.Latin1.GetString(bytes), "Windows-1252 / Latin-1");
        }
    }

    /// <summary>
    /// Picks the delimiter by counting candidates on the <b>header record only</b>, outside quotes.
    ///
    /// <para>The header is the right sample precisely because it is the one line guaranteed to hold every column:
    /// counting over the whole file would let a single address containing « , » outvote the real delimiter. Ties go
    /// to <c>;</c> — what this product writes, so a re-import of its own export never depends on the guess.</para>
    /// </summary>
    private static char DetectDelimiter(string text)
    {
        var headerLine = FirstRecordSample(text);

        var best = ';';
        var bestCount = 0;
        foreach (var candidate in CandidateDelimiters)
        {
            var count = CountOutsideQuotes(headerLine, candidate);
            if (count > bestCount)
            {
                best = candidate;
                bestCount = count;
            }
        }

        return best;
    }

    /// <summary>
    /// The first record's text, stopping at the first line break that is not inside quotes. ⚠️ Not
    /// <c>text.Split('\n')[0]</c>: a quoted header cell may legally contain a newline, and cutting there would
    /// sample half a record and count the wrong delimiter for the whole file.
    /// </summary>
    private static string FirstRecordSample(string text)
    {
        var inQuotes = false;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c == '"')
            {
                inQuotes = !inQuotes;
            }
            else if (!inQuotes && (c == '\n' || c == '\r'))
            {
                return text[..i];
            }
        }

        return text;
    }

    private static int CountOutsideQuotes(string line, char target)
    {
        var inQuotes = false;
        var count = 0;
        foreach (var c in line)
        {
            if (c == '"')
            {
                inQuotes = !inQuotes;
            }
            else if (!inQuotes && c == target)
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>
    /// The RFC 4180 state machine. One pass, tracking the physical line so every row can name where it came from.
    /// </summary>
    private static List<CsvRow> ParseRecords(string text, char delimiter)
    {
        var records = new List<CsvRow>();
        var cells = new List<string>();
        var cell = new StringBuilder();
        var inQuotes = false;
        var line = 1;
        var recordStartLine = 1;

        void EndCell()
        {
            cells.Add(cell.ToString());
            cell.Clear();
        }

        void EndRecord()
        {
            EndCell();
            records.Add(new CsvRow(recordStartLine, cells.ToList()));
            cells.Clear();
        }

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];

            if (inQuotes)
            {
                if (c == '"')
                {
                    // A doubled quote is a literal one; a single quote closes the field.
                    if (i + 1 < text.Length && text[i + 1] == '"')
                    {
                        cell.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    if (c == '\n')
                    {
                        line++;
                    }

                    cell.Append(c);
                }

                continue;
            }

            if (c == '"' && cell.Length == 0)
            {
                // Only an opening quote at the start of a field opens a quoted field. A quote appearing mid-field
                // is data — « 5" » in a note — and treating it as a delimiter would swallow the rest of the file.
                inQuotes = true;
            }
            else if (c == delimiter)
            {
                EndCell();
            }
            else if (c == '\r' || c == '\n')
            {
                // CRLF is one ending, not two empty records.
                if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
                {
                    i++;
                }

                EndRecord();
                line++;
                recordStartLine = line;
            }
            else
            {
                cell.Append(c);
            }
        }

        // A file not ending in a newline still has a final record.
        if (cell.Length > 0 || cells.Count > 0)
        {
            EndRecord();
        }

        return records;
    }
}
