using ClinicManagement.Application.Common;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Maintenance;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using Xunit;

namespace ClinicManagement.UnitTests.Features.Platform;

/// <summary>
/// The pure pass behind every activity figure the vendor console shows (<c>platform-console</c> AC-2.1, AC-2.2,
/// EC-10).
///
/// <para><b>Most of this file is about the two exclusions</b>, because they are the only part that can fail
/// silently. A miscounted total is visible to anyone who looks twice; a background job counted as cabinet
/// activity makes an empty practice read as a busy one, and the vendor's response to that is <i>not</i> to
/// investigate — it is to leave a churning cabinet alone.</para>
///
/// <para>Every fixture pins a fixed instant. The pass buckets active days in <b>clinic-local</b> time, so a
/// fixture built from <c>DateTime.UtcNow</c> would pass or fail depending on the hour the suite runs — which is
/// the failure <c>ClinicClockTests</c> was written to stop being repeated.</para>
/// </summary>
public class PlatformCounterPassTests
{
    // 10 August 2026, 12:00 UTC = 13:00 in Tunis. Mid-afternoon, so no fixture sits on a day boundary by
    // accident; the boundary cases below put themselves there deliberately.
    private static readonly DateTime Noon = new(2026, 8, 10, 12, 0, 0, DateTimeKind.Utc);

