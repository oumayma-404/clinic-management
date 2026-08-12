using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Common.Maintenance;

/// <summary>
/// Turns a cabinet's audit rows into the activity figures the vendor console reports
/// (<c>platform-console</c> AC-2.1, AC-2.2, EC-10). <b>Pure</b> — rows and a window in, counts out — which is
/// what makes the two exclusions below testable at all.
///
/// <para><b>Why the exclusions are the whole substance.</b> « Saves » is meant to answer « are there people
/// working in this practice? », so it must count only actions taken by <b>people at the cabinet</b>:</para>
/// <list type="bullet">
///   <item><description><b>Background work</b> (<c>job|…</c>): scheduled backups, the reminder dispatcher, the
///   expiry pass. Every one of them writes rows into every cabinet's ledger every day, so counting them would
///   make the busiest and the emptiest practice read identically — and the emptiest one would read as
///   <i>active</i>, which is the one answer that costs the vendor money.</description></item>
///   <item><description><b>The vendor's own console writes</b> (<c>console|…</c>). Granting a dormant cabinet a
///   subscription must not make it read as active the next morning — on exactly the cabinet the « dormant »
///   filter just surfaced. Without this, the act of responding to the signal destroys the signal.</description></item>
/// </list>
///
/// <para>⚠️ <b>Both are matched on <see cref="AuditActor"/>'s own prefix constants</b>, never on a retyped
/// <c>"job|"</c> / <c>"console|"</c> literal here. A second copy of a prefix is a filter that keeps passing
/// while the writer moves — the <c>fixes-dont-propagate</c> shape this codebase is full of.</para>
///
/// <para>⚠️ <b>What this pass may NOT be asked for.</b> The total patient count, the staff count, the last
/// sign-in and the month's takings are <i>not</i> derivable here and are read from their own sources by the
/// job — see <c>ClinicActivitySnapshot</c>. Counting patients from audit <c>Insert</c> rows in particular would
/// be wrong in the worst direction: the ledger only exists since <c>adoption-qa-i</c>, so an established
/// practice would read as nearly empty.</para>
/// </summary>
public static class PlatformCounterPass
{
    /// <summary>
    /// The audited entity whose insertions are « rendez-vous pris ».
    ///
    /// <para>⚠️ <b>Booked, not held.</b> An audit row records a save, so this counts appointments <i>created</i>
    /// in the window — not visits that took place in it. That is the right measure for this screen (it is
    /// activity by people at the cabinet, which is what the ledger can see) and the screen labels it that way;
    /// reading it as attendance would overstate a practice that books far ahead and understate one that does not.
    /// </para>
    /// </summary>
    private const string AppointmentEntity = "Appointment";

    /// <summary>The audited entity whose insertions are « nouveaux patients » for a day row.</summary>
    private const string PatientEntity = "Patient";

    /// <summary>
    /// True when this actor is a person at the cabinet, rather than a background process or the vendor's
    /// console. The single definition of AC-2.2's « only actions taken by people at the cabinet ».
    /// </summary>
    public static bool CountsAsCabinetActivity(string? userId)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            // A row with no actor cannot be claimed for the cabinet. The ledger writes `job|unknown` rather than
            // an empty actor, so this is a guard against malformed data, not a case the interceptor produces.
            return false;
        }

        // ⚠️ The restore prefix is a DECORATION and therefore matches neither of the two below: a console-driven
        // restore is `restore|console|{guid}`, which starts with neither `job|` nor `console|`. Left out, the
        // vendor putting a dead cabinet back made it read as the portfolio's most active practice the next
        // morning — poisoning `sort=activity` and the « dormant » filter on exactly the cabinet the filter had
        // just surfaced, which is the « responding to the signal destroys the signal » failure the console
        // exclusion was written to prevent, at far greater magnitude.
        return !userId.StartsWith(AuditActor.ProcessPrefix, StringComparison.Ordinal)
               && !userId.StartsWith(AuditActor.ConsolePrefix, StringComparison.Ordinal)
               && !userId.StartsWith(AuditActor.RestorePrefix, StringComparison.Ordinal);
    }

    /// <summary>
    /// Counts one cabinet's activity over an inclusive UTC window.
    ///
    /// <para><see cref="PlatformActivityCounts.ActiveDays"/> is counted in <b>clinic-local</b> days: a save at
    /// 00:30 Tunis belongs to that Tunisian day, and bucketing on the UTC date would credit it to the previous
    /// one — the same boundary this codebase spent a whole part correcting elsewhere.</para>
    /// </summary>
    public static PlatformActivityCounts Count(
        IEnumerable<ClinicActivityAuditRow> rows, DateTime fromUtcInclusive, DateTime toUtcInclusive)
    {
        ArgumentNullException.ThrowIfNull(rows);

        var writes = 0;
        var appointments = 0;
        var patientsCreated = 0;
        DateTime? lastWriteAt = null;
        var activeDays = new HashSet<DateOnly>();

        foreach (var row in rows)
        {
            if (row.OccurredAt < fromUtcInclusive || row.OccurredAt > toUtcInclusive)
            {
                continue;
            }

            if (!CountsAsCabinetActivity(row.UserId))
            {
                continue;
            }

            writes++;
            activeDays.Add(LocalDayOf(row.OccurredAt));

            if (lastWriteAt is null || row.OccurredAt > lastWriteAt)
            {
                lastWriteAt = row.OccurredAt;
            }

            if (row.Action == AuditAction.Insert)
            {
                if (row.EntityType == AppointmentEntity) appointments++;
                else if (row.EntityType == PatientEntity) patientsCreated++;
            }
        }

        return new PlatformActivityCounts(writes, appointments, patientsCreated, activeDays.Count, lastWriteAt);
    }

    /// <summary>The clinic-local calendar day a UTC instant falls on — the bucket every day figure is defined in.</summary>
    public static DateOnly LocalDayOf(DateTime utc) => DateOnly.FromDateTime(ClinicClock.ToClinicLocal(utc));
}

/// <summary>
/// What one window of a cabinet's audit rows amounts to. <see cref="LastWriteAt"/> is null where nobody at the
/// cabinet saved anything in the window — distinct from a zero count only in that it stays null across a longer
/// window too, which is how « n'a jamais rien fait » is told from « rien ce mois-ci ».
/// </summary>
public readonly record struct PlatformActivityCounts(
    int Writes, int Appointments, int PatientsCreated, int ActiveDays, DateTime? LastWriteAt);
