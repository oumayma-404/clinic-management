using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Infrastructure.Services;
using Xunit;

namespace ClinicManagement.UnitTests.Infrastructure.Services;

/// <summary>
/// <b>This product never deletes an event from a practice's Google calendar.</b> The calendar belongs to the
/// cabinet; the app may add to it and correct what it added, and that is all.
///
/// <para><b>What it used to do.</b> <c>GoogleCalendarSyncService</c> called <c>DeleteEventAsync</c> whenever an
/// appointment became <c>Cancelled</c> <b>or</b> <c>Completed</c>. « Terminé » is the most ordinary action in the
/// product — « À clôturer » asks for it on every visit and <c>AppointmentProgressJob</c> reaches the same path — so
/// every appointment a cabinet actually honoured was erased from its own Google agenda and the event id nulled,
/// with nothing anywhere saying so. The day the practice had worked came out **emptier** than the day it had not.
/// The same call on cancellation is how a cabinet tidying up an unwanted import destroyed a hundred real entries of
/// its own; that loss is permanent and no undo in this product can reach it.</para>
///
/// <para>⚠️ <b>The fix was to remove the capability, not to narrow its call sites</b> — so the guard is mostly
/// about the capability staying absent. A condition can be widened back in one character; a method that does not
/// exist is a compile error. <c>IGoogleCalendarService.DeleteEventAsync</c> and its implementation are gone.</para>
///
/// <para>Asserted three ways, because each catches a different way it could come back: reflection over the
/// <b>contract</b> (somebody re-declares it), reflection over the <b>client</b> (somebody adds it to the concrete
/// class only), and a <b>source scan</b> for the Google SDK's own delete (somebody inlines
/// <c>service.Events.Delete(...)</c> without naming a method at all — which reflection cannot see).</para>
/// </summary>
public class GoogleCalendarNeverDeletesTests
{
    /// <summary>
    /// The contract exposes no way to delete. Asserted on the whole member set rather than on one name, so
    /// « RemoveEvent », « CancelEvent » or a batch variant is caught too.
    /// </summary>
    [Fact]
    public void The_Google_Contract_Exposes_No_Way_To_Delete_An_Event()
    {
        var deleting = typeof(IGoogleCalendarService)
            .GetMethods()
            .Where(m => m.Name.Contains("Delete", StringComparison.OrdinalIgnoreCase)
                        || m.Name.Contains("Remove", StringComparison.OrdinalIgnoreCase))
            .Select(m => m.Name)
            .ToList();

        Assert.True(
            deleting.Count == 0,
            "IGoogleCalendarService must expose no way to remove an event from the practice's calendar. "
            + "Offending members: " + string.Join(", ", deleting));

        // Non-vacuity: a renamed interface would leave the assertion above passing over an empty set for ever.
        Assert.Contains(
            nameof(IGoogleCalendarService.CreateEventAsync),
            typeof(IGoogleCalendarService).GetMethods().Select(m => m.Name));
    }

    /// <summary>The client too — an implementation may not offer what the contract withholds.</summary>
    [Fact]
    public void The_Google_Client_Implements_No_Delete()
    {
        var deleting = typeof(GoogleCalendarService)
            .GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic
                        | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly)
            .Where(m => m.Name.Contains("Delete", StringComparison.OrdinalIgnoreCase))
            .Select(m => m.Name)
            .ToList();

        Assert.True(
            deleting.Count == 0,
            "GoogleCalendarService must implement no delete. Offending members: " + string.Join(", ", deleting));
    }

    /// <summary>
    /// ⚠️ The one a reflection test cannot see: the Google SDK's delete called inline, with no method of our own
    /// wrapping it. Scanned over both Google-facing files, comments stripped — these files *document* the removed
    /// behaviour at length, and a naive scan would match the explanation and never the code.
    /// </summary>
    [Theory]
    [InlineData("GoogleCalendarService.cs")]
    [InlineData("GoogleCalendarSyncService.cs")]
    public void No_Google_Facing_File_Calls_The_SDKs_Delete(string fileName)
    {
        var path = Path.Combine(
            RepositoryRoot().FullName,
            "api", "ClinicManagement.Infrastructure", "Services", fileName);

        Assert.True(File.Exists(path), $"{fileName} not found at {path}. The guard cannot run and must not pass.");

        var code = StripComments(File.ReadAllText(path));

        Assert.DoesNotContain("Events.Delete", code, StringComparison.Ordinal);
        Assert.DoesNotContain("DeleteEventAsync", code, StringComparison.Ordinal);
    }

    /// <summary>
    /// And the positive half, so « never delete » cannot be satisfied by a service that does nothing at all: the
    /// push still creates and still updates.
    /// </summary>
    [Fact]
    public void The_Push_Still_Creates_And_Updates()
    {
        var names = typeof(IGoogleCalendarService).GetMethods().Select(m => m.Name).ToList();

        Assert.Contains(nameof(IGoogleCalendarService.CreateEventAsync), names);
        Assert.Contains(nameof(IGoogleCalendarService.UpdateEventAsync), names);
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────────────

    /// <inheritdoc cref="Features.Appointments.CalendarImportRevertSafetyTests"/>
    private static string StripComments(string source)
    {
        var withoutBlocks = Regex.Replace(source, @"/\*.*?\*/", string.Empty, RegexOptions.Singleline);

        return string.Join(
            '\n',
            withoutBlocks
                .Split('\n')
                .Select(line =>
                {
                    var trimmed = line.TrimStart();
                    return trimmed.StartsWith("//", StringComparison.Ordinal)
                        ? string.Empty
                        : line;
                }));
    }

    /// <summary>
    /// Found through <see cref="CallerFilePathAttribute"/>, never <c>AppContext.BaseDirectory</c>: this suite is
    /// routinely built to a scratch output directory outside the repo (the Smart App Control workaround).
    /// </summary>
    private static DirectoryInfo RepositoryRoot([CallerFilePath] string thisFile = "")
    {
        for (var dir = new FileInfo(thisFile).Directory; dir != null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "web", "package.json"))
                && File.Exists(Path.Combine(dir.FullName, "console", "package.json")))
            {
                return dir;
            }
        }

        throw new InvalidOperationException(
            "Repository root not found from " + thisFile + ". The guard must throw rather than skip.");
    }
}
