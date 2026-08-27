namespace ClinicManagement.Application.DTOs;

/// <summary>
/// « Plafond annuel CNAM » for one patient in one clinic year (L10) — the ceiling, what this clinic has consumed of
/// it, and what is left.
///
/// <para><b>Every figure here is an estimate, for two independent reasons</b>, and both are carried on the DTO
/// rather than left to each screen's own wording:</para>
/// <list type="number">
///   <item><see cref="CeilingIsDefault"/> — the barème behind the ceiling is <b>sourced but not officially
///   confirmed</b> (see <c>CnamPlafond</c>). When an admin has recorded the household's real figure, this is false
///   and the ceiling is as good as the person who typed it.</item>
///   <item><see cref="SeesThisClinicOnly"/> — always true, and stated anyway. The clinic can only count the acts
///   <b>it</b> performed, so a patient treated elsewhere has consumed ceiling this software cannot see and
///   <see cref="Remaining"/> is an <b>upper bound</b>. It is a field rather than a constant so the sentence lives
///   beside the number it qualifies, and so a future multi-practice read can turn it off.</item>
/// </list>
///
/// <para>⚠️ Consumption is measured from the clinic's own <b>issued invoices</b>, not from submitted bulletins: the
/// product records no BS1 submission with an amount, and the invoice is the only place an act's money exists. That
/// makes the figure lag a bulletin the caisse has not yet paid, and lead one it refused — stated here because no
/// caller can compensate for it.</para>
/// </summary>
public class CnamCeilingDto
{
    /// <summary>The clinic-local year the figures cover — a ceiling is annual, and « annual » is the clinic's calendar.</summary>
    public int Year { get; set; }

    /// <summary>The ceiling being measured against: the recorded household figure, else the barème + the dental allowance.</summary>
    public decimal Ceiling { get; set; }

    /// <summary>The household part of a computed ceiling (null when <see cref="CeilingIsDefault"/> is false — an override replaces the whole derivation, it does not adjust it).</summary>
    public decimal? BaseCeiling { get; set; }

    /// <summary>The soins-dentaires-externes allowance included in a computed ceiling. Null for an override, same reason.</summary>
    public decimal? DentalAllowance { get; set; }

    /// <summary>Dependants recorded on the patient's CNAM identity — what <see cref="BaseCeiling"/> was derived from.</summary>
    public int DependantCount { get; set; }

    /// <summary>True when <see cref="Ceiling"/> came from the built-in barème rather than from a figure somebody recorded.</summary>
    public bool CeilingIsDefault { get; set; }

    /// <summary>Reimbursement this clinic's issued invoices represent in the year, counting only acts that consume the ceiling.</summary>
    public decimal Consumed { get; set; }

    /// <summary>Reimbursement for acts that do <b>not</b> consume it (prothèse). Reported, never silently dropped.</summary>
    public decimal HorsPlafond { get; set; }

    /// <summary><c>max(0, Ceiling − Consumed)</c> — floored, because a negative « reste » is not a thing a ceiling has.</summary>
    public decimal Remaining { get; set; }

    /// <summary>True once <see cref="Consumed"/> has reached the ceiling — the case « Remboursement indicatif » used to over-promise on.</summary>
    public bool Exhausted { get; set; }

    /// <summary>Always true today. See the type remarks: it is what makes <see cref="Remaining"/> an upper bound.</summary>
    public bool SeesThisClinicOnly { get; set; } = true;

    /// <summary>How many invoices of the year the consumption was computed over — so « 0,000 consommé » can be told from « nothing billed yet ».</summary>
    public int InvoiceCount { get; set; }
}
