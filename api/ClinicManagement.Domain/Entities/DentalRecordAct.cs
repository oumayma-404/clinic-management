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
    public decimal Cost { get; private set; }

    private readonly List<int> _toothNumbers = new();
    public IReadOnlyList<int> ToothNumbers => _toothNumbers.AsReadOnly();

    /// <summary>Resulting tooth state for the odontogram (null = no state change, e.g. cleaning/consultation).</summary>
    public ToothCondition? ResultingCondition { get; private set; }
    public string? Surfaces { get; private set; }
    public string? Note { get; private set; }
    public DateTime CreatedAt { get; private set; }

    private DentalRecordAct() { } // For EF Core

    public DentalRecordAct(
        Guid id,
        Guid dentalRecordId,
        string procedureName,
        decimal cost,
        IReadOnlyList<int> toothNumbers,
        Guid? procedureTypeId = null,
        ToothCondition? resultingCondition = null,
        string? surfaces = null,
        string? note = null)
    {
        if (string.IsNullOrWhiteSpace(procedureName))
            throw new ArgumentException("Le nom de l'acte est requis.", nameof(procedureName));
        if (cost < 0)
            throw new ArgumentException("Le coût de l'acte ne peut pas être négatif.", nameof(cost));

        Id = id;
        DentalRecordId = dentalRecordId;
        ProcedureName = procedureName.Trim();
        Cost = InvoiceCalculator.RoundMoney(cost);
        ProcedureTypeId = procedureTypeId;
        ResultingCondition = resultingCondition == ToothCondition.Sain ? null : resultingCondition;
        Surfaces = NormalizeSurfaces(surfaces);
        Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        CreatedAt = DateTime.UtcNow;

        if (toothNumbers != null)
        {
            foreach (var tooth in toothNumbers.Distinct())
            {
                if (!FdiTooth.IsValid(tooth))
                    throw new ArgumentException($"Numéro de dent invalide : {tooth}.", nameof(toothNumbers));
                _toothNumbers.Add(tooth);
            }
        }
    }

    private static string? NormalizeSurfaces(string? surfaces)
    {
        if (string.IsNullOrWhiteSpace(surfaces))
            return null;

        var normalized = surfaces.Trim().ToUpperInvariant();
        foreach (var c in normalized)
        {
            if ("MODVL".IndexOf(c) < 0)
                throw new ArgumentException($"Surface invalide : '{c}'. Valeurs autorisées : M, O, D, V, L.", nameof(surfaces));
        }
        return normalized;
    }
}
