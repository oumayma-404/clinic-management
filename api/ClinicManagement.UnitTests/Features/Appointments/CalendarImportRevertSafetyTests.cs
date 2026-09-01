using System.Runtime.CompilerServices;
using ClinicManagement.Application.Features.Appointments.Commands;
using Xunit;

namespace ClinicManagement.UnitTests.Features.Appointments;

/// <summary>
/// The two ways « Annuler cet import » could destroy something it exists to protect — held structurally, because
/// both failures are <b>silent</b> and neither is visible to a behavioural test with a mocked repository.
///
/// <para><b>1. It must never speak to Google.</b> <c>GoogleCalendarSyncService</c> deletes the Google event
/// behind an appointment the moment its status becomes <c>Cancelled</c> or <c>Completed</c> — which is how a
/// cabinet tidying up after an unwanted import was quietly deleting its own calendar in the first place. An undo
/// routed through <c>Appointment.Cancel()</c>, or one that dispatched a sync, would finish that job. The rows are
/// removed outright instead.</para>
///
/// <para><b>2. Reminders must be deleted before the appointments they name.</b>
/// <c>Notification.AppointmentId</c> and <c>PushDelivery</c>'s are <c>OnDelete(SetNull)</c>, so deleting an
/// appointment does not take its queued reminder with it — it orphans it with a null link, and the minutely
/// dispatcher still sends it. A patient would receive « Rappel : votre rendez-vous demain » for a visit that no
/// longer exists, hours after the practice undid the import.</para>
///
/// <para>Both are asserted against the <b>source</b>, the way <c>SubscriptionGateMiddlewareTests</c> pins its
/// middleware's position against <c>Program.cs</c>: the code is correct in isolation and only its <i>shape</i>
/// is wrong, so nothing else in the build can see a regression.</para>
/// </summary>
public class CalendarImportRevertSafetyTests
{
    /// <summary>
    /// Nothing Google-shaped may reach the handler, checked by reflection so a dependency added later fails on
    /// the day it is written rather than the day somebody presses the button.
    /// </summary>
    [Fact]
    public void The_Revert_Handler_Takes_No_Google_Dependency()
    {
        var constructor = Assert.Single(typeof(RevertCalendarImportRunCommandHandler).GetConstructors());

        var googleish = constructor
            .GetParameters()
            .Where(p => p.ParameterType.Name.Contains("Google", StringComparison.OrdinalIgnoreCase))
            .Select(p => p.ParameterType.Name)
            .ToList();

        Assert.True(
            googleish.Count == 0,
            "The calendar-import undo must never be able to reach Google: deleting the practice's own calendar "
            + "events is the damage it exists to repair. Offending dependencies: " + string.Join(", ", googleish));
    }

    /// <summary>
    /// And it must not route a deletion through the status that triggers the push. A <c>.Cancel(</c> anywhere in
    /// this file would do it — the sync deletes the Google event for a <c>Cancelled</c> appointment.
    /// </summary>
    [Fact]
    public void The_Revert_Command_Never_Cancels_An_Appointment_Or_Dispatches_A_Sync()
    {
        var source = File.ReadAllText(Path.Combine(
            RepositoryRoot().FullName,
            "api", "ClinicManagement.Application", "Features", "Appointments", "Commands",
            "RevertCalendarImportRunCommand.cs"));

        // Stripped of comments first: this file *documents* both hazards at length, and a naive scan would match
        // the explanation rather than the code — a guard that can only ever fail on its own prose.
        var code = StripComments(source);

        Assert.DoesNotContain(".Cancel(", code, StringComparison.Ordinal);
        Assert.DoesNotContain("GoogleSyncDispatcher", code, StringComparison.Ordinal);
        Assert.DoesNotContain("IGoogleCalendarService", code, StringComparison.Ordinal);
    }

    /// <summary>
    /// The delete order, read off the one method that owns it. Reminders and pushes are staged for removal
    /// <b>before</b> the appointments; the patients come last.
    /// </summary>
    [Fact]
    public void Reminders_Are_Deleted_Before_The_Appointments_They_Name()
    {
        var source = File.ReadAllText(Path.Combine(
            RepositoryRoot().FullName,
            "api", "ClinicManagement.Infrastructure", "Repositories", "CalendarImportRunRepository.cs"));

        var body = MethodBody(StripComments(source), "public async Task DeleteRunRowsAsync(");

        var notifications = body.IndexOf("_context.Notifications.RemoveRange", StringComparison.Ordinal);
        var pushes = body.IndexOf("_context.PushDeliveries.RemoveRange", StringComparison.Ordinal);
        var appointments = body.IndexOf("_context.Appointments.RemoveRange", StringComparison.Ordinal);

        Assert.True(notifications >= 0, "DeleteRunRowsAsync no longer removes the queued reminders.");
        Assert.True(pushes >= 0, "DeleteRunRowsAsync no longer removes the queued OS pushes.");
        Assert.True(appointments >= 0, "DeleteRunRowsAsync no longer removes the appointments.");

        Assert.True(
            notifications < appointments,
            "Queued reminders must be deleted BEFORE their appointments: the FK is OnDelete(SetNull), so the "
            + "other order leaves a live reminder pointing at nothing and the dispatcher still sends it.");
        Assert.True(
            pushes < appointments,
            "Queued OS pushes must be deleted BEFORE their appointments, for the same reason.");
    }

    /// <summary>The refusal a client branches on is a code, never a French sentence.</summary>
    [Fact]
    public void The_Already_Reverted_Refusal_Carries_A_Stable_Code()
    {
        Assert.Equal(
            "calendar_import_already_reverted",
            RevertCalendarImportRunCommandHandler.AlreadyRevertedCode);
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>Line and block comments removed, so a scan measures the code and not its explanation.</summary>
    private static string StripComments(string source)
    {
        var withoutBlocks = System.Text.RegularExpressions.Regex.Replace(
            source, @"/\*.*?\*/", string.Empty, System.Text.RegularExpressions.RegexOptions.Singleline);

        return string.Join(
            '\n',
            withoutBlocks
                .Split('\n')
                .Select(line =>
                {
                    var trimmed = line.TrimStart();
                    return trimmed.StartsWith("//", StringComparison.Ordinal)
                        || trimmed.StartsWith("///", StringComparison.Ordinal)
                        ? string.Empty
                        : line;
                }));
    }

    /// <summary>Everything from a method's signature to the start of the next member at the same indent.</summary>
    private static string MethodBody(string source, string signature)
    {
        var start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Could not find '{signature}'. The guard cannot run and must not pass silently.");

        var next = source.IndexOf("\n    public ", start + signature.Length, StringComparison.Ordinal);
        return next < 0 ? source[start..] : source[start..next];
    }

    /// <inheritdoc cref="Common.PasswordFloorSingleSourceTests"/>
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

        // Fail loudly rather than skip: a guard that cannot find its subject and reports green leaves the
        // contract it covers unchecked, which is the failure this whole file exists to prevent.
        throw new DirectoryNotFoundException(
            $"Could not locate the repository root by walking up from '{thisFile}'. The calendar-import undo "
            + "safety guards cannot run, and must fail rather than pass silently.");
    }
}
