using MediatR;
using Microsoft.Extensions.Logging;
using ClinicManagement.Application.Common;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.TreatmentPlans.Commands;

/// <summary>
/// « Passer la créance en perte » (S4) — the practice decides the balance will never be collected, and says so
/// once instead of leaving it in « Créances » for ever.
///
/// <para>
/// ⚠️ <b>The devis track had no instrument for this at all.</b> The invoice track has the avoir; a devis had
/// only <c>Cancel</c>, which is wrong twice over — it is refused outright once any money has been collected
/// (<c>EnsureNoLiveMoney</c>, C1) and it drops the whole document out of la caisse, rewriting days that are
/// already closed. So a patient who dies, emigrates or simply cannot pay left a live <c>Stopped</c> devis
/// whose balance <c>CarriesDebt(Stopped)</c> keeps claiming — correctly, since the work was delivered — with
/// no way to stop claiming it.
/// </para>
/// <para>
/// What it does: writes the status and records what was abandoned. What it deliberately does <b>not</b> do:
/// touch a single payment, échéance row or receipt. The cash was received and la caisse's past days stay
/// exactly as they are — that is the whole difference from a cancellation. See <c>TreatmentPlan.WriteOff</c>.
/// </para>
/// <para>
/// Reversible through « Reprendre le traitement » (<c>ReopenTreatmentPlanCommand</c>), which clears the
/// write-off record and brings the créance back. A status nothing can leave is the defect
/// <c>UncancelTreatmentPlanCommand</c> had to be written for.
/// </para>
/// </summary>
public class WriteOffTreatmentPlanCommand : IRequest<Result<TreatmentPlanDto>>
{
    public Guid Id { get; set; }

    /// <summary>Why the balance is being abandoned. Required — this is the evidence for a loss.</summary>
    public string Reason { get; set; } = string.Empty;

    /// <inheritdoc cref="CancelTreatmentPlanCommand.Version"/>
    public uint Version { get; set; }
}

public class WriteOffTreatmentPlanCommandHandler
    : IRequestHandler<WriteOffTreatmentPlanCommand, Result<TreatmentPlanDto>>
{
    private readonly ITreatmentPlanRepository _planRepository;
    private readonly IPatientRepository _patientRepository;
    private readonly IInvoiceRepository _invoiceRepository;
    private readonly ICurrentClinicResolver _clinicResolver;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<WriteOffTreatmentPlanCommandHandler> _logger;

    public WriteOffTreatmentPlanCommandHandler(
        ITreatmentPlanRepository planRepository,
        IPatientRepository patientRepository,
        IInvoiceRepository invoiceRepository,
        ICurrentClinicResolver clinicResolver,
        IUnitOfWork unitOfWork,
        ILogger<WriteOffTreatmentPlanCommandHandler> logger)
    {
        _planRepository = planRepository;
        _patientRepository = patientRepository;
        _invoiceRepository = invoiceRepository;
        _clinicResolver = clinicResolver;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<Result<TreatmentPlanDto>> Handle(
        WriteOffTreatmentPlanCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var clinicResult = await _clinicResolver.GetClinicIdAsync(cancellationToken);
            if (clinicResult.IsFailure)
            {
                return Result<TreatmentPlanDto>.Failure(clinicResult.Error ?? "Cabinet introuvable.");
            }

            var plan = await _planRepository.GetByIdAsync(request.Id, cancellationToken);
            if (plan == null || plan.ClinicId != clinicResult.Value)
            {
                return Result<TreatmentPlanDto>.Failure("Plan de traitement introuvable.");
            }

            /*
             * ⚠️ Refused while a note d'honoraires represents this devis. The bridge is all-or-nothing:
             * `PlanBillingRules.BilledPlanIds` drops the plan from every balance the moment a live note names
             * it, so the debt the dentist is looking at is the NOTE's and writing off the plan would change
             * nothing at all — a green toast over an unmoved figure, which is this repository's worst defect
             * shape. The note's own avoir is the instrument there, and the sentence says so.
             */
            var representingNote = await PlanBridgeLookup.RepresentingNoteAsync(
                _invoiceRepository, clinicResult.Value, plan.Id, cancellationToken);
            if (representingNote != null)
            {
                return Result<TreatmentPlanDto>.Failure(
                    $"Ce devis est facturé sur la note {representingNote} : c'est elle qui porte la créance. "
                    + "Créditez-la par un avoir, ou détachez-la de ce traitement d'abord.");
            }

            plan.WriteOff(request.Reason);

            _unitOfWork.SetExpectedVersion(plan, request.Version);
            await _planRepository.UpdateAsync(plan, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Wrote off {Amount} on treatment plan {PlanId}", plan.WriteOffAmount, plan.Id);

            var patient = await _patientRepository.GetByIdAsync(plan.PatientId, cancellationToken);
            return Result<TreatmentPlanDto>.Success(plan.ToDto(patient?.GetFullName()));
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
            _logger.LogError(ex, "Error writing off treatment plan {PlanId}", request.Id);
            return Result<TreatmentPlanDto>.Failure("Erreur lors de la mise en perte de la créance.");
        }
    }
}
