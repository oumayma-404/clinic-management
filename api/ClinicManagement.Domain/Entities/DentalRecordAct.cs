using ClinicManagement.Domain.Common;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Services;

namespace ClinicManagement.Domain.Entities;

/// <summary>
/// One act performed during a dental-record session (aggregate child of <see cref="DentalRecord"/>): a
/// procedure (from the priced <see cref="ProcedureType"/> menu, snapshotted, or free-text) applied to one or
/// more teeth, with its own cost. Its <see cref="ResultingCondition"/> (inferred from the procedure, editable)
/// feeds the patient's odontogram. A tooth may appear across multiple acts (multiple treatments per session).
/// </summary>
public class DentalRecordAct : Entity<Guid>
{
    public Guid DentalRecordId { get; private set; }
    public Guid? ProcedureTypeId { get; private set; }
    public string ProcedureName { get; private set; } = string.Empty;
    /// <summary>The act's total fee — the authoritative billed amount. Always supplied by the caller.</summary>
    public decimal Cost { get; private set; }
    /// <summary>
    /// The per-unit price <see cref="Cost"/> was built from, kept as provenance so the editor can reopen the
    /// act with its pricing intent intact and the invoice bridge can bill it as quantity × unit price.
    /// Null for a legacy row or an act whose unit price was never captured.
    /// </summary>
    public decimal? UnitCost { get; private set; }
    /// <summary>
    /// True when <see cref="Cost"/> represents <see cref="UnitCost"/> × treated teeth (composite, extraction,
    /// couronne…); false when it is a flat session fee (détartrage, panoramique, prothèse, orthodontie).
    /// Always false for an act with no teeth. <see cref="Cost"/> is never recomputed from these two — the
    /// caller owns the arithmetic, and these record how it was reached.
    /// </summary>
    public bool IsPerTooth { get; private set; }

    private readonly List<int> _toothNumbers = new();
    public IReadOnlyList<int> ToothNumbers => _toothNumbers.AsReadOnly();

    private readonly List<int> _ponticToothNumbers = new();
    /// <summary>
    /// Which of this act's teeth are <b>pontiques</b> (suspended replacements) rather than <b>piliers</b>
    /// (crowned teeth keeping their own roots). Always a subset of <see cref="ToothNumbers"/>, and empty for
    /// every act that is not a bridge.
    ///
    /// <para>⚠️ <b>This is the only place a bridge's shape is recorded, and it had to be recorded rather than
    /// derived.</b> Before it, an act carried ONE <see cref="ResultingCondition"/> for all of its teeth, so a
    /// three-unit bridge could only be entered as two separate acts of the same procedure — which nothing in the
    /// UI said to do, and doing it in one act charted three abutments and no pontic: a bridge that cannot
    /// exist.</para>
    ///
    /// <para>⚠️ Position cannot supply the answer — see <see cref="BridgeCharting"/> on pier abutments and
    /// cantilevers.</para>
    /// </summary>
    public IReadOnlyList<int> PonticToothNumbers => _ponticToothNumbers.AsReadOnly();

    /// <summary>Resulting tooth state for the odontogram (null = no state change, e.g. cleaning/consultation).</summary>
    public ToothCondition? ResultingCondition { get; private set; }
    public string? Surfaces { get; private set; }
    public string? Note { get; private set; }
    public DateTime CreatedAt { get; private set; }

    private DentalRecordAct() { } // For EF Core

    public DentalRecordAct(Guid id, Guid dentalRecordId, DentalRecordActInput input)
    {
        if (input == null)
            throw new ArgumentNullException(nameof(input));
        if (string.IsNullOrWhiteSpace(input.ProcedureName))
            throw new ArgumentException("Le nom de l'acte est requis.", nameof(input));
        if (input.Cost < 0)
            throw new ArgumentException("Le coût de l'acte ne peut pas être négatif.", nameof(input));
        if (input.UnitCost < 0)
            throw new ArgumentException("Le prix unitaire de l'acte ne peut pas être négatif.", nameof(input));

        Id = id;
        DentalRecordId = dentalRecordId;
        ProcedureName = input.ProcedureName.Trim();
        Cost = InvoiceCalculator.RoundMoney(input.Cost);
        UnitCost = input.UnitCost.HasValue ? InvoiceCalculator.RoundMoney(input.UnitCost.Value) : null;
        ProcedureTypeId = input.ProcedureTypeId;
        ResultingCondition = input.ResultingCondition == ToothCondition.Sain ? null : input.ResultingCondition;
        Surfaces = NormalizeSurfaces(input.Surfaces);
        Note = string.IsNullOrWhiteSpace(input.Note) ? null : input.Note.Trim();
        CreatedAt = DateTime.UtcNow;

        if (input.ToothNumbers != null)
        {
            foreach (var tooth in input.ToothNumbers.Distinct())
            {
                if (!FdiTooth.IsValid(tooth))
                    throw new ArgumentException($"Numéro de dent invalide : {tooth}.", nameof(input));
                _toothNumbers.Add(tooth);
            }
        }

        // A mouth-level act (no teeth) can only be a flat fee — there is nothing to multiply.
        IsPerTooth = input.IsPerTooth && _toothNumbers.Count > 0;

        /*
         * ⚠️ **Normalised, never refused** — the same shape as `IsPerTooth` above and `ResultingCondition`'s
         * `Sain` fold, and for the same reason: the client legitimately holds a stale answer.
         *
         * A dentist marks 15 as a pontique, then changes the act's état from « Bridge » to « Couronne », or
         * removes 15 from the act's teeth. Both leave a pontique list that no longer describes anything, and
         * both are ordinary edits made in the fiche's own form. Throwing would turn them into a 400 the user
         * did nothing to earn; keeping them would chart a pontique on a tooth the act does not treat, or a
         * bridge shape on an act that is not a bridge. Intersecting and clearing is the only reading that can
         * be wrong about nothing.
         */
        if (BridgeCharting.IsUnit(ResultingCondition) && input.PonticToothNumbers is not null)
        {
            foreach (var tooth in input.PonticToothNumbers.Distinct())
            {
                if (_toothNumbers.Contains(tooth)) _ponticToothNumbers.Add(tooth);
            }
        }
    }

    // ⚠️ Delegates to ToothSurfaces: this method was byte-for-byte identical in ToothState and
    // DentalRecordAct, two entry points for the same string from the same picker.
    private static string? NormalizeSurfaces(string? surfaces) =>
        ToothSurfaces.Normalize(surfaces, nameof(surfaces));
}

/// <summary>
/// One act requested when (re)building a <see cref="DentalRecord"/>'s act list — a parameter object rather
/// than a positional tuple, because the ten correlated fields (incl. the per-tooth pricing provenance) are
/// unreadable inline. Validated by the <see cref="DentalRecordAct"/> constructor it feeds.
/// </summary>
/// <param name="PonticToothNumbers">
/// The subset of <paramref name="ToothNumbers"/> that are pontiques — see
/// <see cref="DentalRecordAct.PonticToothNumbers"/>. ⚠️ Optional and **last**, with a default, so the eleven
/// existing construction sites keep compiling: they build acts that are not bridges, and adding a required
/// positional parameter to a record every test fixture builds would have been a hundred edits to say « none ».
/// </param>
public sealed record DentalRecordActInput(
    Guid? ProcedureTypeId,
    string ProcedureName,
    decimal Cost,
    decimal? UnitCost,
    bool IsPerTooth,
    IReadOnlyList<int> ToothNumbers,
    ToothCondition? ResultingCondition,
    string? Surfaces,
    string? Note,
    IReadOnlyList<int>? PonticToothNumbers = null);
