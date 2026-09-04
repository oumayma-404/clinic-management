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

    private readonly List<ProcedureStepTemplate> _defaultSteps = new();

    /// <summary>
    /// The clinical steps this act is <b>proposed</b> as when it is added to a devis — « Préparation, Empreinte,
    /// Scellement définitif » for a bridge. <b>Empty is the ordinary case</b>: an act done in one séance has
    /// none, which is every act in the catalogue until somebody fills these in.
    /// <para>
    /// A suggestion, never a constraint: <c>TreatmentPlan.SetItemSteps</c> owns the real steps and they are
    /// edited per case. See <see cref="ProcedureStepTemplate"/> for why the catalogue proposes rather than
    /// becoming hierarchical itself.
    /// </para>
    /// </summary>
    public IReadOnlyList<ProcedureStepTemplate> DefaultSteps => _defaultSteps.AsReadOnly();
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
        string? category = null,
        IEnumerable<ProcedureStepTemplate>? defaultSteps = null)
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

        // Assigned through the shared validator rather than SetDefaultSteps, so a seeded act does not come into
        // the world already carrying an UpdatedAt — the catalogue screen would report every starter act as
        // « modifié » on the day the clinic was created.
        if (defaultSteps != null)
        {
            _defaultSteps.AddRange(ValidateSteps(defaultSteps));
        }
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

    /// <summary>
    /// Replace the act's suggested step list wholesale — the editor posts the list it is showing, matching
    /// <see cref="SetMaterials"/> and <c>TreatmentPlan.SetItems</c>.
    /// <para>
    /// An empty list clears the suggestion, which is a real answer (« cet acte se fait en une séance ») and why
    /// the update DTO has to distinguish it from « unchanged ». Duplicate labels are allowed on purpose: a
    /// protocol legitimately repeats « Contrôle ».
    /// </para>
    /// </summary>
    public void SetDefaultSteps(IEnumerable<ProcedureStepTemplate> steps)
    {
        var cleaned = ValidateSteps(steps);

        _defaultSteps.Clear();
        _defaultSteps.AddRange(cleaned);
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Validate a whole template list before anything is mutated — a half-applied edit would leave the act
    /// proposing some of the new steps and some of the old. Shared by <see cref="SetDefaultSteps"/> and the
    /// constructor, so a seeded act and an edited one are held to one set of rules.
    /// </summary>
    private static List<ProcedureStepTemplate> ValidateSteps(IEnumerable<ProcedureStepTemplate> steps)
    {
        ArgumentNullException.ThrowIfNull(steps);

        var requested = steps.ToList();
        if (requested.Count > TreatmentPlanItemStep.MaxStepsPerItem)
        {
            throw new ArgumentException(
                $"Un acte ne peut pas proposer plus de {TreatmentPlanItemStep.MaxStepsPerItem} étapes.",
                nameof(steps));
        }

        var cleaned = new List<ProcedureStepTemplate>();
        foreach (var step in requested)
        {
            if (string.IsNullOrWhiteSpace(step.Label))
            {
                throw new ArgumentException("Le libellé d'une étape est requis.", nameof(steps));
            }

            var label = step.Label.Trim();
            if (label.Length > TreatmentPlanItemStep.MaxLabelLength)
            {
                throw new ArgumentException(
                    $"Le libellé d'une étape ne peut pas dépasser {TreatmentPlanItemStep.MaxLabelLength} caractères.",
                    nameof(steps));
            }
            // Same band as DefaultDurationMinutes — a step is one sitting at the chair.
            if (step.DurationMinutes is <= 0 or >= 480)
            {
                throw new ArgumentException(
                    "La durée d'une étape doit être comprise entre 1 et 479 minutes.", nameof(steps));
            }

            /*
             * ⚠️ The interval has to be copied across explicitly, and forgetting it is silent in both
             * directions: this rebuild dropped `MinDaysAfterPrevious`, so every seeded protocol's rhythm was
             * discarded on the way into the catalogue *and* a délai typed in the steps editor was accepted,
             * saved without it, and read back blank. Measured: 50 catalogue rows carried a protocol and 0
             * carried an interval, which makes the worklist unable to tell « pas encore due » from
             * « oubliée » — the distinction the interval exists to draw.
             */
            cleaned.Add(new ProcedureStepTemplate(
                label,
                step.DurationMinutes,
                TreatmentPlanItemStep.GuardInterval(step.MinDaysAfterPrevious)));
        }

        return cleaned;
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

