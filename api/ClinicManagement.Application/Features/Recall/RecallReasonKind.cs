namespace ClinicManagement.Application.Features.Recall;

/// <summary>
/// Why a patient is on the « à rappeler » worklist.
///
/// <para>The list used to answer one question — "who have we not seen in six months?" — which for a
/// perio/implant practice is the <i>least</i> informative of the several reasons to pick up the phone. A patient
/// seen last week who accepted a 2 000 DT plan and stopped after two acts is both lost revenue and an unfinished
/// surgical case, yet a time-since-last-visit rule can never surface them.</para>
///
/// <para>Ordered by how urgent the reason is, most urgent first — <c>RecallWorklistRules</c> uses the enum order to
/// pick a patient's headline reason, so the declaration order is behaviour, not documentation.</para>
/// </summary>
public enum RecallReasonKind
{
    /// <summary>An échéance whose due date has passed and is not fully paid. Money already owed.</summary>
    OverdueInstallment = 0,

    /// <summary>An accepted/in-progress devis with acts still to do and nothing booked to do them.</summary>
    StalledPlan = 1,

    /// <summary>A devis presented but never accepted or refused — the patient has not answered.</summary>
    UnansweredDevis = 2,

    /// <summary>Not seen for the clinic's recall interval. The original — and weakest — reason.</summary>
    OverdueVisit = 3
}
