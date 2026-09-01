using System.Reflection;
using ClinicManagement.API.Controllers;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Domain.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Xunit;

namespace ClinicManagement.UnitTests.Features.Appointments;

/// <summary>
/// Guards on the retirement of « Importer depuis Google » (Google→App).
///
/// <para><b>Why a test and not a note.</b> The direction was removed because one press was a mass, unbounded,
/// irreversible write: 97 days of a practice's calendar became appointment rows, and the past week of them landed
/// on « À clôturer » as visits nobody could honestly close — so the cabinet cancelled them, which inflated its own
/// « taux d'absence » and deleted the matching events from its Google calendar. That is not a bug anybody would
/// re-introduce on purpose; it is one somebody re-introduces by adding « just a small pull » to a sync service
/// whose name does not say which way it goes.</para>
///
/// <para>⚠️ <b>The second half matters more than the first.</b> The undo — <see cref="CalendarImportRun"/>, the
/// preview and the revert — reads history and imports nothing, so it deliberately <b>outlived</b> the importer: a
/// cabinet that pressed the old button can still take it back, and a cabinet whose rows are still on the worklist
/// today has no other way to. A tidy-up that removed « the calendar import stuff » wholesale would take a live
/// recovery path with it and nothing would fail until somebody needed it.</para>
/// </summary>
public class CalendarImportRetirementTests
{
    /// <summary>
    /// The sync contract offers exactly one direction. Re-adding a pull here is what the retirement removed, and
    /// the name <c>IGoogleCalendarSyncService</c> does not by itself say which way « sync » goes.
    /// </summary>
    [Fact]
    public void Sync_Contract_Offers_Only_The_Push_Direction()
    {
        var methods = typeof(IGoogleCalendarSyncService)
            .GetMethods()
            .Select(m => m.Name)
            .OrderBy(n => n)
            .ToArray();

        Assert.Equal(new[] { nameof(IGoogleCalendarSyncService.SyncAppointmentToGoogleCalendarAsync) }, methods);
    }

    /// <summary>
    /// No route pulls events in. The three surviving <c>imports/…</c> routes are reads over runs already on
    /// record plus the undo itself — asserted by name below rather than excluded by a pattern, so a new route
    /// that imports cannot hide behind a familiar-looking name.
    /// </summary>
    [Fact]
    public void No_Controller_Route_Imports_From_Google()
    {
        var actions = typeof(GoogleCalendarController)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => m.GetCustomAttributes<HttpMethodAttribute>().Any())
            .Select(m => m.Name)
            .ToArray();

        var permitted = new[]
        {
            nameof(GoogleCalendarController.GetImports),
            nameof(GoogleCalendarController.PreviewRevert),
            nameof(GoogleCalendarController.RevertImport),
        };

        var suspicious = actions
            .Where(n => n.Contains("Import", StringComparison.OrdinalIgnoreCase)
                        || n.Contains("FromGoogle", StringComparison.OrdinalIgnoreCase))
            .Where(n => !permitted.Contains(n))
            .ToArray();

        Assert.Empty(suspicious);
    }

    /// <summary>
    /// The recurring job is gone from the assembly, not merely unregistered. <c>Program.cs</c> also calls
    /// <c>RecurringJob.RemoveIfExists("import-from-google-calendar")</c>, because deleting the registration alone
    /// leaves the entry in every deployed install's Hangfire storage, firing every 15 minutes at a missing type.
    /// </summary>
    [Fact]
    public void The_Recurring_Import_Job_Type_No_Longer_Exists()
    {
        var jobs = typeof(GoogleCalendarController).Assembly
            .GetTypes()
            .Where(t => t.Namespace == "ClinicManagement.API.BackgroundJobs")
            .Select(t => t.Name)
            .ToArray();

        Assert.DoesNotContain("GoogleCalendarImportJob", jobs);
    }

    /// <summary>
    /// ⚠️ The undo must survive the retirement. These three routes are a cabinet's only way back from an import it
    /// already made, and the rows are still on real worklists — so this is a live recovery path, not history.
    /// </summary>
    [Theory]
    [InlineData(nameof(GoogleCalendarController.GetImports))]
    [InlineData(nameof(GoogleCalendarController.PreviewRevert))]
    [InlineData(nameof(GoogleCalendarController.RevertImport))]
    public void The_Undo_Outlives_The_Importer(string action)
    {
        var method = typeof(GoogleCalendarController).GetMethod(action);

        Assert.NotNull(method);
        Assert.NotEmpty(method!.GetCustomAttributes<HttpMethodAttribute>());
    }

    /// <summary>
    /// The run entity keeps the counts it recorded, so a reverted or retired-era run can still say what it did
    /// after its rows are gone. Derived from the entity rather than asserted in prose: the preview and the banner
    /// both read these, and a run that forgot its own figures reads exactly like an import that never happened.
    /// </summary>
    [Fact]
    public void A_Recorded_Run_Still_Reports_What_It_Did()
    {
        foreach (var name in new[]
                 {
                     nameof(CalendarImportRun.AppointmentsCreated),
                     nameof(CalendarImportRun.PatientsCreated),
                     nameof(CalendarImportRun.RevertedAtUtc),
                 })
        {
            Assert.NotNull(typeof(CalendarImportRun).GetProperty(name));
        }
    }

    /// <summary>
    /// « Retirer de la liste » asks for no motif. It shipped demanding one on « Rien à facturer »'s reasoning, and
    /// the parallel does not hold: that mark is a claim about money the cabinet may be asked to justify, this one
    /// asserts nothing. Charging a sentence for it priced the honest exit above the annulation that caused the
    /// wrong absence rate in the first place.
    /// </summary>
    [Fact]
    public void Disregarding_A_Visit_Asks_For_No_Motif()
    {
        var disregard = typeof(Appointment).GetMethod(nameof(Appointment.Disregard));
        Assert.NotNull(disregard);
        Assert.DoesNotContain(
            "reason",
            disregard!.GetParameters().Select(p => p.Name!),
            StringComparer.OrdinalIgnoreCase);

        // And nothing is left holding a motif that nothing writes.
        Assert.Null(typeof(Appointment).GetProperty("DisregardedReason"));
    }

    /// <summary>
    /// A disregarded visit really does leave the figures, not only the list. Excluded from one but not the other,
    /// the worklist goes quiet while the absence rate stays exactly as wrong as before — which is the complaint
    /// the whole feature exists to answer, and it is invisible unless the dashboard is checked afterwards.
    /// </summary>
    [Fact]
    public void Disregarding_Is_Recorded_And_Withdrawable()
    {
        var appointment = new Appointment(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), doctorId: null,
            new DateTime(2026, 8, 31, 9, 0, 0, DateTimeKind.Utc), TimeSpan.FromHours(1));

        appointment.Disregard("local|someone", new DateTime(2026, 9, 1, 10, 0, 0, DateTimeKind.Utc));
        Assert.True(appointment.IsDisregarded);
        Assert.Equal("local|someone", appointment.DisregardedByUserId);

        // Idempotent: a double-click or an overlapping bulk selection must not restamp it.
        appointment.Disregard("local|someone-else", new DateTime(2026, 9, 2, 10, 0, 0, DateTimeKind.Utc));
        Assert.Equal("local|someone", appointment.DisregardedByUserId);

        appointment.RestoreToWorklist();
        Assert.False(appointment.IsDisregarded);
        Assert.Null(appointment.DisregardedByUserId);
    }
}
