using ClinicManagement.Application.Common.Csv;
using ClinicManagement.Application.Features.Patients;
using ClinicManagement.Application.Features.Patients.Import;

namespace ClinicManagement.UnitTests.Features.Patients;

/// <summary>
/// The CSV import's phone path (international-phone-numbers AC-20).
///
/// <para>⚠️ <b>This is the import's first test file.</b> <c>PatientImportRowReader</c>,
/// <c>PatientImportPlanner</c>, <c>PatientImportFields</c> and <c>PatientImportMapping</c> were covered by
/// nothing at all — so the errors-vs-warnings split, the E.164-on-the-way-in normalisation and the deliberate
/// emergency-phone leniency were four documented decisions with no guard, on the one surface where a mistake
/// costs 3 000 patient records at once.</para>
///
/// <para>Scoped to what this feature touched. The reader is pure and static — no repository, no clinic, no
/// DbContext — which is exactly the property its own docstring claims makes a test like this possible.</para>
/// </summary>
public class PatientImportRowReaderTests
{
    /// <summary>The narrowest mapping that produces a command: the two fields the planner requires.</summary>
    private static PatientImportRowRead ReadRow(string lastName, string firstName, string? phone = null, string? emergencyPhone = null)
    {
        var cells = new List<string> { lastName, firstName, phone ?? "", emergencyPhone ?? "" };
        var mapping = new Dictionary<PatientImportField, int>
        {
            [PatientImportField.LastName] = 0,
            [PatientImportField.FirstName] = 1,
            [PatientImportField.PhoneNumber] = 2,
            [PatientImportField.EmergencyContactPhone] = 3,
        };

        return PatientImportRowReader.Read(new CsvRow(2, cells), mapping);
    }

    [Theory]
    [InlineData("20123456", "+21620123456")]
    [InlineData("20 111 222", "+21620111222")]
    [InlineData("0021620123456", "+21620123456")]
    [InlineData("216-20-123-456", "+21620123456")]
    public void A_Tunisian_Number_Is_Normalised_On_The_Way_In(string raw, string expected)
    {
        var read = ReadRow("Ben Salah", "Amine", phone: raw);

        Assert.Empty(read.Errors);
        Assert.NotNull(read.Command);
        // ⚠️ The import stores the NORMALISED value, deliberately unlike the hand-typed path (whose ctor only
        // trims). A file arrives with eight spellings of one number, and storing them verbatim makes « do we
        // already have this patient? » unanswerable for ever.
        Assert.Equal(expected, read.Command!.PhoneNumber);
    }

    [Theory]
    [InlineData("+33 6 12 34 56 78", "+33612345678")]
    [InlineData("0033612345678", "+33612345678")]
    [InlineData("+213 551 234 567", "+213551234567")]
    public void A_Foreign_Number_Is_Accepted_And_Normalised(string raw, string expected)
    {
        var read = ReadRow("Dupont", "Claire", phone: raw);

        Assert.Empty(read.Errors);
        Assert.Equal(expected, read.Command!.PhoneNumber);
    }

    /// <summary>
    /// The case a length rule structurally cannot catch: nine digits where Tunisia has eight. Before the
    /// metadata library this row imported as a valid Egyptian number, and every reminder to it went nowhere.
    /// </summary>
    [Fact]
    public void A_Tunisian_Number_With_A_Ninth_Digit_Is_Refused()
    {
        var read = ReadRow("Ben Salah", "Amine", phone: "201234567");

        Assert.Null(read.Command);
        var error = Assert.Single(read.Errors);
        Assert.Equal(PhoneRefusals.InvalidRow("201234567"), error);
    }

    /// <summary>
    /// A bad phone refuses its own ROW and nothing else — one number in 3 000 must not refuse the file. The
    /// sentence is also the whole fix instruction: a rejected row has no in-app edit affordance, so the operator
    /// corrects the CSV and re-imports.
    /// </summary>
    [Fact]
    public void An_Unreadable_Number_Refuses_The_Row_With_The_Shared_Sentence()
    {
        var read = ReadRow("Trabelsi", "Nour", phone: "71 555 (bureau)");

        Assert.Null(read.Command);
        Assert.Equal(PhoneRefusals.InvalidRow("71 555 (bureau)"), Assert.Single(read.Errors));
        // The refusal names no country, so it stays true for every country the field now takes.
        Assert.DoesNotContain("tunisien", read.Errors[0], StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("8 chiffres", read.Errors[0], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void No_Phone_At_All_Is_Not_A_Refusal()
    {
        var read = ReadRow("Ben Salah", "Amine");

        Assert.Empty(read.Errors);
        Assert.NotNull(read.Command);
    }

    /// <summary>
    /// ⚠️ The asymmetry the whole emergency-contact decision rests on (AC-14): an unreadable relative's number
    /// is a <b>warning</b> and is imported as typed. Nothing dispatches to it — a human reads it in an emergency
    /// — so refusing the row would lose the patient's record to protect a field nobody sends to.
    /// </summary>
    [Fact]
    public void An_Unreadable_Emergency_Number_Warns_And_Is_Kept_Verbatim()
    {
        var read = ReadRow("Ben Salah", "Amine", emergencyPhone: "71 555 (bureau)");

        Assert.Empty(read.Errors);
        Assert.NotNull(read.Command);
        Assert.Equal("71 555 (bureau)", read.Command!.EmergencyContactPhone);
        Assert.Equal(PhoneRefusals.EmergencyRowNotRecognised("71 555 (bureau)"), Assert.Single(read.Warnings));
    }

    [Fact]
    public void A_Readable_Emergency_Number_Is_Normalised_And_Warns_About_Nothing()
    {
        var read = ReadRow("Ben Salah", "Amine", emergencyPhone: "+33 6 12 34 56 78");

        Assert.Empty(read.Errors);
        Assert.Empty(read.Warnings);
        Assert.Equal("+33612345678", read.Command!.EmergencyContactPhone);
    }

    /// <summary>
    /// The two phones are graded differently on the SAME row — the property a single shared rule would lose.
    /// </summary>
    [Fact]
    public void The_Two_Phones_Are_Graded_Independently()
    {
        var read = ReadRow("Ben Salah", "Amine", phone: "201234567", emergencyPhone: "71 555 (bureau)");

        Assert.Null(read.Command);
        Assert.Single(read.Errors);   // the patient's own number
        Assert.Single(read.Warnings); // the relative's
    }

    /// <summary>
    /// An unmapped phone column is « not supplied », never « empty and therefore wrong ».
    /// </summary>
    [Fact]
    public void An_Unmapped_Phone_Column_Is_Not_Read()
    {
        var mapping = new Dictionary<PatientImportField, int>
        {
            [PatientImportField.LastName] = 0,
            [PatientImportField.FirstName] = 1,
        };

        var read = PatientImportRowReader.Read(
            new CsvRow(2, new List<string> { "Ben Salah", "Amine", "201234567" }),
            mapping);

        Assert.Empty(read.Errors);
        Assert.NotNull(read.Command);
    }
}
