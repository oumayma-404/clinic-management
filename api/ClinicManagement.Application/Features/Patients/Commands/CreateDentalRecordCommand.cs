using MediatR;
using Microsoft.Extensions.Logging;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.Common;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Application.Features.Documents;
using ClinicManagement.Application.Features.Invoices;
using ClinicManagement.Application.Features.Patients;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.Patients.Commands;

public class CreateDentalRecordCommand : IRequest<Result<DentalRecordDto>>
{
    public Guid PatientId { get; set; }
    public DateTime InterventionDate { get; set; }
    public decimal AmountPaid { get; set; }

    /// <summary>
    /// How <see cref="AmountPaid"/> was settled — <c>Cash</c>/<c>Cheque</c>/<c>Card</c>/<c>Transfer</c>. Omit for
    /// cash. It reaches the note d'honoraires this save raises, which is the point: the payment used to be booked
    /// as cash unconditionally, so a séance settled by cheque never reached « Chèques à encaisser ».
    /// <para>
    /// A new fiche needs no billing guard — it has no note d'honoraires to contradict. The AC-2 / AC-3b refusals
    /// live on the <b>update</b> path only.
    /// </para>
    /// </summary>
    public string? PaymentMethod { get; set; }

    /// <inheritdoc cref="PaymentMethod"/>
    public string? ChequeNumber { get; set; }

    /// <inheritdoc cref="PaymentMethod"/>
    public string? ChequeBankName { get; set; }

    /// <inheritdoc cref="PaymentMethod"/>
    public DateTime? ChequeDueDate { get; set; }

    public bool IsAdultTeeth { get; set; }
    /// <summary>The acts performed this session — the record's procedure summary + cost are derived from these.</summary>
    public List<DentalActInput> Acts { get; set; } = new();
    public List<string> Notes { get; set; } = new();
    public List<string> ImportantNotes { get; set; } = new();
    /// <summary>
    /// What the patient handed over towards the <b>treatment</b> this séance carries out — the cumulative figure
    /// for this séance, not an increment. Requires <see cref="TreatmentPlanId"/>.
    ///
    /// <para>
    /// ⚠️ <b>A second money field, and it must never be folded into <see cref="AmountPaid"/>.</b> They settle two
    /// different things: « Payé » covers this séance's own acts and produces a note d'honoraires, while this draws
    /// down a multi-séance act priced <i>once</i> on its devis and produces an échéance payment. A devis act sits
    /// on the fiche at 0 by rule, so putting its money through « Payé » would either be refused (the payment may
    /// not exceed the séance's total) or, if the 0 were overtyped, raise a second claim for work the treatment
    /// already prices. See <c>DentalRecordTreatmentCollection</c>.
    /// </para>
    /// <para>
    /// ⚠️ Collecting on a treatment that has no devis number <b>issues one</b> — see
    /// <c>CollectOnTreatmentCommand</c>. That spends a gapless number, so the UI must say so before saving.
    /// </para>
    /// </summary>
    public decimal AmountCollectedOnPlan { get; set; }

    /// <summary>Optional treatment plan whose step this record completes (required when <see cref="TreatmentPlanItemId"/> is set).</summary>
    public Guid? TreatmentPlanId { get; set; }
    /// <summary>Optional plan step this record carries out — marked "réalisé" and linked to this record on save.</summary>
    public Guid? TreatmentPlanItemId { get; set; }

    /// <summary>
    /// Which <b>step</b> of that devis act this fiche carries out — « le scellement ». Null when the act is
    /// done in one séance, which is the ordinary case and every fiche written before steps existed.
    /// <para>
    /// ⚠️ Supplying it is what lets one devis line be evidenced by several fiches:
    /// <c>TreatmentPlanItem.MarkDone</c> refuses a second, different record, so without a step the second
    /// séance of a bridge is refused outright. Omitting it on a stepped act is still safe — the act-level
    /// path advances that act's next pending step.
    /// </para>
    /// </summary>
    public Guid? TreatmentPlanItemStepId { get; set; }
    /// <summary>Optional appointment this record documents — completing it and dismissing its post-visit review
    /// prompt (finding #10), so recording the dental work (not only a medical document) closes the loop.</summary>
    public Guid? AppointmentId { get; set; }

    /// <summary>
    /// Which practitioner performed the séance (L9). Optional — omit it and the documented appointment's
    /// practitioner is used, else the caller's own <c>Doctor</c> record.
    /// </summary>
    public Guid? DoctorId { get; set; }

