using MediatR;
using Microsoft.Extensions.Logging;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.TreatmentPlans.Commands;

/// <summary>
/// « Détacher la note d'honoraires » — one note stops speaking for this devis, and the devis stays exactly
/// where it is.
///
/// <para>
/// ⚠️ <b>Three refusals named this remedy and none of them had a route.</b>
/// <c>TreatmentPlanItem.Revise</c> refuses to re-price an act a note collects with « …ou détachez-la de ce
/// traitement »; <c>TreatmentPlanBridgeRelease.DetachAsync</c> — the only thing in the product that detaches
/// one — was reachable from <b>cancelling</b> and <b>stopping</b> the treatment alone. So the dentist who
/// attached the wrong note could only fix it by killing the devis, which is the literal shape of « once
/// something becomes a treatment plan, any modification becomes load-bearing ».
/// </para>
/// <para>
/// It detaches <b>both</b> links, because a note can speak for a plan two ways and releasing one leaves the
/// other still claiming it: the <i>bridge</i> (<c>Invoice.TreatmentPlanId</c>, which drops the whole plan out
/// of every money read) and the <i>carried</i> markers (<c>TreatmentPlanItem.BilledOnInvoiceId</c>, which hold
/// individual acts at 0).
/// </para>
/// <para>
/// ⚠️ <b>The note's own money is untouched</b> — its lines, its number and its payments all stand. And the
/// freed acts stay at 0: nothing here knows what they were worth before the marker imposed it, and inventing a
/// figure on a devis is worse than a 0 the dentist can see and type over. Pricing them is the amendment that
/// follows.
/// </para>
/// </summary>
public class DetachTreatmentPlanNoteCommand : IRequest<Result<TreatmentPlanDto>>
{
    public Guid PlanId { get; set; }

    /// <summary>The note to release. It must currently speak for this plan, one way or the other.</summary>
    public Guid InvoiceId { get; set; }

    /// <inheritdoc cref="CancelTreatmentPlanCommand.Version"/>
    public uint Version { get; set; }
}

public class DetachTreatmentPlanNoteCommandHandler
    : IRequestHandler<DetachTreatmentPlanNoteCommand, Result<TreatmentPlanDto>>
{
    private readonly ITreatmentPlanRepository _planRepository;
    private readonly IInvoiceRepository _invoiceRepository;
    private readonly IPatientRepository _patientRepository;
    private readonly ICurrentClinicResolver _clinicResolver;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<DetachTreatmentPlanNoteCommandHandler> _logger;

    public DetachTreatmentPlanNoteCommandHandler(
        ITreatmentPlanRepository planRepository,
        IInvoiceRepository invoiceRepository,
        IPatientRepository patientRepository,
        ICurrentClinicResolver clinicResolver,
        IUnitOfWork unitOfWork,
        ILogger<DetachTreatmentPlanNoteCommandHandler> logger)
    {
        _planRepository = planRepository;
        _invoiceRepository = invoiceRepository;
        _patientRepository = patientRepository;
        _clinicResolver = clinicResolver;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<Result<TreatmentPlanDto>> Handle(
        DetachTreatmentPlanNoteCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var clinicResult = await _clinicResolver.GetClinicIdAsync(cancellationToken);
            if (clinicResult.IsFailure)
            {
                return Result<TreatmentPlanDto>.Failure(clinicResult.Error ?? "Cabinet introuvable.");
            }
            var clinicId = clinicResult.Value;

            var plan = await _planRepository.GetByIdAsync(request.PlanId, cancellationToken);
            if (plan == null || plan.ClinicId != clinicId)
            {
                return Result<TreatmentPlanDto>.Failure("Plan de traitement introuvable.");
            }

            var invoice = await _invoiceRepository.GetByIdAsync(request.InvoiceId, cancellationToken);
            if (invoice == null || invoice.ClinicId != clinicId)
            {
                return Result<TreatmentPlanDto>.Failure("Note d'honoraires introuvable.");
            }

            // The carried markers first: `DetachNote` runs `EnsureAmendable`, so a cancelled devis is refused
            // before either half has moved.
            var releasedActs = plan.DetachNote(invoice.Id);

            var wasBridged = invoice.TreatmentPlanId == plan.Id;
            if (wasBridged)
            {
                invoice.DetachFromTreatmentPlan(plan.Id);
                await _invoiceRepository.UpdateAsync(invoice, cancellationToken);
            }

            // Say so rather than reporting a success that changed nothing — the class of lie this whole
            // remediation exists to remove.
            if (releasedActs == 0 && !wasBridged)
            {
                return Result<TreatmentPlanDto>.Failure(
                    "Cette note d'honoraires n'est pas rattachée à ce traitement.");
            }

            _unitOfWork.SetExpectedVersion(plan, request.Version);
            await _planRepository.UpdateAsync(plan, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Detached note {InvoiceId} from plan {PlanId}: bridge={Bridged}, {Released} act(s) released",
                invoice.Id, plan.Id, wasBridged, releasedActs);

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
            _logger.LogError(ex, "Error detaching note from treatment plan {PlanId}", request.PlanId);
            return Result<TreatmentPlanDto>.Failure("Erreur lors du détachement de la note d'honoraires.");
        }
    }
}
