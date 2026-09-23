using MediatR;
using Microsoft.Extensions.Logging;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.Patients.Commands;

public class DeleteDentalRecordCommand : IRequest<Result<bool>>
{
    public Guid Id { get; set; }
    public Guid PatientId { get; set; }
}

public class DeleteDentalRecordCommandHandler : IRequestHandler<DeleteDentalRecordCommand, Result<bool>>
{
    private readonly IDentalRecordRepository _dentalRecordRepository;
    private readonly IPatientRepository _patientRepository;
    private readonly ITreatmentPlanRepository _planRepository;
    private readonly IInvoiceRepository _invoiceRepository;
    private readonly ICreditNoteRepository _creditNoteRepository;
    private readonly IMedicalDocumentRepository _medicalDocumentRepository;
    private readonly IUserRepository _userRepository;
    private readonly IClinicContext _clinicContext;
    private readonly ICurrentClinicResolver _clinicResolver;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<DeleteDentalRecordCommandHandler> _logger;

    public DeleteDentalRecordCommandHandler(
        IDentalRecordRepository dentalRecordRepository,
        IPatientRepository patientRepository,
        ITreatmentPlanRepository planRepository,
        IInvoiceRepository invoiceRepository,
        ICreditNoteRepository creditNoteRepository,
        IMedicalDocumentRepository medicalDocumentRepository,
        IUserRepository userRepository,
        IClinicContext clinicContext,
        ICurrentClinicResolver clinicResolver,
        IUnitOfWork unitOfWork,
        ILogger<DeleteDentalRecordCommandHandler> logger)
    {
        _dentalRecordRepository = dentalRecordRepository;
        _patientRepository = patientRepository;
        _planRepository = planRepository;
        _invoiceRepository = invoiceRepository;
        _creditNoteRepository = creditNoteRepository;
        _medicalDocumentRepository = medicalDocumentRepository;
        _userRepository = userRepository;
        _clinicContext = clinicContext;
        _clinicResolver = clinicResolver;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<Result<bool>> Handle(DeleteDentalRecordCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var clinicResult = await _clinicResolver.GetClinicIdAsync(cancellationToken);
            if (clinicResult.IsFailure)
            {
                // A-9: this returned the English "Unable to resolve current clinic" — the § 2 sweep missed it.
                return Result<bool>.Failure(clinicResult.Error ?? "Cabinet introuvable.");
            }

            var dentalRecord = await _dentalRecordRepository.GetByIdAsync(request.Id, cancellationToken);
            if (dentalRecord == null)
            {
                return Result<bool>.Failure("Acte dentaire introuvable.");
            }

            if (dentalRecord.PatientId != request.PatientId)
            {
                return Result<bool>.Failure("Cet acte dentaire n'appartient pas à ce patient.");
            }

            // Verify the owning patient belongs to the caller's clinic before deleting.
            var patient = await _patientRepository.GetByIdAsync(dentalRecord.PatientId, cancellationToken);
            if (patient == null || patient.ClinicId != clinicResult.Value)
            {
                return Result<bool>.Failure("Acte dentaire introuvable.");
            }

            /*
             * The fiche's FK-less links, all of them. `ToothState`, `DentalRecordTooth` and `DentalRecordAct`
             * have real cascading FKs and clean themselves; the rest are soft by design
             * (InvoiceLineConfiguration:36, TreatmentPlanItemConfiguration:55) and nothing at the database
             * level clears them.
             *
             * ⚠️ **Its own comment used to say « the two soft links to this fiche ». There are six**, and the
             * three it did not know about are where the money lives: `InstallmentPayment.DentalRecordId`,
             * `Invoice.DentalRecordId` and `MedicalDocument.DentalRecordId`. See
             * `DentalRecordDeletionReversal` for what that cost a patient.
             *
             * One transaction: a partial cleanup is the defect, not the fix — and now that money moves inside
             * it, that sentence is load-bearing rather than tidy.
             */
            var reversal = await DentalRecordDeletionReversal.InspectAsync(
                _planRepository, _invoiceRepository, _creditNoteRepository,
                clinicResult.Value, dentalRecord, cancellationToken);

            // Refused BEFORE the transaction opens, and before a single row is touched. A banked cheque, a
            // blocking avoir or a note billing another séance are all « fix that first » rather than
            // « we will do our best » — money half-undone is the one outcome nobody can read.
            if (reversal.IsRefused)
            {
                return Result<bool>.Failure(reversal.Refusal!);
            }

            await _unitOfWork.BeginTransactionAsync(cancellationToken);
            try
            {
                // Money first, while every link it is found by is still intact: `DetachInvoiceLinesAsync` below
                // clears the very line pointers a note is discovered through, and `DetachPlanActsAsync` clears
                // the act's own record link. Reversing after them would find nothing and report success.
                await DentalRecordDeletionReversal.ApplyAsync(
                    reversal, _planRepository, _invoiceRepository,
                    dentalRecord.InterventionDate,
                    _clinicContext.GetUserId(),
                    await ResolveActorNameAsync(cancellationToken),
                    cancellationToken);

                var releasedDocuments = await ReleaseMedicalDocumentsAsync(
                    clinicResult.Value, dentalRecord, cancellationToken);
                var detachedActs = await DetachPlanActsAsync(clinicResult.Value, request.Id, cancellationToken);
                var detachedLines = await DetachInvoiceLinesAsync(clinicResult.Value, request.Id, cancellationToken);

                await _dentalRecordRepository.DeleteAsync(request.Id, cancellationToken);
                await _unitOfWork.SaveChangesAsync(cancellationToken);
                await _unitOfWork.CommitTransactionAsync(cancellationToken);

                if (detachedActs > 0 || detachedLines > 0 || reversal.TouchesMoney || releasedDocuments > 0)
                {
                    _logger.LogInformation(
                        "Deleted dental record {RecordId}: detached {Acts} plan act(s), {Lines} invoice line(s), "
                        + "released {Documents} document(s), reversed {Amount} DT across {Plans} devis and {Notes} note(s)",
                        request.Id, detachedActs, detachedLines, releasedDocuments,
                        reversal.TotalReversed, reversal.PlanCollections.Count, reversal.Notes.Count);
                }
            }
            catch
            {
                await _unitOfWork.RollbackTransactionAsync(cancellationToken);
                throw;
            }

            return Result<bool>.Success(true);
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            // AC-13.2: the detail goes to the log; the caller only ever sees French guidance.
            _logger.LogError(ex, "Unhandled failure deleting dental record");
            return Result<bool>.Failure("Erreur lors de la suppression de l'acte dentaire. Veuillez réessayer.");
        }
    }

