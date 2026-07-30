using ClinicManagement.Domain.Common;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Services;

namespace ClinicManagement.Domain.Entities;

/// <summary>
/// A dental-record session: a list of <see cref="DentalRecordAct"/> (the acts done), from which the record's
/// <see cref="ProcedureType"/> summary, <see cref="Cost"/>, and flat <see cref="Teeth"/> list are DERIVED
/// (recomputed in <see cref="SetActs"/>). Kept as stored columns for display / AI summary / the invoice bridge.
/// </summary>
public class DentalRecord : Entity<Guid>
{
    private const int ProcedureSummaryMaxLength = 200;

    public Guid PatientId { get; private set; }
    public DateTime InterventionDate { get; private set; }

    /// <summary>
    /// The appointment this session documents, when it was recorded from one. Null for a fiche entered without going
    /// through the agenda.
    ///
    /// <para>
    /// ⚠️ The <c>DentalRecords.AppointmentId</c> column and its index have existed since
    /// <c>AddDentalRecordAppointmentId</c> (2026-07-17) — but the property did not, so the EF model never declared it,
    /// nothing populated it, and every row held NULL. The id *was* already reaching the write side: the create command
    /// takes it, uses it post-commit to mark the visit completed and cancel its post-visit prompt, then discards it.
    /// Storing it is what finally makes « quelles séances n'ont pas encore de fiche ? » answerable — the same defect,
    /// and the same repair, as <c>Invoice.AppointmentId</c>.
    /// </para>
    ///
    /// <para>
    /// It is deliberately NOT a required link and NOT unique: a visit may legitimately produce more than one fiche,
    /// and a fiche may exist with no appointment behind it.
    /// </para>
    /// </summary>
    public Guid? AppointmentId { get; private set; }

    /// <summary>Derived summary of the acts' procedure names (recomputed in <see cref="SetActs"/>).</summary>
    public string ProcedureType { get; private set; } = string.Empty;
    /// <summary>Derived total = sum of act costs (recomputed in <see cref="SetActs"/>).</summary>
    public decimal Cost { get; private set; }
    public decimal AmountPaid { get; private set; }

    private readonly List<string> _notes = new();
    public IReadOnlyList<string> Notes => _notes.AsReadOnly();
    private readonly List<string> _importantNotes = new();
    public IReadOnlyList<string> ImportantNotes => _importantNotes.AsReadOnly();

    public bool IsAdultTeeth { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? UpdatedAt { get; private set; }

    // Navigation
    public Patient Patient { get; private set; } = null!;
    private readonly List<DentalRecordTooth> _teeth = new();
    public IReadOnlyCollection<DentalRecordTooth> Teeth => _teeth.AsReadOnly();
    private readonly List<DentalRecordAct> _acts = new();
    public IReadOnlyCollection<DentalRecordAct> Acts => _acts.AsReadOnly();

    private DentalRecord() { } // For EF Core

    public DentalRecord(
        Guid id,
        Guid patientId,
        DateTime interventionDate,
        decimal amountPaid,
        bool isAdultTeeth,
        List<string>? notes = null,
        List<string>? importantNotes = null,
        Guid? appointmentId = null)
    {
        if (amountPaid < 0)
            throw new ArgumentException("Amount paid cannot be negative", nameof(amountPaid));

        Id = id;
        PatientId = patientId;
        InterventionDate = interventionDate;
        AmountPaid = amountPaid;
        IsAdultTeeth = isAdultTeeth;
        AppointmentId = appointmentId;

        if (notes != null)
            _notes.AddRange(notes.Where(n => !string.IsNullOrWhiteSpace(n)).Select(n => n.Trim()));
        if (importantNotes != null)
            _importantNotes.AddRange(importantNotes.Where(n => !string.IsNullOrWhiteSpace(n)).Select(n => n.Trim()));

        CreatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Replace all acts, then recompute the derived cost / procedure summary / flat tooth list.
    /// NOTE: every act is rebuilt with a fresh id, so <see cref="DentalRecordAct.Id"/> is NOT stable across
    /// updates — nothing may hold a foreign key to it. Downstream links target the record instead
    /// (e.g. <c>InvoiceLine.DentalRecordId</c>, <c>TreatmentPlanItem.LinkedDentalRecordId</c>).
    /// </summary>
    public void SetActs(IEnumerable<DentalRecordActInput> acts)
    {
        _acts.Clear();
        _teeth.Clear();
        var teethSeen = new HashSet<int>();

        foreach (var a in acts)
        {
            _acts.Add(new DentalRecordAct(Guid.NewGuid(), Id, a));

            foreach (var tooth in a.ToothNumbers ?? Array.Empty<int>())
            {
                if (teethSeen.Add(tooth))
                    _teeth.Add(new DentalRecordTooth(Guid.NewGuid(), Id, tooth));
            }
        }

        RecomputeDerived();
        UpdatedAt = DateTime.UtcNow;
    }

    public void Update(
        DateTime interventionDate,
        decimal amountPaid,
        List<string>? notes = null,
        List<string>? importantNotes = null)
    {
        if (amountPaid < 0)
            throw new ArgumentException("Amount paid cannot be negative", nameof(amountPaid));

        InterventionDate = interventionDate;
        AmountPaid = amountPaid;

        if (notes != null)
        {
            _notes.Clear();
            _notes.AddRange(notes.Where(n => !string.IsNullOrWhiteSpace(n)).Select(n => n.Trim()));
        }
        if (importantNotes != null)
        {
            _importantNotes.Clear();
            _importantNotes.AddRange(importantNotes.Where(n => !string.IsNullOrWhiteSpace(n)).Select(n => n.Trim()));
        }

        UpdatedAt = DateTime.UtcNow;
    }

    private void RecomputeDerived()
    {
        Cost = InvoiceCalculator.RoundMoney(_acts.Sum(a => a.Cost));

        var names = _acts
            .Select(a => a.ProcedureName)
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Distinct()
            .ToList();
        var summary = names.Count > 0 ? string.Join(", ", names) : string.Empty;
        ProcedureType = summary.Length > ProcedureSummaryMaxLength
            ? summary[..(ProcedureSummaryMaxLength - 1)] + "…"
            : summary;
    }
}