    private static readonly DateTime WindowFrom =
        ClinicClock.StartOfLocalDayUtc(new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc));

    private static readonly DateTime WindowTo =
        ClinicClock.LastTickOfLocalDayUtc(new DateTime(2026, 8, 10, 0, 0, 0, DateTimeKind.Utc));

    private static ClinicActivityAuditRow Row(
        string userId, DateTime occurredAt, string entityType = "Invoice", AuditAction action = AuditAction.Update) =>
        new(userId, entityType, action, occurredAt);

    private const string Person = "local|11111111-1111-1111-1111-111111111111";

    // [AC-2.2] A person at the cabinet is anyone who is not a process and not the console. Stated as a predicate
    // because both call sites — the count below and any future reader — must agree on one definition.
    [Fact]
    public void A_Clinic_User_Counts_As_Cabinet_Activity()
    {
        Assert.True(PlatformCounterPass.CountsAsCabinetActivity(Person));
        Assert.True(PlatformCounterPass.CountsAsCabinetActivity("auth0|abc123"));
    }

    // [AC-2.2] Background work writes into every cabinet's ledger every day. Counting it would make the busiest
    // and the emptiest practice read identically — and the empty one would read as ACTIVE, which is the reading
    // that costs the vendor a renewal.
    [Fact]
    public void Background_Work_Does_Not_Count()
    {
        Assert.False(PlatformCounterPass.CountsAsCabinetActivity($"{AuditActor.ProcessPrefix}BackupJob"));
        Assert.False(PlatformCounterPass.CountsAsCabinetActivity(AuditActor.Unknown.UserId));
    }

    // [EC-10] The vendor's own writes. Granting a dormant cabinet a subscription must not make it read as active
    // the next morning — on exactly the cabinet the « dormant » filter just surfaced. Without this, responding to
    // the signal destroys the signal.
    [Fact]
    public void The_Consoles_Own_Writes_Do_Not_Count()
    {
        var console = AuditActor.Console(Guid.NewGuid()).UserId;

        Assert.False(PlatformCounterPass.CountsAsCabinetActivity(console));
    }

    // [AC-2.2] The same two exclusions, through the counting path rather than the predicate — because a pass that
    // knew the rule and forgot to apply it is the defect, not a wrong predicate.
    [Fact]
    public void Counting_Excludes_Jobs_And_The_Console()
    {
        var rows = new[]
        {
            Row(Person, Noon),
            Row($"{AuditActor.ProcessPrefix}NotificationJob", Noon),
            Row(AuditActor.Console(Guid.NewGuid()).UserId, Noon)
        };

        var counts = PlatformCounterPass.Count(rows, WindowFrom, WindowTo);

        Assert.Equal(1, counts.Writes);
        Assert.Equal(Noon, counts.LastWriteAt);
    }

    // [AC-2.1] `appointments30d` counts appointments BOOKED — audit inserts on Appointment — and nothing else.
    // An update to an appointment is a save (it counts as a write) but is not a new booking.
    [Fact]
    public void Only_Inserted_Appointments_And_Patients_Are_Counted_As_Such()
    {
        var rows = new[]
        {
            Row(Person, Noon, "Appointment", AuditAction.Insert),
            Row(Person, Noon, "Appointment", AuditAction.Update),
            Row(Person, Noon, "Patient", AuditAction.Insert),
            Row(Person, Noon, "Patient", AuditAction.Delete),
            Row(Person, Noon, "Invoice", AuditAction.Insert)
        };

        var counts = PlatformCounterPass.Count(rows, WindowFrom, WindowTo);

        Assert.Equal(5, counts.Writes);
        Assert.Equal(1, counts.Appointments);
        Assert.Equal(1, counts.PatientsCreated);
    }

    // [AC-2.1] « jours actifs » is what tells a cabinet used daily from one that had a single busy afternoon, so
    // several saves in one day are one active day.
    [Fact]
    public void Active_Days_Counts_Days_Not_Saves()
    {
        var rows = new[]
        {
            Row(Person, Noon),
            Row(Person, Noon.AddHours(2)),
            Row(Person, Noon.AddDays(-1)),
            Row(Person, Noon.AddDays(-1).AddMinutes(5))
        };

        var counts = PlatformCounterPass.Count(rows, WindowFrom, WindowTo);

        Assert.Equal(4, counts.Writes);
        Assert.Equal(2, counts.ActiveDays);
    }

    // [AC-2.1] The bucket is the CABINET's day, not UTC's. Tunisia is UTC+1, so 23:30 UTC on 9 August is already
    // 10 August in the practice — and bucketing on the UTC date would credit that evening's work to the previous
    // day, splitting one working evening across two « jours actifs ».
    [Fact]
    public void Active_Days_Are_Bucketed_In_The_Clinics_Own_Day()
    {
        var lateEveningUtc = new DateTime(2026, 8, 9, 23, 30, 0, DateTimeKind.Utc);
        var nextMorningUtc = new DateTime(2026, 8, 10, 7, 0, 0, DateTimeKind.Utc);

        var counts = PlatformCounterPass.Count(
            new[] { Row(Person, lateEveningUtc), Row(Person, nextMorningUtc) }, WindowFrom, WindowTo);

        Assert.Equal(1, counts.ActiveDays);
        Assert.Equal(
            new DateOnly(2026, 8, 10),
            PlatformCounterPass.LocalDayOf(lateEveningUtc));
    }

    // [AC-2.1] The window is inclusive on both ends — the convention every windowed read in this codebase
    // follows — and rows outside it are ignored rather than trusted from the caller's query.
    [Fact]
    public void The_Window_Is_Inclusive_And_Filters_What_The_Caller_Passed()
    {
        var rows = new[]
        {
            Row(Person, WindowFrom),
            Row(Person, WindowTo),
            Row(Person, WindowFrom.AddTicks(-1)),
            Row(Person, WindowTo.AddTicks(1))
        };

        var counts = PlatformCounterPass.Count(rows, WindowFrom, WindowTo);

        Assert.Equal(2, counts.Writes);
    }

    // [EC-8] A cabinet with nothing to count is a real answer, and it must be zeros with a null last-write —
    // never an absence the caller has to interpret.
    [Fact]
    public void A_Cabinet_With_Nothing_To_Count_Yields_Zeros_And_No_Last_Write()
    {
        var counts = PlatformCounterPass.Count(Array.Empty<ClinicActivityAuditRow>(), WindowFrom, WindowTo);

        Assert.Equal(0, counts.Writes);
        Assert.Equal(0, counts.Appointments);
        Assert.Equal(0, counts.PatientsCreated);
        Assert.Equal(0, counts.ActiveDays);
        Assert.Null(counts.LastWriteAt);
    }

    // [AC-2.2] The exclusions read AuditActor's own constants. If a prefix is ever reworded there, this fails
    // here rather than in production — where the symptom would be a dormant cabinet silently reading as active.
    [Fact]
    public void The_Exclusions_Track_AuditActors_Own_Prefixes()
    {
        Assert.False(PlatformCounterPass.CountsAsCabinetActivity(AuditActor.Process("any").UserId));
        Assert.False(PlatformCounterPass.CountsAsCabinetActivity(AuditActor.Console(Guid.Empty).UserId));
        Assert.StartsWith(AuditActor.ProcessPrefix, AuditActor.Process("any").UserId, StringComparison.Ordinal);
        Assert.StartsWith(AuditActor.ConsolePrefix, AuditActor.Console(Guid.Empty).UserId, StringComparison.Ordinal);
    }
}
