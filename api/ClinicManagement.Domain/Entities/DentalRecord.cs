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
        List<string>? importantNotes = null)
    {
        if (amountPaid < 0)
            throw new ArgumentException("Amount paid cannot be negative", nameof(amountPaid));

        Id = id;
        PatientId = patientId;
        InterventionDate = interventionDate;
        AmountPaid = amountPaid;
        IsAdultTeeth = isAdultTeeth;

        if (notes != null)
            _notes.AddRange(notes.Where(n => !string.IsNullOrWhiteSpace(n)).Select(n => n.Trim()));
        if (importantNotes != null)
            _importantNotes.AddRange(importantNotes.Where(n => !string.IsNullOrWhiteSpace(n)).Select(n => n.Trim()));

        CreatedAt = DateTime.UtcNow;
    }

    /// <summary>Replace all acts, then recompute the derived cost / procedure summary / flat tooth list.</summary>
    public void SetActs(IEnumerable<(Guid? procedureTypeId, string procedureName, decimal cost, IReadOnlyList<int> toothNumbers, ToothCondition? resultingCondition, string? surfaces, string? note)> acts)
    {
        _acts.Clear();
        _teeth.Clear();
        var teethSeen = new HashSet<int>();

        foreach (var a in acts)
        {
            _acts.Add(new DentalRecordAct(
                Guid.NewGuid(), Id, a.procedureName, a.cost, a.toothNumbers,
                a.procedureTypeId, a.resultingCondition, a.surfaces, a.note));

            foreach (var tooth in a.toothNumbers)
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
