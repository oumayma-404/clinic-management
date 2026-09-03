using MediatR;
using Microsoft.Extensions.Logging;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
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
    private readonly ICurrentClinicResolver _clinicResolver;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<DeleteDentalRecordCommandHandler> _logger;

    public DeleteDentalRecordCommandHandler(
        IDentalRecordRepository dentalRecordRepository,
        IPatientRepository patientRepository,
        ITreatmentPlanRepository planRepository,
        IInvoiceRepository invoiceRepository,
        ICurrentClinicResolver clinicResolver,
        IUnitOfWork unitOfWork,
        ILogger<DeleteDentalRecordCommandHandler> logger)
    {
        _dentalRecordRepository = dentalRecordRepository;
        _patientRepository = patientRepository;
        _planRepository = planRepository;
        _invoiceRepository = invoiceRepository;
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

            // The two soft links to this fiche are FK-less by design (InvoiceLineConfiguration:36,
            // TreatmentPlanItemConfiguration:55), so nothing at the database level clears them. Deleting the
            // fiche without this leaves a plan act « réalisé » pointing at a row that no longer exists — and
            // because marking an act done can auto-complete a plan, a deleted fiche could leave a devis closed
            // against evidence that is gone. One transaction: a partial cleanup is the defect, not the fix.
            await _unitOfWork.BeginTransactionAsync(cancellationToken);
            try
            {
                var detachedActs = await DetachPlanActsAsync(clinicResult.Value, request.Id, cancellationToken);
                var detachedLines = await DetachInvoiceLinesAsync(clinicResult.Value, request.Id, cancellationToken);

                await _dentalRecordRepository.DeleteAsync(request.Id, cancellationToken);
                await _unitOfWork.SaveChangesAsync(cancellationToken);
                await _unitOfWork.CommitTransactionAsync(cancellationToken);

                if (detachedActs > 0 || detachedLines > 0)
                {
                    _logger.LogInformation(
                        "Deleted dental record {RecordId}: detached {Acts} plan act(s) and {Lines} invoice line(s)",
                        request.Id, detachedActs, detachedLines);
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
            var touched = 0;

            // Steps first, and separately: a stepped act only takes its own LinkedDentalRecordId once its LAST
            // step lands, so a fiche that carried out one step of three is recorded on the step alone and the
            // act-level loop below would not see it. Left behind, the step would keep claiming « réalisée »
            // against a fiche that no longer exists.
            var linkedSteps = plan.Items
                .SelectMany(i => i.Steps.Select(s => new { ItemId = i.Id, StepId = s.Id, s.LinkedDentalRecordId }))
                .Where(s => s.LinkedDentalRecordId == recordId)
                .ToList();

            foreach (var step in linkedSteps)
            {
                if (plan.UnmarkItemStep(step.ItemId, step.StepId))
                {
                    touched++;
                }
            }

            // An act whose steps were just detached no longer carries a record-level link (the recompute cleared
            // it), so this reads what is genuinely left: step-less acts evidenced by this fiche.
            var linkedItemIds = plan.Items
                .Where(i => i.LinkedDentalRecordId == recordId)
                .Select(i => i.Id)
                .ToList();

            foreach (var itemId in linkedItemIds)
            {
                plan.UnmarkItemDone(itemId);
                touched++;
            }

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









