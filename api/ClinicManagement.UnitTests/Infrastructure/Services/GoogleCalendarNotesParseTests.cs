using System.Reflection;
using ClinicManagement.Infrastructure.Services;
using Xunit;

namespace ClinicManagement.UnitTests.Infrastructure.Services;

/// <summary>
/// Hardening pass (§4 / AC-6) — the Google→App sync must parse back ONLY the user's notes from the
/// composite description block that App→Google writes (Doctor:/Notes:/Status:/Patient ID:). Assigning
/// the whole Description into appointment.Notes made the metadata block accumulate and nest on every
/// sync. The parser is a private static pure function on <see cref="GoogleCalendarSyncService"/>;
/// invoked here via reflection (no public seam, and standing up the full sync would need the Google
/// client + repositories).
/// </summary>
public class GoogleCalendarNotesParseTests
{
    private static string? Extract(string? description)
    {
        var method = typeof(GoogleCalendarSyncService).GetMethod(
            "ExtractNotesFromDescription",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);
        return (string?)method!.Invoke(null, new object?[] { description });
    }

    // The exact shape BuildAppointmentDescription emits.
    private static string Composite(string notes) =>
        $"Doctor: Dr House\nNotes: {notes}\nStatus: Scheduled\nPatient ID: {Guid.NewGuid()}";

    [Fact]
    public void Extract_Returns_Only_The_Notes_Line() // [AC-6]
    {
        var result = Extract(Composite("Patient a la grippe"));

        Assert.Equal("Patient a la grippe", result);
    }

    [Fact]
    public void Extract_Returns_Null_When_No_Notes_Marker() // [AC-6] leave existing notes untouched
    {
        var noNotes = "Doctor: Dr House\nStatus: Scheduled\nBusy Slot - No Patient";

        Assert.Null(Extract(noNotes));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Extract_Returns_Null_For_Empty_Description(string? description) // [AC-6]
    {
        Assert.Null(Extract(description));
    }

    // [AC-6] Repeated syncs must not accumulate/nest the metadata block: the parsed notes never contain
    // the Doctor:/Status:/Patient ID: markers, so re-building a description from them stays flat.
    [Fact]
    public void Extract_Does_Not_Leak_Metadata_Block()
    {
        var notes = Extract(Composite("Controle annuel"));

        Assert.Equal("Controle annuel", notes);
        Assert.DoesNotContain("Status:", notes);
        Assert.DoesNotContain("Doctor:", notes);
        Assert.DoesNotContain("Patient ID:", notes);

        // Round-trip: feeding the extracted notes back through a freshly built composite yields the same
        // value — proving no nesting builds up across syncs.
        Assert.Equal(notes, Extract(Composite(notes!)));
    }
}