    /// <summary>
    /// The name written beside every annulment this deletion performs, so the journal says <b>who</b> and not
    /// only what — the same resolution <c>VoidInstallmentPaymentCommand</c> does, because a correction whose
    /// author is unknown is the one a practice cannot settle between two people.
    /// </summary>
    private async Task<string?> ResolveActorNameAsync(CancellationToken cancellationToken)
    {
        var actorUserId = _clinicContext.GetUserId();
        if (string.IsNullOrWhiteSpace(actorUserId))
        {
            return null;
        }

        var user = await _userRepository.GetByAuth0SubAsync(actorUserId, cancellationToken);
        return user?.FullName ?? user?.Email;
    }

    /// <summary>
    /// Cut the fiche's ordonnances loose, <b>keeping the documents</b>.
    ///
    /// <para>
    /// ⚠️ <b>Deliberately not a deletion, and the decision is older than this change.</b> « Elle n'efface
    /// jamais » — the fiche never destroys its own ordonnance, because the paper may already be in the
    /// patient's hand and <c>DeleteMedicalDocumentCommand</c> is its own <c>AdminOrDoctor</c> verb. So « as if
    /// the fiche had never existed » stops exactly here, and stops on purpose.
    /// </para>
    /// <para>
    /// What must go is the dangling pointer: « Modifier » routes on <c>MedicalDocumentDto.DentalRecordId</c>,
    /// on the ground that a document a fiche owns is recomposed from that fiche's next save. With the fiche
    /// gone there is no next save, so the document would route to an editor that can never reach it. Cleared,
    /// it is an ordinary document again and stays editable.
    /// </para>
    /// </summary>
    /// <param name="record">
    /// Its own id <b>and</b> its appointment: the read claims a legacy ordonnance — one written before
    /// <c>MedicalDocument.DentalRecordId</c> existed, so carrying only an <c>AppointmentId</c> — through the
    /// visit, and those are exactly the documents this fiche owns and nothing else will ever release.
    /// </param>
    private async Task<int> ReleaseMedicalDocumentsAsync(
        Guid clinicId, DentalRecord record, CancellationToken cancellationToken)
    {
        var documents = await _medicalDocumentRepository.GetFicheOrdonnancesForDentalRecordsAsync(
            clinicId,
            new[] { record.Id },
            record.AppointmentId is { } appointmentId ? new[] { appointmentId } : Array.Empty<Guid>(),
            cancellationToken);

        var released = 0;
        foreach (var document in documents)
        {
            // Only the ones this fiche actually owns. The read deliberately also returns legacy documents
            // claimed through the visit, and a document already carrying ANOTHER fiche's id is that fiche's.
            if (document.DentalRecordId is { } owner && owner != record.Id)
            {
                continue;
            }

            document.ReleaseFromDentalRecord();
            await _medicalDocumentRepository.UpdateAsync(document, cancellationToken);
            released++;
        }

        return released;
    }