    /// <summary>
    /// What was prescribed at this séance — médicaments and/or examens. Becomes the séance's own ordonnance
    /// (a <c>MedicalDocument</c>), emitted <b>inside this save's transaction</b>: see
    /// <see cref="Documents.FicheOrdonnanceEmitter"/> for why it is not one of the post-commit side effects.
    /// <para>
    /// Tri-state, like every other update payload here: <b>absent</b> means « unchanged » and is not even read;
    /// <b>present and empty</b> means « nothing prescribed »; a populated list is the prescription. The fiche
    /// modal always sends it — a field a routine re-save forgets is how this product has lost data before.
    /// </para>
    /// </summary>
    public PrescriptionInput? Prescription { get; set; }
}

public class CreateDentalRecordCommandHandler : IRequestHandler<CreateDentalRecordCommand, Result<DentalRecordDto>>
{
    private readonly IPatientRepository _patientRepository;
    private readonly IDentalRecordRepository _dentalRecordRepository;
    private readonly IToothStateRepository _toothStateRepository;
    private readonly ITreatmentPlanRepository _treatmentPlanRepository;
    private readonly IDoctorRepository _doctorRepository;
    private readonly IClinicRepository _clinicRepository;
    private readonly IMedicalDocumentRepository _documentRepository;
    private readonly IClinicContext _clinicContext;
    private readonly IAppointmentRepository _appointmentRepository;
    private readonly ICurrentClinicResolver _clinicResolver;
    private readonly IUnitOfWork _unitOfWork;
    private readonly INotificationGenerator _notificationGenerator;
    private readonly IStockConsumptionService _stockConsumption;
    private readonly IRealtimeNotifier _realtimeNotifier;
    private readonly ISender _sender;
    private readonly ILogger<CreateDentalRecordCommandHandler> _logger;

    public CreateDentalRecordCommandHandler(
        IPatientRepository patientRepository,
        IDentalRecordRepository dentalRecordRepository,
        IToothStateRepository toothStateRepository,
        ITreatmentPlanRepository treatmentPlanRepository,
        IDoctorRepository doctorRepository,
        IClinicRepository clinicRepository,
        IMedicalDocumentRepository documentRepository,
        IClinicContext clinicContext,
        IAppointmentRepository appointmentRepository,
        ICurrentClinicResolver clinicResolver,
        IUnitOfWork unitOfWork,
        INotificationGenerator notificationGenerator,
        IStockConsumptionService stockConsumption,
        IRealtimeNotifier realtimeNotifier,
        ISender sender,
        ILogger<CreateDentalRecordCommandHandler> logger)
    {
        _patientRepository = patientRepository;
        _dentalRecordRepository = dentalRecordRepository;
        _toothStateRepository = toothStateRepository;
        _treatmentPlanRepository = treatmentPlanRepository;
        _doctorRepository = doctorRepository;
        _clinicRepository = clinicRepository;
        _documentRepository = documentRepository;
        _clinicContext = clinicContext;
        _appointmentRepository = appointmentRepository;
        _clinicResolver = clinicResolver;
        _unitOfWork = unitOfWork;
        _notificationGenerator = notificationGenerator;
        _stockConsumption = stockConsumption;
        _realtimeNotifier = realtimeNotifier;
        _sender = sender;
        _logger = logger;
    }

