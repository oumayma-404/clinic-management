using System.Linq.Expressions;
using ClinicManagement.Application.Common;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;

namespace ClinicManagement.Application.Features.LabOrders;

/// <summary>
/// The one rule for « ce bon est en retard », stated as a single expression so the number the dashboard leads
/// with and the badge on the list it drills through to cannot disagree.
///
/// <para>The dashboard's « Prothèses en retard : N » card lands on <c>/lab-orders?status=Sent</c>, where nothing
/// marked which N rows were meant — a bon « prévu 24 août » rendered identically to one « prévu 27 août », and
/// the only way to tell was to read seven dates by eye.</para>
///
/// <para>⚠️ <b>The cutoff is the start of the clinic-local day, not "now".</b> <c>ExpectedDate</c> holds a date,
/// stored at midnight, so comparing it against the current instant made a bon due <b>today</b> late from 00:01
/// onwards — invisible while the count was the only surface, and immediately visible once a row wears a badge.
/// « En retard » means the day it was due has passed. Both surfaces move together because both read this file.
/// </para>
///
/// <para>A bon with no <c>ExpectedDate</c> has nothing to be late against and is never late — inventing a
/// default would be inventing a deadline the practice never agreed with the prothésiste. Received and Fitted are
/// already back; InProgress is excluded on the reading that the lab has acknowledged it and is working.</para>
/// </summary>
public static class LabOrderOverdue
{
    /// <summary>Midnight of the clinic-local today, in UTC — the instant a due date stops being in the future.</summary>
    public static DateTime CutoffUtc(DateTime? nowUtc = null) =>
        ClinicClock.StartOfLocalDayUtc(ClinicClock.ClinicToday(nowUtc));

    /// <summary>The rule, translated to SQL by <c>CountOverdueAsync</c>.</summary>
    public static Expression<Func<LabWorkOrder, bool>> Predicate(DateTime cutoffUtc) =>
        o => o.Status == LabOrderStatus.Sent && o.ExpectedDate != null && o.ExpectedDate < cutoffUtc;

    /// <summary>
    /// The same rule as a delegate, for the DTO mapping. Compiled from <see cref="Predicate"/> rather than
    /// re-typed, so there is one statement of it; hold the result for a page instead of calling it per row.
    /// </summary>
    public static Func<LabWorkOrder, bool> Evaluator(DateTime cutoffUtc) => Predicate(cutoffUtc).Compile();
}