    /// <summary>
    /// Return every plan act evidenced by this fiche to « prévu », reopening any devis that the act had closed.
    /// Uses the aggregate's own <c>UnmarkItemDone</c> so the status arithmetic is the single implementation — a
    /// second copy here would be free to disagree with it.
    /// </summary>
    private async Task<int> DetachPlanActsAsync(Guid clinicId, Guid recordId, CancellationToken cancellationToken)
    {
        var plans = await _planRepository.GetByLinkedDentalRecordAsync(clinicId, recordId, cancellationToken);
        var detached = 0;

        foreach (var plan in plans)
        {
            // The aggregate's own release: steps first (a stepped act carries the record link only through its
            // last step), then step-less acts — and on a cancelled or written-off devis the pointers alone, since
            // `UnmarkItem*` refuse a void plan and that refusal made deleting such a fiche fail outright.
            var touched = plan.ReleaseDentalRecord(recordId);

            if (touched > 0)
            {
                await _planRepository.UpdateAsync(plan, cancellationToken);
                detached += touched;
            }
        }

        return detached;
    }

    /// <summary>
    /// Drop the provenance pointer on every invoice line raised from this fiche. The invoice keeps its number,
    /// its lines and its amounts — deleting a clinical record must never alter a fiscal document.
    /// </summary>
    private async Task<int> DetachInvoiceLinesAsync(Guid clinicId, Guid recordId, CancellationToken cancellationToken)
    {
        var links = await _invoiceRepository.GetDentalRecordLinksAsync(clinicId, cancellationToken);
        var invoiceIds = links
            .Where(l => l.DentalRecordId == recordId)
            .Select(l => l.InvoiceId)
            .Distinct()
            .ToList();

        var detached = 0;
        foreach (var invoiceId in invoiceIds)
        {
            var invoice = await _invoiceRepository.GetByIdAsync(invoiceId, cancellationToken);
            if (invoice == null || invoice.ClinicId != clinicId)
            {
                continue;
            }

            var cleared = invoice.ClearDentalRecordLinks(recordId);
            if (cleared > 0)
            {
                await _invoiceRepository.UpdateAsync(invoice, cancellationToken);
                detached += cleared;
            }
        }

        return detached;
    }
}









