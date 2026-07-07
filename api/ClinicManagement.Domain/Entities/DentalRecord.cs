using ClinicManagement.Domain.Common;

namespace ClinicManagement.Domain.Entities;

public class DentalRecord : Entity<Guid>
{
    public Guid PatientId { get; private set; }
    public DateTime InterventionDate { get; private set; }
    public string ProcedureType { get; private set; }
    public decimal Cost { get; private set; }
    public decimal AmountPaid { get; private set; }
    private readonly List<string> _notes = new();
    public IReadOnlyList<string> Notes => _notes.AsReadOnly();
    private readonly List<string> _importantNotes = new();
    public IReadOnlyList<string> ImportantNotes => _importantNotes.AsReadOnly();
    public bool IsAdultTeeth { get; private set; } // True for adult, false for child/baby teeth
    public DateTime CreatedAt { get; private set; }
    public DateTime? UpdatedAt { get; private set; }

    // Navigation properties
    public Patient Patient { get; private set; } = null!;
    private readonly List<DentalRecordTooth> _teeth = new();
    public IReadOnlyCollection<DentalRecordTooth> Teeth => _teeth.AsReadOnly();

    private DentalRecord() { } // For EF Core

    public DentalRecord(
        Guid id,
        Guid patientId,
        DateTime interventionDate,
        string procedureType,
        decimal cost,
        decimal amountPaid,
        bool isAdultTeeth,
        List<string>? notes = null,
        List<string>? importantNotes = null)
    {
        if (string.IsNullOrWhiteSpace(procedureType))
            throw new ArgumentException("Procedure type cannot be null or empty", nameof(procedureType));

        if (cost < 0)
            throw new ArgumentException("Cost cannot be negative", nameof(cost));

        if (amountPaid < 0)
            throw new ArgumentException("Amount paid cannot be negative", nameof(amountPaid));

        Id = id;
        PatientId = patientId;
        InterventionDate = interventionDate;
        ProcedureType = procedureType.Trim();
        Cost = cost;
        AmountPaid = amountPaid;
        IsAdultTeeth = isAdultTeeth;
        
        if (notes != null)
        {
            _notes.AddRange(notes.Where(n => !string.IsNullOrWhiteSpace(n)).Select(n => n.Trim()));
        }
        
        if (importantNotes != null)
        {
            _importantNotes.AddRange(importantNotes.Where(n => !string.IsNullOrWhiteSpace(n)).Select(n => n.Trim()));
        }
        
        CreatedAt = DateTime.UtcNow;
    }

    public void AddTooth(int toothNumber)
    {
        if (_teeth.Any(t => t.ToothNumber == toothNumber))
            return; // Tooth already added

        var tooth = new DentalRecordTooth(
            Guid.NewGuid(),
            Id,
            toothNumber);
        _teeth.Add(tooth);
        UpdatedAt = DateTime.UtcNow;
    }

    public void RemoveTooth(int toothNumber)
    {
        var tooth = _teeth.FirstOrDefault(t => t.ToothNumber == toothNumber);
        if (tooth != null)
        {
            _teeth.Remove(tooth);
            UpdatedAt = DateTime.UtcNow;
        }
    }

    public void Update(
        DateTime interventionDate,
        string procedureType,
        decimal cost,
        decimal amountPaid,
        List<string>? notes = null,
        List<string>? importantNotes = null)
    {
        if (string.IsNullOrWhiteSpace(procedureType))
            throw new ArgumentException("Procedure type cannot be null or empty", nameof(procedureType));

        if (cost < 0)
            throw new ArgumentException("Cost cannot be negative", nameof(cost));

        if (amountPaid < 0)
            throw new ArgumentException("Amount paid cannot be negative", nameof(amountPaid));

        InterventionDate = interventionDate;
        ProcedureType = procedureType.Trim();
        Cost = cost;
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
}

