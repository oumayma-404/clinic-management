using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.Features.ProcedureTypes;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.Appointments;

/// <summary>
/// One act as the client asks for it: a catalog id, plus the devis step it carries out when the séance was built
/// from a treatment plan. The act's name, duration and colour are read from the catalog server-side — the client
/// sends what it chose, never what it thinks the act is called or how long it takes.
/// <para>
/// ⚠️ <see cref="AgreedCost"/> is the deliberate exception, and the only one: a negotiated price is a fact the
/// client is the sole source of, because it was agreed on the telephone. It is validated here rather than
/// trusted.
/// </para>
/// </summary>
public class AppointmentProcedureRequest
{
    /// <summary>
    /// The catalog act. **Nullable**, and only when <see cref="TreatmentPlanItemId"/> is set: a hand-typed devis
    /// line has no <c>ProcedureType</c> behind it, and refusing such an act would mean a grouped séance could only
    /// carry the devis links of the acts that happen to match the catalogue — silently leaving the rest reading
    /// « À planifier ». The row then takes its name from the plan step's désignation.
    /// </summary>
    public Guid? ProcedureTypeId { get; set; }

    /// <summary>Optional devis step this act realises. Validated against the command's <c>TreatmentPlanId</c>.</summary>
    public Guid? TreatmentPlanItemId { get; set; }

    /// <summary>
    /// The price agreed for this act at this visit, in dinars — a <b>forfait</b>, not a per-tooth rate. Omit it
    /// (or send null) to leave the act at its catalogue tarif, which is what every booking that does not
    /// negotiate sends and what makes this field additive.
    ///
    /// <para>A patient who telephones and haggles is the whole reason this exists: the devis is the product's
    /// answer to a negotiated price, and it is too heavy for one act settled in one sentence. The figure the
    /// receptionist types here is carried into the fiche de soins for that visit, so the amount quoted is the
    /// amount billed.</para>
    /// </summary>
    public decimal? AgreedCost { get; set; }
}

/// <summary>
/// Turns a requested list of acts into validated <see cref="AppointmentProcedureInput"/>s — shared by create and
/// update so a booking and an edit cannot apply different rules to the same list.
/// </summary>
public static class AppointmentProcedureSelection
{
    /// <summary>
    /// Upper bound on the acts of one séance. Not a business rule so much as a guard on an unbounded loop of
    /// catalog reads: twelve acts in one visit is already far past anything real, and the alternative (no cap) lets
    /// one request issue arbitrarily many queries.
    /// </summary>
    public const int MaxProceduresPerAppointment = 12;

    /// <summary>
    /// Refusals for a negotiated price. The ceiling is <c>ProcedureTypeRefusals.MaxCost</c> deliberately rather
    /// than a figure of its own: it is the <c>decimal(18,3)</c> column's own limit, the same column, and a second
    /// constant here would be a second authority over one fact. Without it PostgreSQL refuses the write and the
    /// dentist reads an English EF sentence — the exact failure that class was written for.
    /// </summary>
    public const string AgreedCostNegative = "Le prix convenu pour un acte ne peut pas être négatif.";

    public const string AgreedCostTooLarge =
        "Le prix convenu pour un acte est trop élevé. Saisissez un montant en dinars, par exemple 120,000.";

    /// <summary>
    /// The effective list of acts, reconciling the multi-act field with the single-act one.
    /// <para>
    /// Both are accepted deliberately: <c>procedures</c> is what the booking dialogs now send, while
    /// <c>procedureTypeId</c> is still what the AI dispatcher, the recurring-series expansion and any existing
    /// integration send — and a one-act booking should not have to learn a list shape. When both arrive the list
    /// wins, since it is the newer and strictly more expressive of the two.
    /// </para>
    /// </summary>
    public static List<AppointmentProcedureRequest> Reconcile(
        List<AppointmentProcedureRequest>? procedures,
        Guid? singleProcedureTypeId,
        Guid? singleTreatmentPlanItemId)
    {
        if (procedures is { Count: > 0 })
        {
            return procedures;
        }

        // A single act, OR a devis step with no catalog act behind it. The second case matters: booking a
        // hand-typed plan line sends only `treatmentPlanItemId`, and returning an empty list for it would leave
        // `SetProcedures` deriving a null link — losing the very thing the booking was for.
        if (singleProcedureTypeId.HasValue || singleTreatmentPlanItemId.HasValue)
        {
            return new List<AppointmentProcedureRequest>
            {
                new()
                {
                    ProcedureTypeId = singleProcedureTypeId,
                    TreatmentPlanItemId = singleTreatmentPlanItemId,
                },
            };
        }

        return new List<AppointmentProcedureRequest>();
    }

