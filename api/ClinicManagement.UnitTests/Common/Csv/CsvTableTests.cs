using System.Text;
using ClinicManagement.Application.Common;
using ClinicManagement.Application.Common.Csv;

namespace ClinicManagement.UnitTests.Common.Csv;

/// <summary>
/// The CSV writer (L5) — the only part of the export path that can be wrong in a way no other test would see.
///
/// <para>The endpoints themselves are thin: they re-send the screen's own query with no paging and hand the rows
/// here, so « does the export honour the filters » is a property of the query, already covered. What is *only*
/// testable here is the file's shape — and each assertion below stands for a specific way a French clinic's
/// spreadsheet renders an unusable file.</para>
/// </summary>
public class CsvTableTests
{
    private static string Render(CsvTable table) =>
        new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetString(StripBom(table.ToBytes()));

    private static byte[] StripBom(byte[] bytes) =>
        bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF
            ? bytes[3..]
            : bytes;

    // Excel on Windows reads a BOM-less UTF-8 file in the system codepage, which turns « Béchir » into
    // « BÃ©chir » — in a product whose every column heading is French. The BOM is not decoration.
    [Fact]
    public void The_File_Starts_With_A_Utf8_Bom()
    {
        var bytes = CsvTable.Create("Nom").Row("Béchir").ToBytes();

        Assert.True(bytes.Length >= 3);
        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes[..3]);
    }

    // The delimiter is a SEMICOLON. Excel's list separator follows the Windows locale, and in fr-TN/fr-FR that is
    // `;` — a comma-delimited file opens as one column per row, which is the whole file unusable.
    [Fact]
    public void Columns_Are_Separated_By_Semicolons_And_Rows_By_CrLf()
    {
        var csv = Render(CsvTable.Create("Nom", "Prénom").Row("Ben Salah", "Amine"));

        Assert.Equal("Nom;Prénom\r\nBen Salah;Amine\r\n", csv);
    }

    /*
     * Quoting. Each of these is a real cell this product produces: a clinical note containing a semicolon or a
     * line break, an address with a quote, a phone number typed with a trailing space.
     */

    [Theory]
    [InlineData("Carie; à revoir", "\"Carie; à revoir\"")]
    [InlineData("Il a dit \"non\"", "\"Il a dit \"\"non\"\"\"")]
    [InlineData("ligne 1\nligne 2", "\"ligne 1\nligne 2\"")]
    // Leading/trailing whitespace is quoted too: a spreadsheet silently trims it otherwise, so a phone number
    // typed with a trailing space would round-trip differently from the stored value.
    [InlineData(" 20123456 ", "\" 20123456 \"")]
    [InlineData("Ben Salah", "Ben Salah")]
    public void A_Cell_Is_Quoted_Only_When_It_Has_To_Be(string value, string expected)
    {
        var csv = Render(CsvTable.Create("Note").Row(value));

        Assert.Equal($"Note\r\n{expected}\r\n", csv);
    }

    [Fact]
    public void A_Null_Or_Empty_Cell_Is_Written_As_Nothing()
    {
        // Never « — » and never a sentinel: contact details are genuinely nullable, and a placeholder re-imports
        // as data. The four contact sentinels were retired for exactly that reason.
        var csv = Render(CsvTable.Create("Email", "Téléphone").Row(null, string.Empty));

        Assert.Equal("Email;Téléphone\r\n;\r\n", csv);
    }

    // A cell-count mismatch is a programming error that would otherwise produce a file whose columns silently
    // shift from one row onward — far worse to diagnose than an exception at the call site.
    [Fact]
    public void A_Row_With_The_Wrong_Number_Of_Cells_Is_Refused()
    {
        var table = CsvTable.Create("A", "B");

        Assert.Throws<ArgumentException>(() => table.Row("only one"));
    }

    /*
     * Money. « Never hand-format a dinar » (audit § 8.5): three decimals and a comma.
     */

    [Theory]
    [InlineData(0, "0,000")]
    [InlineData(90, "90,000")]
    [InlineData(1234.5, "1234,500")]
    // Rounded through InvoiceCalculator.RoundMoney — away from zero at the millime, the solution's single
    // rounding authority — so an exported figure equals the screen it came from.
    [InlineData(1.2345, "1,235")]
    [InlineData(1.2344, "1,234")]
    public void Money_Is_Three_Decimals_With_A_Comma(decimal amount, string expected)
    {
        Assert.Equal(expected, CsvCell.Money(amount));
    }

    // No thousands separator, deliberately: a space (or a non-breaking space) is what makes a spreadsheet read
    // the cell as TEXT and refuse to sum the column — which is the entire reason an accountant asked for the file.
    [Fact]
    public void Money_Carries_No_Thousands_Separator()
    {
        var cell = CsvCell.Money(1_234_567.891m);

        Assert.Equal("1234567,891", cell);
        Assert.DoesNotContain(" ", cell);
        Assert.DoesNotContain(" ", cell);
    }

    [Fact]
    public void A_Null_Amount_Is_An_Empty_Cell_Not_A_Zero()
    {
        // « aucun coût enregistré » and « 0,000 DT » are different claims — the same distinction the CNAM
        // estimate makes between a null and a zero reimbursement.
        Assert.Equal(string.Empty, CsvCell.Money((decimal?)null));
    }

    /*
     * Dates. An instant is clinic-local; a calendar day is not converted at all.
     */

    // Tunisia is UTC+1, so 23:30 UTC on the 3rd is already the 4th at the clinic. A raw UTC format would file a
    // payment recorded at 00:30 local under the previous day — finding #20, in the one artefact an accountant
    // reconciles against a bank statement.
    [Fact]
    public void An_Instant_Is_Written_In_The_Clinics_Own_Day()
    {
        var lateEvening = new DateTime(2026, 3, 3, 23, 30, 0, DateTimeKind.Utc);

        Assert.Equal("04/03/2026", CsvCell.Date(lateEvening));
        Assert.Equal(4, ClinicClock.ToClinicLocal(lateEvening).Day);
    }

    // A date of birth or an échéance is already a calendar day. Converting one would move it by a day for half
    // the values, which for a date of birth is simply a wrong date.
    [Fact]
    public void A_Calendar_Day_Is_Not_Converted()
    {
        Assert.Equal("03/03/2026", CsvCell.CalendarDay(new DateTime(2026, 3, 3, 0, 0, 0, DateTimeKind.Utc)));
    }

    [Fact]
    public void A_Boolean_Reads_In_French()
    {
        Assert.Equal("Oui", CsvCell.YesNo(true));
        Assert.Equal("Non", CsvCell.YesNo(false));
    }
}
