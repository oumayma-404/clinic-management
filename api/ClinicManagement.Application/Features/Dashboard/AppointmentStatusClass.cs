using ClinicManagement.Domain.Enums;

namespace ClinicManagement.Application.Features.Dashboard;

/// <summary>
/// The five classes « Rendez-vous par statut » paints, and the <b>single authority</b> on which of the seven
/// <see cref="AppointmentStatus"/> members folds into which.
///
/// <para><b>Why five and not seven.</b> Seven fills side by side in one stacked column cannot be told apart. That is
/// measured, not asserted: with the app's own tokens, splitting « À venir » into two steps of the same azur leaves
/// the pair at ΔE 12.6 in normal vision (the floor is 15), and splitting it into azur + violet collapses to ΔE 2.1
/// under deuteranopia — the two colours become one for that reader. Five classes clear both gates.</para>
///
/// <para><b>The two folds each join statuses that answer the same question</b>, which is what makes them honest:
/// <c>Scheduled</c> and <c>Confirmed</c> are both « the séance has not happened yet », and <c>InProgress</c> and
/// <c>AwaitingClosure</c> are both « it happened, or is happening, and is not finished ». Nothing is folded across
/// that line — a cancelled visit and a missed one stay apart, because « the patient told us » and « the patient did
/// not come » are different facts about the practice and a clinic acts on them differently.</para>
///
/// <para><b>The Scheduled/Confirmed distinction is not lost</b>, only moved: the reader also returns
/// <c>ConfirmedUpcoming</c> so the legend can say « dont N confirmés ». In the agenda that distinction is what the
/// colour itself carries (see <c>web/components/appointment-labels.ts</c>); on a monthly aggregate it is a footnote,
/// and a footnote is the right place for it.</para>
///
/// <para>⚠️ This enum is a <b>presentation</b> grouping and is deliberately not in the Domain. The seven statuses
/// are the truth; these five are how one chart reads them.</para>
/// </summary>
public enum AppointmentStatusClass
{
    /// <summary>« Terminé » — <see cref="AppointmentStatus.Completed"/>. The work happened and was closed.</summary>
    Done = 1,

    /// <summary>« À venir » — <see cref="AppointmentStatus.Scheduled"/> + <see cref="AppointmentStatus.Confirmed"/>.</summary>
    Upcoming = 2,

    /// <summary>
    /// « À clôturer » — <see cref="AppointmentStatus.InProgress"/> + <see cref="AppointmentStatus.AwaitingClosure"/>.
    /// <para>⚠️ Deliberately <b>not</b> the same population as the « Séances à clôturer » chip on the same page. That
    /// chip counts what <c>VisitClosureRules</c> says still owes a presence, a fiche or a money document, and a
    /// <c>Completed</c> visit can legitimately still be on it. This class is about the visit's own <i>status</i>
    /// only. The two figures answer different questions and will differ; neither is wrong.</para>
    /// </summary>
    ToClose = 3,

    /// <summary>« Annulé » — <see cref="AppointmentStatus.Cancelled"/>. Called off; nothing is expected of anyone.</summary>
    Cancelled = 4,

    /// <summary>« Absent » — <see cref="AppointmentStatus.NoShow"/>. The chair was held and nobody came.</summary>
    Absent = 5
}

public static class AppointmentStatusClasses
{
    /// <summary>
    /// Which class a status belongs to.
    ///
    /// <para>A <c>switch</c> over every named member with no <c>_ =&gt;</c> default, on purpose: an eighth status
    /// appended to the enum must be a compile-time decision here, not silently absorbed into whichever class the
    /// default happened to name. The unnamed-cast case is the only fallthrough, and it takes
    /// <see cref="AppointmentStatusClass.Upcoming"/> because a value outside the enum is a booking whose outcome
    /// nobody has recorded.</para>
    /// </summary>
    public static AppointmentStatusClass Of(AppointmentStatus status) => status switch
    {
        AppointmentStatus.Completed => AppointmentStatusClass.Done,
        AppointmentStatus.Scheduled => AppointmentStatusClass.Upcoming,
        AppointmentStatus.Confirmed => AppointmentStatusClass.Upcoming,
        AppointmentStatus.InProgress => AppointmentStatusClass.ToClose,
        AppointmentStatus.AwaitingClosure => AppointmentStatusClass.ToClose,
        AppointmentStatus.Cancelled => AppointmentStatusClass.Cancelled,
        AppointmentStatus.NoShow => AppointmentStatusClass.Absent,
        _ => AppointmentStatusClass.Upcoming
    };
}
