using ClinicManagement.Domain.Common;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Services;

namespace ClinicManagement.Domain.Entities;

/// <summary>
/// A dental treatment plan (aggregate root, clinic-scoped) — the spine of the dental core. A Draft holds
/// planned act lines + an optional installment schedule (échéancier) and renders as a devis (quote). It is
/// <c>Accept</c>ed to receive a per-clinic-per-year number (<c>AAAA-NNNN</c>, separate from invoices) and
/// freeze; acts are then marked done and installment payments recorded, moving it through
/// InProgress → Completed, or it can be Cancelled. Not a fiscal document (no VAT/timbre/TTN).
/// </summary>
public class TreatmentPlan : AggregateRoot<Guid>
{
    public Guid ClinicId { get; private set; }
    public Guid PatientId { get; private set; }

    /// <summary>Sequential number <c>AAAA-NNNN</c>; null while a draft (assigned at acceptance).</summary>
    public string? Number { get; private set; }
    public TreatmentPlanStatus Status { get; private set; }
    public string Title { get; private set; } = string.Empty;
    public string? Notes { get; private set; }
    public DateTime? AcceptedDate { get; private set; }
    public string? CancellationReason { get; private set; }

    /// <summary>Sum of the planned act costs (TND millimes) — the devis total.</summary>
    public decimal TotalPlanned { get; private set; }

    public DateTime CreatedAt { get; private set; }
    public DateTime? UpdatedAt { get; private set; }

    private readonly List<TreatmentPlanItem> _items = new();
    public IReadOnlyCollection<TreatmentPlanItem> Items => _items.AsReadOnly();

    private readonly List<Installment> _installments = new();
    public IReadOnlyCollection<Installment> Installments => _installments.AsReadOnly();

    public decimal AmountPaid => InvoiceCalculator.RoundMoney(_installments.Sum(i => i.AmountPaid));
    public decimal Outstanding => Math.Max(0m, TotalPlanned - AmountPaid);
    public bool CanBeDeleted => Status == TreatmentPlanStatus.Draft;

    private TreatmentPlan() { } // For EF Core

    public TreatmentPlan(Guid id, Guid clinicId, Guid patientId, string title, string? notes = null)
    {
        if (clinicId == Guid.Empty)
            throw new ArgumentException("Le cabinet est requis.", nameof(clinicId));
        if (patientId == Guid.Empty)
            throw new ArgumentException("Le patient est requis.", nameof(patientId));
        if (string.IsNullOrWhiteSpace(title))
            throw new ArgumentException("Le titre du plan est requis.", nameof(title));

        Id = id;
        ClinicId = clinicId;
        PatientId = patientId;
        Title = title.Trim();
        Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        Status = TreatmentPlanStatus.Draft;
        CreatedAt = DateTime.UtcNow;
        RecomputeTotal();
    }

    /// <summary>Update the title / notes. Draft only.</summary>
    public void UpdateDetails(string title, string? notes)
    {
        EnsureDraft();
        if (string.IsNullOrWhiteSpace(title))
            throw new ArgumentException("Le titre du plan est requis.", nameof(title));

        Title = title.Trim();
        Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        Touch();
    }

    /// <summary>Replace all planned act lines. Draft only. Clears any installment schedule (totals change).</summary>
    public void SetItems(IEnumerable<(string designationFr, decimal plannedCost, Guid? dentalActCodeId, string? codeActe, IReadOnlyList<int> toothNumbers)> items)
    {
        EnsureDraft();
        _items.Clear();
        _installments.Clear();
        foreach (var (designationFr, plannedCost, dentalActCodeId, codeActe, toothNumbers) in items)
        {
            _items.Add(new TreatmentPlanItem(Guid.NewGuid(), Id, designationFr, plannedCost, dentalActCodeId, codeActe, toothNumbers));
        }
        RecomputeTotal();
        Touch();
    }

    /// <summary>
    /// Replace the installment schedule (échéancier). Draft only. If any installments are given, their
    /// amounts must sum exactly to the total planned cost (the caller lands the millime remainder on the
    /// last installment). An empty schedule is allowed (no formal plan; then no installment payments).
    /// </summary>
    public void SetInstallments(IEnumerable<(DateTime dueDate, decimal amount)> installments)
    {
        EnsureDraft();
        var list = installments.ToList();
        _installments.Clear();
        if (list.Count == 0)
        {
            Touch();
            return;
        }

        var sum = InvoiceCalculator.RoundMoney(list.Sum(i => i.amount));
        if (sum != TotalPlanned)
            throw new InvalidOperationException("Le total des échéances doit être égal au coût total planifié du devis.");

        foreach (var (dueDate, amount) in list)
        {
            _installments.Add(new Installment(Guid.NewGuid(), Id, dueDate, amount));
        }
        Touch();
    }

    /// <summary>
    /// Accept the devis: assign its (externally computed, unique) sequential number and move it to Accepted.
    /// Requires at least one act.
    /// </summary>
    public void Accept(string number)
    {
        EnsureDraft();
        if (string.IsNullOrWhiteSpace(number))
            throw new ArgumentException("Le numéro du devis est requis.", nameof(number));
        if (_items.Count == 0)
            throw new InvalidOperationException("Un plan doit comporter au moins un acte pour être accepté.");

        Number = number.Trim();
        AcceptedDate = DateTime.UtcNow;
        Status = TreatmentPlanStatus.Accepted;

        // Ensure the accepted plan is payable: a devis with no échéancier gets a single lump-sum installment
        // for the full planned total, due at acceptance — otherwise Outstanding (derived from installments)
        // would be stuck at the total forever with no way to record a payment.
        if (_installments.Count == 0 && TotalPlanned > 0m)
        {
            _installments.Add(new Installment(Guid.NewGuid(), Id, AcceptedDate.Value, TotalPlanned));
        }

        Touch();
    }

