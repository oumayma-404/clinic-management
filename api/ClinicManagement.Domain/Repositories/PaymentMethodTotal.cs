using ClinicManagement.Domain.Enums;

namespace ClinicManagement.Domain.Repositories;

/// <summary>
/// One line of a per-method money total — the shape of a <c>GROUP BY "Method"</c> over a payment ledger.
///
/// <para>
/// It lives in its own file because <b>both</b> payment ledgers project into it
/// (<c>IInvoiceRepository.GetCollectedByMethodBetweenAsync</c> and
/// <c>ITreatmentPlanRepository.GetInstallmentCollectedByMethodBetweenAsync</c>) and the caller merges the two
/// into a single breakdown. A per-interface copy would be two types the merge has to reconcile.
/// </para>
/// <para>
/// ⚠️ Only the methods actually present in the window appear. The absence of a row means « nothing was taken
/// this way », which is not the same statement as a zero the database computed — and the caller is the one that
/// decides whether to render the missing methods as zero (la caisse does, so the drawer figure is always on
/// screen even on a day with only cheques).
/// </para>
/// </summary>
public sealed record PaymentMethodTotal(PaymentMethod Method, decimal Amount);