    public async Task<Result<DentalRecordDto>> Handle(CreateDentalRecordCommand request, CancellationToken cancellationToken)
    {
        try
        {
            if (request.Acts.Count == 0)
            {
                return Result<DentalRecordDto>.Failure("Au moins un acte est requis.");
            }

            var clinicResult = await _clinicResolver.GetClinicIdAsync(cancellationToken);
            if (clinicResult.IsFailure)
            {
                return Result<DentalRecordDto>.Failure(clinicResult.Error ?? "Unable to resolve current clinic");
            }

            var patient = await _patientRepository.GetByIdAsync(request.PatientId, cancellationToken);
            if (patient == null || patient.ClinicId != clinicResult.Value)
            {
                return Result<DentalRecordDto>.Failure("Patient introuvable.");
            }

            var parsed = DentalRecordActParser.Parse(request.Acts);
            if (parsed.IsFailure)
            {
                return Result<DentalRecordDto>.Failure(parsed.Error!);
            }

            // « Un acte porté par un devis est à 0 », imposed here rather than trusted from the client — the same
            // rule `PriceForPlanLinkedAct` already imposes when the séance is booked. Overtyping that 0 on the
            // fiche is what raised a note d'honoraires for work the treatment already prices.
            var acts = await PlanCarriedActPricing.ImposeAsync(
                _treatmentPlanRepository, parsed.Value!, request.TreatmentPlanId, request.TreatmentPlanItemId,
                clinicResult.Value, _logger, cancellationToken);

            // Which visit does this fiche document? The client's id when it sent one — the post-visit deep link
            // knows more than we can infer — otherwise the patient's single visit that day, and nothing when
            // there are none or several.
            //
            // ⚠️ Until this call, that deep link was the ONLY door that ever populated the column: a fiche
            // charted the ordinary way from the patient's page stored null. So « quelles séances n'ont pas encore
            // de fiche ? » — the exact question DentalRecord.AppointmentId's own docstring says it exists to
            // answer — reported « pas de fiche » for the majority of visits that have one. See
            // DentalRecordVisitLink for why ambiguity is left unresolved rather than guessed.
            var appointmentId = await DentalRecordVisitLink.ResolveAsync(
                request.AppointmentId,
                request.PatientId,
                patient.ClinicId,
                request.InterventionDate,
                _appointmentRepository,
                cancellationToken);

            // The appointment id is now STORED on the record, not only used for the post-commit side effect below.
            // It reached this handler from the first version and was thrown away, which is why no screen could tell
            // which past visits still have no fiche — the question this link exists to answer.
            var record = new DentalRecord(
                Guid.NewGuid(),
                request.PatientId,
                patient.ClinicId,
                request.InterventionDate,
                request.AmountPaid,
                request.IsAdultTeeth,
                request.Notes,
                request.ImportantNotes,
                appointmentId);

            if (!Enum.TryParse<PaymentMethod>(request.PaymentMethod ?? nameof(PaymentMethod.Cash), ignoreCase: true, out var method))
            {
                return Result<DentalRecordDto>.Failure("Mode de paiement invalide.");
            }

            record.SetActs(acts);

            // Only now is `Cost` known — it is derived in `SetActs`, not passed to the ctor.
            var withinCost = DentalRecordBillingGuard.CheckPaymentWithinCost(record.Cost, record.AmountPaid);
            if (withinCost.IsFailure)
            {
                return Result<DentalRecordDto>.FailureFrom(withinCost);
            }

            // Null stays null — « non renseigné », read as cash everywhere. `SetPayment` runs the cheque details
            // through the existing `ChequeDetails.For`, so details on a non-cheque method are refused there rather
            // than by a second copy of the rule here.
            record.SetPayment(
                request.PaymentMethod is null ? null : method,
                request.ChequeNumber, request.ChequeBankName, request.ChequeDueDate);

            // L9 — who performed the séance. Derived from the documented appointment when there is one (that is the
            // most reliable source: the visit was booked with a practitioner), else the caller's own Doctor record.
            // A fiche with no attribution is a real outcome and is left null rather than guessed at.
            var recordAppointmentDoctorId = appointmentId.HasValue
                ? (await _appointmentRepository.GetByIdAsync(appointmentId.Value, cancellationToken))?.DoctorId
                : null;
            record.SetDoctor(await ResolveAttributedDoctorAsync(
                request.DoctorId, recordAppointmentDoctorId, clinicResult.Value, cancellationToken));

            await _dentalRecordRepository.AddAsync(record, cancellationToken);

            // Completing a scheduled plan step: mark it "réalisé" and link it to this record (AC-4).
            //
            // ⚠️ **This now runs BEFORE the odontogram is written, and the order is the fix.** Charting an act's
            // end state is only legitimate once the act is finished, and only the aggregate — after the step has
            // been marked — can say whether it is. See `ToothChartingRules`.
            DentalRecordLinker.PlanActLink? planLink = null;
            if (request.TreatmentPlanItemId.HasValue)
            {
                var link = await DentalRecordLinker.LinkPlanItemAsync(
                    _treatmentPlanRepository, _appointmentRepository,
                    request.TreatmentPlanId, request.TreatmentPlanItemId.Value,
                    request.PatientId, clinicResult.Value, record.Id, request.InterventionDate, cancellationToken,
                    request.TreatmentPlanItemStepId, request.AppointmentId);
                if (link.IsFailure)
                {
                    return Result<DentalRecordDto>.Failure(link.Error!);
                }
                planLink = link.Value;
            }

            /*
             * ⚠️ `ChartableActs`, never `acts` — an implant's step-1 fiche used to chart « Implant » on the tooth
             * weeks before the implant existed, and `ClearDiagnosesForTreatedTeethAsync` below deleted the
             * « à traiter » that said the work was still needed. Measured: 7 rows on the live database, every one
             * written from a step 1 of 2.
             *
             * The list is for the odontogram ONLY. `record.SetActs` above keeps the condition the dentist chose —
             * it is what the act will chart when the treatment ends, and what re-opening the fiche must show.
             */
            var chartable = ToothChartingRules.ChartableActs(
                acts, planLink?.Item, planLink?.ItemIsComplete ?? true);

            var toothStates = DentalRecordActParser
                .BuildToothStates(chartable, request.PatientId, patient.ClinicId, request.InterventionDate, record.Id)
                .ToList();

            // Treating a tooth closes any open diagnosis charted on it (AC-5). Driven off the states above, so a
            // withheld one withholds this in the same breath — which is what keeps the tooth « à traiter » for
            // the length of the treatment instead of clearing it on the first séance.
            await DentalRecordLinker.ClearDiagnosesForTreatedTeethAsync(
                _toothStateRepository, request.PatientId, toothStates, cancellationToken);

            foreach (var toothState in toothStates)
            {
                await _toothStateRepository.AddAsync(toothState, cancellationToken);
            }

            /*
             * The séance's ordonnance — médicaments and examens — emitted as a real MedicalDocument.
             *
             * ⚠️ HERE, before the commit, and deliberately NOT with the post-commit side effects below. Those
             * are derived (stock, the note d'honoraires, marking the visit complete) and may fail without the
             * clinical record being wrong. A prescription is entered clinical data: a dentist who types an
             * antibiotic, reads « fiche enregistrée » and has no ordonnance has lost work silently. Either
             * both land or neither does. See FicheOrdonnanceEmitter.
             */
            // ⚠️ An ABSENT payload is skipped outright — no read, no write. Omitting a key means « unchanged »
            // here as everywhere else, and it is what keeps every caller that predates prescriptions (and every
            // server-internal writer) from touching a document. An empty-but-present payload is a statement and
            // does go through. See FicheOrdonnanceResult.None.
            var ordonnance = request.Prescription is null
                ? FicheOrdonnanceResult.None
                : await FicheOrdonnanceEmitter.EmitAsync(
                    request.Prescription, record, patient, clinicResult.Value, _clinicContext.GetUserId(),
                    _documentRepository, _clinicRepository, _doctorRepository, _unitOfWork, _logger, cancellationToken);

            await _unitOfWork.SaveChangesAsync(cancellationToken);

            // If this record documents a scheduled appointment, close its post-visit-review loop
            // (finding #10) — best-effort, never rolls back the committed record.
            //
            // ⚠️ Since the link is now also INFERRED, this fires for a fiche charted from the patient's page —
            // charting the séance marks that day's visit « Terminé » and withdraws its post-visit prompt. That is
            // the loop, not a side effect of it: filling the record *is* the evidence the patient came. It stays
            // safe because MarkVisitCompleted returns Contradicted rather than throwing for a visit the schedule
            // says was cancelled or missed, and because the inference refuses every ambiguous day.
            if (appointmentId.HasValue)
            {
                await CompleteReviewedAppointmentAsync(appointmentId.Value, clinicResult.Value, cancellationToken);
            }

            // AC-P4.10 — draw each recorded act's material list out of stock. Post-commit and best-effort, so a
            // stock failure can never lose the clinical record (AC-P4.13). One entry per act performance, not
            // per distinct procedure: two composites really do use two capsules.
            await _stockConsumption.ConsumeForDentalRecordAsync(
                clinicResult.Value,
                record.Id,
                record.Acts.Where(a => a.ProcedureTypeId.HasValue).Select(a => a.ProcedureTypeId!.Value).ToList(),
                cancellationToken);

            // « Montant payé » becomes real money: raise the note d'honoraires and record the payment. Post-commit
            // and last, after the two side effects above have finished with the DbContext — the billing opens its
            // own transaction. Best-effort for the record, never silent about the cash (see DentalRecordAutoBilling).
            var dto = record.ToDto();
            // Read back so the modal can round-trip the document (and its own version) on the next save, and
            // so the séance row can name what was prescribed without a second request.
            dto.PrescriptionDocumentId = ordonnance.DocumentId;
            dto.PrescriptionSummary = ordonnance.Summary.ToList();
            // The second sheet. Independent of the first: a seance may prescribe medicaments, examens,
            // both or neither, and the two are separate papers with separate ids.
            dto.ExamensDocumentId = ordonnance.ExamensDocumentId;
            dto.ExamensSummary = ordonnance.ExamensSummary.ToList();
            dto.Billing = await DentalRecordAutoBilling.BillIfPaidAsync(
                _sender, record, request.AmountPaid, _logger, cancellationToken);

            // The séance's OTHER money: what the patient paid towards a multi-séance act, which is priced once on
            // its treatment and collected a visit at a time. Post-commit and best-effort on the same contract, and
            // deliberately a second call rather than a branch inside the first — the two produce different
            // documents (a note d'honoraires, an échéance receipt) and a séance can legitimately do both.
            dto.TreatmentCollection = await DentalRecordTreatmentCollection.CollectIfPaidAsync(
                _sender, record, request.TreatmentPlanId, request.AmountCollectedOnPlan, _logger, cancellationToken);

            return Result<DentalRecordDto>.Success(dto);
        }
        catch (ArgumentException ex)
        {
            return Result<DentalRecordDto>.Failure(ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            return Result<DentalRecordDto>.Failure(ex.Message);
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            _logger.LogError(ex, "Unhandled failure creating a dental record");
            return Result<DentalRecordDto>.Failure("Erreur lors de l'enregistrement de la fiche de soins. Veuillez réessayer.");
        }
    }

    // Marks the documented appointment Completed (if it resolves in the caller's clinic) and removes its
    // pending post-visit review — mirrors CreateMedicalDocumentCommand.CompleteReviewedAppointmentAsync.
    // A cross-clinic/missing id is a silent no-op. Wrapped so any failure only logs — never rolls back the
    // already-committed dental record.
    private async Task CompleteReviewedAppointmentAsync(Guid appointmentId, Guid clinicId, CancellationToken cancellationToken)
    {
        try
        {
            var appointment = await _appointmentRepository.GetByIdAsync(appointmentId, cancellationToken);
            if (appointment == null || appointment.ClinicId != clinicId)
            {
                return; // cross-clinic or unknown id → leave everything unchanged
            }

            // AC-P1.12: three outcomes, not two. `Contradicted` means the fiche documents a visit the schedule
            // says was cancelled or missed — logged as a Warning so it is discoverable, and the appointment is
            // deliberately NOT reopened (silently un-cancelling a visit is the invisible state change this part
            // exists to remove).
            var outcome = appointment.MarkVisitCompleted();
            if (outcome == VisitCompletionOutcome.Contradicted)
            {
                _logger.LogWarning(
                    "Fiche de soins recorded against appointment {AppointmentId}, which is {Status}. The "
                    + "appointment was left unchanged; its post-visit review is cleared regardless.",
                    appointmentId, appointment.Status);
            }

            // Only Completed actually changed the row; the other two outcomes have nothing to persist, so the
            // save is skipped rather than issuing an UPDATE that sets nothing.
            if (outcome == VisitCompletionOutcome.Completed)
            {
                // Change-tracked from GetByIdAsync, so SaveChanges persists MarkVisitCompleted().
                await _unitOfWork.SaveChangesAsync(cancellationToken);
            }

            // The review is fulfilled either way — including on AlreadyCompleted (idempotent) and on
            // Contradicted, where leaving the prompt up would nag about a visit that is not going to happen.
            await _notificationGenerator.CancelPostVisitReviewAsync(appointment.ClinicId, appointmentId, cancellationToken);

            // This command broadcasts "patients"; also tell "appointments" consumers so the calendar reflects
            // the now-Completed status.
            await _realtimeNotifier.NotifyEntityChangedAsync(appointment.ClinicId, "appointments", cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Post-visit completion side-effect failed for appointment {AppointmentId}", appointmentId);
        }
    }

    /// <summary>
    /// The practitioner to attribute this to, through the one shared precedence rule
    /// (<see cref="PractitionerAttribution"/>): an explicitly named one, else the visit's, else the caller's own
    /// <c>Doctor</c> record.
    /// <para>
    /// The caller is the <b>last</b> resort, not the first: a secretary recording a dentist's work must not credit
    /// themselves. In the common Tunisian single-dentist practice the owner *is* the caller, which is exactly where
    /// the fall-back is correct.
    /// </para>
    /// </summary>
    private async Task<Guid?> ResolveAttributedDoctorAsync(
        Guid? explicitDoctorId, Guid? appointmentDoctorId, Guid clinicId, CancellationToken cancellationToken)
    {
        var clinicDoctorIds = await PractitionerAttribution.LoadClinicDoctorIdsAsync(
            _doctorRepository, clinicId, cancellationToken);

        Guid? callerDoctorId = null;
        var userId = _clinicContext.GetUserId();
        if (!string.IsNullOrEmpty(userId))
        {
            callerDoctorId = (await _doctorRepository.GetByUserIdAsync(userId, cancellationToken))?.Id;
        }

        return PractitionerAttribution.Resolve(
            explicitDoctorId, appointmentDoctorId, callerDoctorId, clinicDoctorIds);
    }

}