    /// <summary>Reassign the number on an accepted plan — only to resolve a concurrent numbering collision.</summary>
    public void SetAcceptedNumber(string number)
    {
        if (Status == TreatmentPlanStatus.Draft)
            throw new InvalidOperationException("Le numéro ne peut être attribué qu'à un plan accepté.");
        if (string.IsNullOrWhiteSpace(number))
            throw new ArgumentException("Le numéro du devis est requis.", nameof(number));

        Number = number.Trim();
        Touch();
    }

    /// <summary>
    /// Record a payment against one installment. Allowed on an accepted, in-progress <b>or completed</b> plan —
    /// see <see cref="EnsurePayable"/>. A payment never re-opens a completed plan.
    /// </summary>
    public void RecordInstallmentPayment(Guid installmentId, decimal amount, PaymentMethod method, DateTime paidOn)
    {
        EnsurePayable();
        var installment = _installments.FirstOrDefault(i => i.Id == installmentId)
            ?? throw new InvalidOperationException("Échéance introuvable.");

        installment.RecordPayment(amount, method, paidOn);
        if (Status == TreatmentPlanStatus.Accepted)
            Status = TreatmentPlanStatus.InProgress;
        Touch();
    }

    /// <summary>
    /// Mark a planned act as carried out, optionally linking the dental record that recorded it. When this was
    /// the last outstanding act the plan closes itself.
    /// <para>
    /// The auto-close rule lives here, not in a handler, so every path behaves identically: previously only
    /// <c>MarkTreatmentPlanItemDoneCommand</c> auto-closed while the record-driven path
    /// (<c>DentalRecordLinker</c>) did not — and since that command has no UI caller, a fully-treated plan
    /// never actually reached « Terminé » on its own.
    /// </para>
    /// </summary>
    public void MarkItemDone(Guid itemId, DateTime doneOn, Guid? linkedDentalRecordId)
    {
        EnsureActive();
        var item = _items.FirstOrDefault(i => i.Id == itemId)
            ?? throw new InvalidOperationException("Acte introuvable.");

        item.MarkDone(doneOn, linkedDentalRecordId);
        if (Status == TreatmentPlanStatus.Accepted)
            Status = TreatmentPlanStatus.InProgress;

        // EnsureActive + the bump above leave the plan InProgress, so Complete() can never throw here.
        if (_items.Count > 0 && _items.All(i => i.Status == TreatmentPlanItemStatus.Done))
        {
            Complete();
        }

        Touch();
    }

    /// <summary>Close the plan once every act has been carried out.</summary>
    public void Complete()
    {
        if (Status != TreatmentPlanStatus.Accepted && Status != TreatmentPlanStatus.InProgress)
            throw new InvalidOperationException("Seul un plan accepté ou en cours peut être clôturé.");
        if (_items.Any(i => i.Status != TreatmentPlanItemStatus.Done))
            throw new InvalidOperationException("Tous les actes doivent être réalisés avant de clôturer le plan.");

        Status = TreatmentPlanStatus.Completed;
        Touch();
    }

    /// <summary>Cancel an accepted/in-progress plan (motif required). A draft is deleted, not cancelled.</summary>
    public void Cancel(string reason)
    {
        if (Status == TreatmentPlanStatus.Draft)
            throw new InvalidOperationException("Un brouillon se supprime, il ne s'annule pas.");
        if (Status == TreatmentPlanStatus.Cancelled)
            throw new InvalidOperationException("Le plan est déjà annulé.");
        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("Le motif d'annulation est requis.", nameof(reason));

        CancellationReason = reason.Trim();
        Status = TreatmentPlanStatus.Cancelled;
        Touch();
    }

    private void RecomputeTotal() => TotalPlanned = InvoiceCalculator.RoundMoney(_items.Sum(i => i.PlannedCost));

    private void EnsureDraft()
    {
        if (Status != TreatmentPlanStatus.Draft)
            throw new InvalidOperationException("Seul un plan au statut brouillon peut être modifié.");
    }

    private void EnsureActive()
    {
        if (Status != TreatmentPlanStatus.Accepted && Status != TreatmentPlanStatus.InProgress)
            throw new InvalidOperationException("Le plan doit être accepté pour cette opération.");
    }

    /// <summary>
    /// Money may still be collected on a <c>Completed</c> plan. « Terminé » means every act was carried out,
    /// not that the patient has paid — treatment routinely finishes before the last échéance is collected, so
    /// closing the clinical track must never close the financial one. (Wider than <see cref="EnsureActive"/>,
    /// which still guards act completion.)
    /// </summary>
    private void EnsurePayable()
    {
        if (Status != TreatmentPlanStatus.Accepted
            && Status != TreatmentPlanStatus.InProgress
            && Status != TreatmentPlanStatus.Completed)
        {
            throw new InvalidOperationException("Le plan doit être accepté pour enregistrer un paiement.");
        }
    }

    private void Touch() => UpdatedAt = DateTime.UtcNow;
}
