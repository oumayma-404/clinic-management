using ClinicManagement.Domain.Common;
using ClinicManagement.Domain.ValueObjects;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Services;

namespace ClinicManagement.Domain.Entities;

public class ProcedureType : AggregateRoot<Guid>
{
    public Guid ClinicId { get; private set; }
    public string Name { get; private set; }
    public int DefaultDurationMinutes { get; private set; }
    public decimal? DefaultCost { get; private set; }
    public ColorHex Color { get; private set; }
    public string? Description { get; private set; }
    /// <summary>
    /// The clinical discipline this act belongs to (« Endodontie », « Prothèse fixe »); null = unfiled.
    /// <para>
    /// Open text, canonicalised through <see cref="ProcedureTypeCategories.Normalize"/> on every write — see that
    /// class for why an open set is the right call here and what keeps it from shredding into spelling variants.
    /// </para>
    /// <para>
    /// ⚠️ This is what <c>Description</c> used to hold. <c>ProcedureTypeCatalogSeed</c> passed each row's category
    /// into the constructor's <c>description</c> slot for want of anywhere better, so every seeded clinic had
    /// « Endodontie » sitting in a field its own form labels « Description (optionnel) » — read as a grouping hint
    /// by the act picker, which had to document that it was not allowed to trust it. The `AddProcedureTypeCategory`
    /// migration moves those values here and clears the descriptions it took them from.
    /// </para>
    /// </summary>
    public string? Category { get; private set; }
    /// <summary>Odontogram state a dental act of this procedure produces (null = no tooth-state change). Editable.</summary>
    public ToothCondition? ResultingCondition { get; private set; }
    public bool IsActive { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? UpdatedAt { get; private set; }

    // Navigation property
    public ICollection<Appointment> Appointments { get; private set; } = new List<Appointment>();

    /// <summary>
    /// What performing this act consumes from the stock room (AC-P4.9). <b>Opt-in per act</b> (AC-P4.11): an
    /// empty list is the majority case and consumes nothing, so an act that never had a list behaves exactly as
    /// it did before this existed.
    /// </summary>
    private readonly List<ProcedureTypeMaterial> _materials = new();
    public IReadOnlyCollection<ProcedureTypeMaterial> Materials => _materials.AsReadOnly();

    private ProcedureType() { } // For EF Core

    public ProcedureType(
        Guid id,
        Guid clinicId,
        string name,
        int defaultDurationMinutes,
        ColorHex color,
        string? description = null,
        decimal? defaultCost = null,
        ToothCondition? resultingCondition = null,
        string? category = null)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Name cannot be null or empty", nameof(name));

        if (defaultDurationMinutes <= 0)
            throw new ArgumentException("Default duration must be greater than 0", nameof(defaultDurationMinutes));

        if (defaultDurationMinutes >= 480)
            throw new ArgumentException("Default duration must be less than 480 minutes (8 hours)", nameof(defaultDurationMinutes));

        if (color == null)
            throw new ArgumentNullException(nameof(color));

        if (defaultCost.HasValue && defaultCost.Value < 0)
            throw new ArgumentException("Default cost cannot be negative", nameof(defaultCost));

        Id = id;
        ClinicId = clinicId;
        Name = name.Trim();
        DefaultDurationMinutes = defaultDurationMinutes;
        DefaultCost = defaultCost;
        Color = color;
        Description = description?.Trim();
        Category = ProcedureTypeCategories.Normalize(category);
        ResultingCondition = resultingCondition == ToothCondition.Sain ? null : resultingCondition;
        IsActive = true;
        CreatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Files the act under a discipline, or unfiles it when <paramref name="category"/> is blank.
    /// <para>
    /// Blank means <c>null</c>, not <c>""</c>: « unfiled » has to be one value, or a category filter and the
    /// grouped catalogue would each have to know about two spellings of nothing.
    /// </para>
    /// </summary>
    public void UpdateCategory(string? category)
    {
        Category = ProcedureTypeCategories.Normalize(category);
        UpdatedAt = DateTime.UtcNow;
    }

    public void UpdateResultingCondition(ToothCondition? resultingCondition)
    {
        ResultingCondition = resultingCondition == ToothCondition.Sain ? null : resultingCondition;
        UpdatedAt = DateTime.UtcNow;
    }

    public void UpdateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Name cannot be null or empty", nameof(name));

        Name = name.Trim();
        UpdatedAt = DateTime.UtcNow;
    }

    public void UpdateDefaultDuration(int defaultDurationMinutes)
    {
        if (defaultDurationMinutes <= 0)
            throw new ArgumentException("Default duration must be greater than 0", nameof(defaultDurationMinutes));

        if (defaultDurationMinutes >= 480)
            throw new ArgumentException("Default duration must be less than 480 minutes (8 hours)", nameof(defaultDurationMinutes));

        DefaultDurationMinutes = defaultDurationMinutes;
        UpdatedAt = DateTime.UtcNow;
    }

    public void UpdateColor(ColorHex color)
    {
        if (color == null)
            throw new ArgumentNullException(nameof(color));

        Color = color;
        UpdatedAt = DateTime.UtcNow;
    }

    public void UpdateDescription(string? description)
    {
        Description = description?.Trim();
        UpdatedAt = DateTime.UtcNow;
    }

    public void UpdateDefaultCost(decimal? defaultCost)
    {
        if (defaultCost.HasValue && defaultCost.Value < 0)
            throw new ArgumentException("Default cost cannot be negative", nameof(defaultCost));

        DefaultCost = defaultCost;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Replaces the act's material list wholesale (AC-P4.14, admin-edited from the act catalog). Whole-list
    /// replacement rather than add/remove because the editor posts the list it is showing — the same shape as
    /// <c>TreatmentPlan.SetItems</c> — and one stock item may appear only once, so a duplicate is a caller bug
    /// rather than something to silently merge.
    /// </summary>
    public void SetMaterials(IEnumerable<(Guid StockItemId, int QuantityPerAct)> materials)
    {
        if (materials == null)
            throw new ArgumentNullException(nameof(materials));

        var requested = materials.ToList();
        if (requested.Select(m => m.StockItemId).Distinct().Count() != requested.Count)
            throw new ArgumentException("Un même article ne peut apparaître qu'une fois dans la liste des consommables.", nameof(materials));

        _materials.Clear();
        foreach (var material in requested)
        {
            _materials.Add(new ProcedureTypeMaterial(Guid.NewGuid(), Id, material.StockItemId, material.QuantityPerAct));
        }

        UpdatedAt = DateTime.UtcNow;
    }

    public void Deactivate()
    {
        IsActive = false;
        UpdatedAt = DateTime.UtcNow;
    }

    public void Activate()
    {
        IsActive = true;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Checks if this procedure type is used by any future appointments.
    /// <para>
    /// Looks at the whole séance, not just its lead act: once an appointment can hold several acts, a procedure
    /// booked as the *second* act of a future visit is just as much in use, and matching only
    /// <c>Appointment.ProcedureTypeId</c> would hard-delete it out from under that booking.
    /// </para>
    /// </summary>
    public bool IsUsedByFutureAppointments(IEnumerable<Appointment> appointments)
    {
        var now = DateTime.UtcNow;
        return appointments.Any(apt =>
            (apt.ProcedureTypeId == Id || apt.Procedures.Any(p => p.ProcedureTypeId == Id)) &&
            apt.AppointmentDateTime > now &&
            apt.Status != AppointmentStatus.Cancelled &&
            apt.Status != AppointmentStatus.Completed);
    }
}

