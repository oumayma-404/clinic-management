using MediatR;
using Microsoft.Extensions.Logging;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.TreatmentPlans.Commands;

/// <summary>
/// Change which practitioner a devis is attributed to.
///
/// <para>
/// ⚠️ <b>The wrong dentist was invisible and permanent.</b> <c>TreatmentPlan.SetDoctor</c>'s only two call
/// sites were on a plan two lines old (<c>CreateTreatmentPlanCommand</c>, <c>ContinueRecordedActCommand</c>),
/// and <c>TreatmentPlanDto</c> carried no doctor field at all — so no screen showed the attribution and
/// nothing could correct it. It is not cosmetic:
/// <c>CreateInvoiceFromTreatmentPlanCommand</c> snapshots it onto the note d'honoraires, so in a two-dentist
/// cabinet every dinar of that treatment is credited to the wrong person for ever.
/// </para>
/// <para>
/// ⚠️ <b>It does not re-attribute anything already issued.</b> A note raised from this devis kept its own
/// snapshot deliberately — a numbered fiscal document is not rewritten to tidy a plan — so this changes what
/// the <i>next</i> one will carry. The recorded work's own attribution is the fiche's
/// (<c>PractitionerAttribution</c>) and is untouched.
/// </para>
/// <para>
/// <c>null</c> clears the attribution, which is a real state: many treatments in a single-practitioner cabinet
/// genuinely name nobody, and « inconnu » would assert one exists.
/// </para>
/// </summary>
public class SetTreatmentPlanDoctorCommand : IRequest<Result<TreatmentPlanDto>>
{
    public Guid Id { get; set; }

    /// <summary>The practitioner, or <c>null</c> to clear the attribution.</summary>
    public Guid? DoctorId { get; set; }

    /// <inheritdoc cref="CancelTreatmentPlanCommand.Version"/>
    public uint Version { get; set; }
}

public class SetTreatmentPlanDoctorCommandHandler
    : IRequestHandler<SetTreatmentPlanDoctorCommand, Result<TreatmentPlanDto>>
{
    private readonly ITreatmentPlanRepository _planRepository;
    private readonly IDoctorRepository _doctorRepository;
    private readonly IPatientRepository _patientRepository;
    private readonly ICurrentClinicResolver _clinicResolver;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<SetTreatmentPlanDoctorCommandHandler> _logger;

    public SetTreatmentPlanDoctorCommandHandler(
        ITreatmentPlanRepository planRepository,
        IDoctorRepository doctorRepository,
        IPatientRepository patientRepository,
        ICurrentClinicResolver clinicResolver,
        IUnitOfWork unitOfWork,
        ILogger<SetTreatmentPlanDoctorCommandHandler> logger)
    {
        _planRepository = planRepository;
        _doctorRepository = doctorRepository;
        _patientRepository = patientRepository;
        _clinicResolver = clinicResolver;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<Result<TreatmentPlanDto>> Handle(
        SetTreatmentPlanDoctorCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var clinicResult = await _clinicResolver.GetClinicIdAsync(cancellationToken);
            if (clinicResult.IsFailure)
            {
                return Result<TreatmentPlanDto>.Failure(clinicResult.Error ?? "Cabinet introuvable.");
            }
            var clinicId = clinicResult.Value;

            var plan = await _planRepository.GetByIdAsync(request.Id, cancellationToken);
            if (plan == null || plan.ClinicId != clinicId)
            {
                return Result<TreatmentPlanDto>.Failure("Plan de traitement introuvable.");
            }

            string? doctorName = null;
            if (request.DoctorId.HasValue && request.DoctorId.Value != Guid.Empty)
            {
                // Tenant-checked before it is believed — a foreign id must read as « not found », never be
                // stored and then rendered as another practice's practitioner.
                var doctor = await _doctorRepository.GetByIdAsync(request.DoctorId.Value, cancellationToken);
                if (doctor == null || doctor.ClinicId != clinicId)
                {
                    return Result<TreatmentPlanDto>.Failure("Praticien introuvable.");
                }
                doctorName = doctor.FullName;
            }

            plan.SetDoctor(
                request.DoctorId.HasValue && request.DoctorId.Value != Guid.Empty ? request.DoctorId : null);

            _unitOfWork.SetExpectedVersion(plan, request.Version);
            await _planRepository.UpdateAsync(plan, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Treatment plan {PlanId} attributed to doctor {DoctorId} ({DoctorName})",
                plan.Id, plan.DoctorId, doctorName);

            var patient = await _patientRepository.GetByIdAsync(plan.PatientId, cancellationToken);
            var dto = plan.ToDto(patient?.GetFullName());
            // The command path maps without the derived read-back, so the name is filled here from the record
            // this handler already loaded rather than by a second lookup in the mapper.
            dto.DoctorName = doctorName;
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
            _logger.LogError(ex, "Error setting the doctor of treatment plan {PlanId}", request.Id);
            return Result<TreatmentPlanDto>.Failure("Erreur lors du changement de praticien.");
        }
    }
}