    /// <summary>
    /// Validate each act against the caller's clinic and read its name/duration/colour from the catalog.
    /// <para>
    /// Every act is checked, not just the first: an unvalidated second act would be exactly the cross-clinic hole
    /// the single-act path closes, and an inactive one would put a retired procedure onto a future visit.
    /// </para>
    /// </summary>
    /// <param name="planItemDesignations">
    /// Désignation per validated devis step, from <c>AppointmentPlanLink.ValidateManyAsync</c> — the name a
    /// link-only act (no catalog procedure) is labelled with.
    /// </param>
    public static async Task<Result<List<AppointmentProcedureInput>>> ResolveAsync(
        IProcedureTypeRepository procedureTypeRepository,
        Guid clinicId,
        IReadOnlyCollection<AppointmentProcedureRequest> requested,
        IReadOnlyDictionary<Guid, string> planItemDesignations,
        CancellationToken cancellationToken)
    {
        if (requested.Count > MaxProceduresPerAppointment)
        {
            return Result<List<AppointmentProcedureInput>>.Failure(
                $"Un rendez-vous ne peut pas comporter plus de {MaxProceduresPerAppointment} actes.");
        }

        var inputs = new List<AppointmentProcedureInput>();
        var seen = new HashSet<Guid>();

        foreach (var item in requested)
        {
            // Checked for every act, catalogue-backed or link-only, and before anything else about the row: a
            // price refused only on the branch that happens to be read first is a price accepted on the other.
            if (item.AgreedCost is < 0)
            {
                return Result<List<AppointmentProcedureInput>>.Failure(AgreedCostNegative);
            }
            if (item.AgreedCost > ProcedureTypeRefusals.MaxCost)
            {
                return Result<List<AppointmentProcedureInput>>.Failure(AgreedCostTooLarge);
            }

            // Link-only act: no catalogue entry, so no duration or colour either. It exists to carry the devis
            // link, and the séance's total duration simply does not count it — which is honest, since nothing
            // anywhere knows how long a hand-typed plan line takes.
            if (!item.ProcedureTypeId.HasValue)
            {
                if (!item.TreatmentPlanItemId.HasValue)
                {
                    return Result<List<AppointmentProcedureInput>>.Failure("Type de procédure introuvable.");
                }

                inputs.Add(new AppointmentProcedureInput(
                    null,
                    planItemDesignations.TryGetValue(item.TreatmentPlanItemId.Value, out var designation)
                        ? designation
                        : "Acte du devis",
                    null,
                    null,
                    item.AgreedCost,
                    item.TreatmentPlanItemId));
                continue;
            }

            if (item.ProcedureTypeId.Value == Guid.Empty)
            {
                return Result<List<AppointmentProcedureInput>>.Failure("Type de procédure introuvable.");
            }

            var procedureType = await procedureTypeRepository.GetByIdAsync(item.ProcedureTypeId.Value, cancellationToken);
            if (procedureType == null || procedureType.ClinicId != clinicId)
            {
                return Result<List<AppointmentProcedureInput>>.Failure("Type de procédure introuvable.");
            }
            if (!procedureType.IsActive)
            {
                return Result<List<AppointmentProcedureInput>>.Failure(
                    $"Le type de procédure « {procedureType.Name} » n'est pas actif.");
            }

            // Refused with the act's name rather than deduped silently: the user picked it twice, and quietly
            // dropping one leaves them looking at a séance that does not match what they selected. Quantity per
            // tooth is the fiche de soins' job, not the agenda's.
            if (!seen.Add(procedureType.Id))
            {
                return Result<List<AppointmentProcedureInput>>.Failure(
                    $"L'acte « {procedureType.Name} » est déjà présent dans ce rendez-vous.");
            }

            inputs.Add(new AppointmentProcedureInput(
                procedureType.Id,
                procedureType.Name,
                procedureType.DefaultDurationMinutes,
                procedureType.Color.Value,
                // The client's figure, kept as sent — including null, which means « no negotiation » and leaves
                // the act at its tarif. Substituting `procedureType.DefaultCost` here would freeze today's
                // catalogue price onto the visit and make a later tarif change invisible to a booking nobody
                // negotiated.
                item.AgreedCost,
                item.TreatmentPlanItemId));
        }

        return Result<List<AppointmentProcedureInput>>.Success(inputs);
    }

    /// <summary>
    /// The devis steps a requested séance carries out — the set <c>AppointmentPlanLink.ValidateManyAsync</c> must
    /// validate, read from the **request** because its désignations are what <see cref="ResolveAsync"/> then needs.
    /// </summary>
    public static List<Guid> PlanItemIds(IEnumerable<AppointmentProcedureRequest> requested) =>
        requested.Where(i => i.TreatmentPlanItemId.HasValue)
            .Select(i => i.TreatmentPlanItemId!.Value)
            .Distinct()
            .ToList();
}
