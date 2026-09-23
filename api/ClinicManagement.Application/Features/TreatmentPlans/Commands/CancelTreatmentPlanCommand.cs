using MediatR;
using Microsoft.Extensions.Logging;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.TreatmentPlans.Commands;

/// <summary>Cancel an accepted/in-progress plan (motif required). AdminOrDoctor (controller-enforced).</summary>
public class CancelTreatmentPlanCommand : IRequest<Result<TreatmentPlanDto>>
{
    public Guid Id { get; set; }
    public string Reason { get; set; } = string.Empty;

    /// <summary>
    /// The plan's <c>xmin</c> as the caller read it. <b>0 means « not supplied »</b> and skips the check — the
    /// solution-wide convention that keeps jobs and older callers unaffected.
    /// <para>
    /// ⚠️ This was the one <b>irreversible</b> write on the aggregate with no version check at all, so a cancel
    /// landed over a colleague's concurrent amendment with no 409 and no trace, on a plan that — before
    /// <see cref="UncancelTreatmentPlanCommand"/> — could never be recovered.
    /// </para>
    /// </summary>
    public uint Version { get; set; }
}

public class CancelTreatmentPlanCommandHandler : IRequestHandler<CancelTreatmentPlanCommand, Result<TreatmentPlanDto>>
{
    private readonly ITreatmentPlanRepository _planRepository;
    private readonly IInvoiceRepository _invoiceRepository;
    private readonly IPatientRepository _patientRepository;
    private readonly ICurrentClinicResolver _clinicResolver;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<CancelTreatmentPlanCommandHandler> _logger;
    // Both optional so the older construction sites keep compiling; the DI container always supplies them.
    private readonly IAppointmentRepository? _appointmentRepository;
    private readonly ISender? _sender;

    public CancelTreatmentPlanCommandHandler(
        ITreatmentPlanRepository planRepository,
        IInvoiceRepository invoiceRepository,
        IPatientRepository patientRepository,
        ICurrentClinicResolver clinicResolver,
        IUnitOfWork unitOfWork,
        ILogger<CancelTreatmentPlanCommandHandler> logger,
        IAppointmentRepository? appointmentRepository = null,
        ISender? sender = null)
    {
        _planRepository = planRepository;
        _invoiceRepository = invoiceRepository;
        _patientRepository = patientRepository;
        _clinicResolver = clinicResolver;
        _unitOfWork = unitOfWork;
        _logger = logger;
        _appointmentRepository = appointmentRepository;
        _sender = sender;
    }

    public async Task<Result<TreatmentPlanDto>> Handle(CancelTreatmentPlanCommand request, CancellationToken cancellationToken)
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

            plan.Cancel(request.Reason);

            // Release any note d'honoraires this devis was attached to, in the SAME save — see
            // `TreatmentPlanBridgeRelease` for what a missing detach costs. Shared with the stop command's
            // cancel branch rather than copied into it.
            await TreatmentPlanBridgeRelease.DetachAsync(
                _invoiceRepository, clinicResult.Value, plan.Id, cancellationToken);

            // ⚠️ The séances booked for work that will not happen go with the devis. They used to stay on the
            // agenda — the reminder sent, the worklist chasing them — and every later save of such a visit was
            // refused, since it pointed at a cancelled devis. Only work not yet carried out is released: a
            // finished act's visit happened.
            var toCancel = _appointmentRepository is null
                ? new List<Guid>()
                : await PlanBookingRelease.ReleaseAsync(
                    plan.Items.Where(i => i.Status != TreatmentPlanItemStatus.Done).Select(i => i.Id).ToList(),
                    clinicResult.Value, _appointmentRepository, cancellationToken);

            _unitOfWork.SetExpectedVersion(plan, request.Version);
            await _planRepository.UpdateAsync(plan, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            if (_sender is not null)
            {
                await PlanBookingRelease.CancelEmptiedAsync(
                    _sender, toCancel, "Devis annulé", _logger, cancellationToken);
            }

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
            _logger.LogError(ex, "Error cancelling treatment plan {PlanId}", request.Id);
            return Result<TreatmentPlanDto>.Failure("Erreur lors de l'annulation du plan de traitement.");
        }
    }
}
