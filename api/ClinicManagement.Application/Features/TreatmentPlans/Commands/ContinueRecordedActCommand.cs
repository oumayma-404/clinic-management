using MediatR;
using Microsoft.Extensions.Logging;
using ClinicManagement.Application.Common;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Application.Features.Invoices;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.TreatmentPlans.Commands;

/// <summary>
/// Turn an act already carried out into a multi-séance treatment — « cette séance est la suite de celle du
/// 12 août ».
/// </summary>
/// <remarks>
/// <para>
/// The case the client described: an act quoted and started as a one-off, not finished, and the dentist now
/// booking the visit that finishes it. Before this the product had two doors — a devis written up front, and
/// attaching a séance to a devis that already exists — and neither covers work that never had a devis at all.
/// </para>
/// <para>
/// ⚠️ <b>THE MONEY RULE, and everything here follows from it: the devis owns only what has not been billed
/// yet.</b> With the fiche already on a note d'honoraires, that note keeps the money and the new plan is
/// <i>attached</i> to it (<see cref="Invoice.AttachToTreatmentPlan"/>); with no note, the plan owns the act's
/// fee and bills it once when the work is done. The attached case is what stops the double count:
/// <c>Accept</c> raises a lump-sum échéance for the plan's whole total, and « Solde patient » drops a plan that
/// is billed into an invoice — so without the link a 1 000 DT bridge already invoiced would be claimed twice,
/// once by the note (200 still owed) and once by the devis (1 000 « restant »).
/// </para>
/// <para>
/// ⚠️ <b>The 800 already collected is never replayed onto the plan.</b> A plan installment payment posts its own
/// caisse movement, so recording it a second time would show 1 600 in the till for 800 received — the class of
/// defect <c>reconcile-money</c> exists to find. The remaining 200 stays on its note, where « Solde patient »,
/// « Créances » and the caisse already carry it, and the continuation surfaces state it rather than re-modelling
/// it.
/// </para>
/// <para>
/// ⚠️ <b>Two steps, and the labels are deliberately generic.</b> Nothing knows how the finished work was
/// actually divided — a fiche records what was done, never a protocol — so inventing « Préparation / Empreinte »
/// would be a claim about a patient's mouth. « 1re séance » is marked done against the fiche that evidences it;
/// the second carries whatever the dentist typed. Both are editable afterwards in the ordinary steps dialog.
/// </para>
/// </remarks>
public class ContinueRecordedActCommand : IRequest<Result<TreatmentPlanDto>>
{
    public Guid DentalRecordId { get; set; }

    /// <summary>Which act of that fiche is being continued — a séance may hold several.</summary>
    public Guid ActId { get; set; }

    /// <summary>
    /// What the next séance is called. Optional; « Séance suivante » when the dentist did not say. Never
    /// inferred from a catalogue protocol — see the class note on why the steps are generic.
    /// </summary>
    public string? NextStepLabel { get; set; }

    /// <summary>
    /// What the work still to come is worth, as its <b>own</b> act on the devis. Optional; omitted or 0 means
    /// the remaining séance adds nothing to what was already quoted.
    /// <para>
    /// ⚠️ <b>Without it every retroactive continuation systematically under-prices.</b> The devis used to carry
    /// the original act's fee and nothing else, with no money field anywhere in the dialog — so live data shows
    /// « Extraction simple, 120 DT » whose next séance is « Pose de la prothèse ». A prosthesis is not part of an
    /// extraction's fee, and the only remedy was to amend the devis afterwards, which on a plan already bridged
    /// to a note put the added money out of reach of every collection path.
    /// </para>
    /// <para>
    /// A <b>second act</b> rather than a larger fee on the first, deliberately: the first act's price is what a
    /// patient was already quoted (and, on the billed path, what a numbered note already says), so raising it
    /// would contradict a document. A separate line prices the new work without touching the old.
    /// </para>
    /// </summary>
    public decimal? RemainingWorkCost { get; set; }

    /// <summary>What that second act is called on the devis. Defaults to the next séance's own label.</summary>
    public string? RemainingWorkLabel { get; set; }
}

public class ContinueRecordedActCommandHandler
    : IRequestHandler<ContinueRecordedActCommand, Result<TreatmentPlanDto>>
{
    private const string FirstStepLabel = "1re séance";
    private const string DefaultNextStepLabel = "Séance suivante";

    private readonly IDentalRecordRepository _recordRepository;
    private readonly ITreatmentPlanRepository _planRepository;
    private readonly IInvoiceRepository _invoiceRepository;
    private readonly IPatientRepository _patientRepository;
    private readonly IProcedureTypeRepository _procedureTypeRepository;
    private readonly ICurrentClinicResolver _clinicResolver;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<ContinueRecordedActCommandHandler> _logger;

    public ContinueRecordedActCommandHandler(
        IDentalRecordRepository recordRepository,
        ITreatmentPlanRepository planRepository,
        IInvoiceRepository invoiceRepository,
        IPatientRepository patientRepository,
        IProcedureTypeRepository procedureTypeRepository,
        ICurrentClinicResolver clinicResolver,
        IUnitOfWork unitOfWork,
        ILogger<ContinueRecordedActCommandHandler> logger)
    {
        _recordRepository = recordRepository;
        _planRepository = planRepository;
        _invoiceRepository = invoiceRepository;
        _patientRepository = patientRepository;
        _procedureTypeRepository = procedureTypeRepository;
        _clinicResolver = clinicResolver;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<Result<TreatmentPlanDto>> Handle(
        ContinueRecordedActCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var clinicResult = await _clinicResolver.GetClinicIdAsync(cancellationToken);
            if (clinicResult.IsFailure)
            {
                return Result<TreatmentPlanDto>.Failure(clinicResult.Error ?? "Cabinet introuvable.");
            }
            var clinicId = clinicResult.Value;

            var record = await _recordRepository.GetByIdAsync(request.DentalRecordId, cancellationToken);
            if (record == null || record.ClinicId != clinicId)
            {
                return Result<TreatmentPlanDto>.Failure("Fiche de soins introuvable.");
            }

            var act = record.Acts.FirstOrDefault(a => a.Id == request.ActId);
            if (act == null)
            {
                return Result<TreatmentPlanDto>.Failure("Acte introuvable sur cette fiche de soins.");
            }

            var patient = await _patientRepository.GetByIdAsync(record.PatientId, cancellationToken);
            if (patient == null || patient.ClinicId != clinicId)
            {
                return Result<TreatmentPlanDto>.Failure("Patient introuvable.");
            }

            /*
             * Already on a devis? Then this is not a continuation, it is a second devis over the same work, and
             * the two would disagree about how far along it is. Checked against the plan items AND their steps,
             * because a stepped act carries its evidence per step.
             */
            var existingPlans = await _planRepository.GetFilteredAsync(
                clinicId, patientId: record.PatientId, cancellationToken: cancellationToken);
            var alreadyTracked = existingPlans.Items
                .SelectMany(p => p.Items)
                .Any(i => i.LinkedDentalRecordId == record.Id
                          || i.Steps.Any(s => s.LinkedDentalRecordId == record.Id));
            if (alreadyTracked)
            {
                return Result<TreatmentPlanDto>.Failure(
                    "Cette séance fait déjà partie d'un traitement. Ouvrez le devis pour planifier la suite.");
            }

            /*
             * The note that already bills this fiche, if any. THE fork of the whole feature — see the class note.
             * A cancelled note bills nothing, so it does not count: the work is unbilled and the plan owns it.
             */
            var billingLinks = InvoiceLinkChoice.ByKey(
                (await _invoiceRepository.GetDentalRecordLinksAsync(clinicId, cancellationToken))
                    .Select(l => (l.DentalRecordId, l.InvoiceId, l.Number, l.Status)));
            Invoice? billingInvoice = null;
            if (billingLinks.TryGetValue(record.Id, out var billingLink))
            {
                billingInvoice = await _invoiceRepository.GetByIdAsync(billingLink.InvoiceId, cancellationToken);
                if (billingInvoice != null && billingInvoice.TreatmentPlanId.HasValue)
                {
                    // The note already speaks for another devis; attaching it again would put that plan back
                    // into « Solde patient » carrying a total nobody re-quoted.
                    return Result<TreatmentPlanDto>.Failure(
                        "La note d'honoraires de cette séance est déjà rattachée à un devis.");
                }
            }

            var designation = string.IsNullOrWhiteSpace(act.ProcedureName)
                ? "Acte"
                : act.ProcedureName.Trim();

            var remainingCost = request.RemainingWorkCost ?? 0m;
            if (remainingCost < 0m)
            {
                return Result<TreatmentPlanDto>.Failure("Le montant du travail restant ne peut pas être négatif.");
            }

            var nextLabel = string.IsNullOrWhiteSpace(request.NextStepLabel)
                ? DefaultNextStepLabel
                : request.NextStepLabel.Trim();

            var plan = new TreatmentPlan(Guid.NewGuid(), clinicId, record.PatientId, designation, null);
            // L9 — the work was this fiche's practitioner's, not the caller's. That is a fact on the record here,
            // unlike a devis written from scratch, so there is nothing to fall back to.
            plan.SetDoctor(record.DoctorId);

            var lines = new List<TreatmentPlanItemInput>
            {
                new(null, designation, act.Cost, act.ProcedureTypeId, act.ToothNumbers.ToList()),
            };

            // The work still to come, priced on its own line — see `RemainingWorkCost`. The teeth travel with it
            // (it is the same tooth being finished) while the catalogue link deliberately does not: this is not
            // another one of that act, it is the rest of this one.
            var remainingLabel = string.IsNullOrWhiteSpace(request.RemainingWorkLabel)
                ? nextLabel
                : request.RemainingWorkLabel.Trim();
            if (remainingCost > 0m)
            {
                lines.Add(new TreatmentPlanItemInput(
                    null, remainingLabel, remainingCost, null, act.ToothNumbers.ToList()));
            }

            plan.SetItems(lines);

            /*
             * The séances, handed in as CONFIRMED steps so the act's catalogue protocol is not applied over
             * them: an implant's six researched séances are the wrong answer about work that is already one
             * séance in, and `TreatmentPlanStepProtocol` treats a confirmed list as final for exactly this.
             *
             * With a priced remaining act the next séance belongs to THAT line, so the original act is one
             * finished séance and the new line is the one still to book — otherwise the séance to come would sit
             * on the act whose fee a note has already collected.
             */
            var confirmedSteps = remainingCost > 0m
                ? new List<IReadOnlyList<TreatmentPlanItemStepInput>?>
                {
                    new[] { new TreatmentPlanItemStepInput(null, FirstStepLabel, null) },
                    new[] { new TreatmentPlanItemStepInput(null, nextLabel, null) },
                }
                : new List<IReadOnlyList<TreatmentPlanItemStepInput>?>
                {
                    new[]
                    {
                        new TreatmentPlanItemStepInput(null, FirstStepLabel, null),
                        new TreatmentPlanItemStepInput(null, nextLabel, null),
                    },
                };

            /*
             * Everything below happens inside DevisNumbering's `persist`, which runs AFTER Accept() and the step
             * application and BEFORE the single SaveChanges — so the steps already have ids and the whole thing
             * commits as one transaction. It must be idempotent, because a devis-number collision replays it:
             * both writes below are guarded on their own effect.
             */
            var accepted = await DevisNumbering.AcceptAndSaveAsync(
                plan, clinicId, _planRepository, _procedureTypeRepository, _unitOfWork,
                async ct =>
                {
                    // The original act, always position 0 — the priced remaining line, when there is one, is 1.
                    var item = plan.Items.OrderBy(i => i.SequenceNumber).First();
                    var firstStep = item.Steps.FirstOrDefault(s => s.Label == FirstStepLabel);
                    if (firstStep != null && firstStep.DoneDate == null)
                    {
                        // Dated by the séance, not by today: the work happened when the fiche says it did, and
                        // « dernière séance » on the worklist reads this to decide what has gone quiet.
                        plan.MarkItemStepDone(item.Id, firstStep.Id, record.InterventionDate, record.Id);
                    }

                    billingInvoice?.AttachToTreatmentPlan(plan.Id);

                    await _planRepository.AddAsync(plan, ct);
                },
                confirmedSteps,
                _logger, cancellationToken);
            if (accepted.IsFailure)
            {
                return Result<TreatmentPlanDto>.Failure(accepted.Error!);
            }

            _logger.LogInformation(
                "Continued recorded act {ActId} of record {RecordId} as plan {PlanId} ({Number}); billed on {Invoice}",
                act.Id, record.Id, plan.Id, plan.Number, billingInvoice?.Id.ToString() ?? "none");

            var dto = plan.ToDto(patient.GetFullName());
            /*
             * The note this plan was attached to, echoed back. ToDto leaves the link null — it is « populated on
             * the query paths only », i.e. the ones running TreatmentPlanWorkflowProjection — and the browser
             * needs it immediately: the booking dialog puts this plan is act straight onto the seance, and the
             * acts picker decides from this field whether to quote the devis own « reste ». That figure is false
             * for a plan billed into a note (its auto-echeance will never see a payment) and would tell somebody
             * to collect money the patient has already handed over.
             */
            if (billingInvoice != null)
            {
                dto.LinkedInvoiceId = billingInvoice.Id;
                dto.LinkedInvoiceNumber = billingInvoice.Number;
                dto.LinkedInvoiceStatus = billingInvoice.Status.ToString();
                dto.LinkedInvoiceTotal = billingInvoice.TotalTtc;
                dto.LinkedInvoiceOutstanding = billingInvoice.Outstanding;
            }

            return Result<TreatmentPlanDto>.Success(dto);
        }
        catch (InvalidOperationException ex)
        {
            return Result<TreatmentPlanDto>.Failure(ex.Message);
        }
        catch (ArgumentException ex)
        {
            return Result<TreatmentPlanDto>.Failure(ex.Message);
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            _logger.LogError(ex, "Error continuing recorded act {ActId}", request.ActId);
            return Result<TreatmentPlanDto>.Failure("Erreur lors de la création du traitement.");
        }
    }
}
