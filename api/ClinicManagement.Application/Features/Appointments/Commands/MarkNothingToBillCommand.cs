using MediatR;
using Microsoft.Extensions.Logging;
using ClinicManagement.Application.Common;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.Appointments.Commands;

/// <summary>
/// « Rien à facturer » — record that a séance will raise no note d'honoraires, or withdraw that.
///
/// <para><b>The escape hatch of last resort.</b> Three legitimate cases are derived and stay derived — a fiche
/// worth nothing, a séance carried by a devis, an existing note — so this is for the case none of those describe.
/// It exists at all because without it a row nothing can satisfy stays flagged for ever, and an alarm that is
/// always on is one nobody reads.</para>
///
/// <para><b>One command with a <c>bool</c>, two routes.</b> Mirrors <c>SetClinicSuspensionFromConsoleCommand</c>:
/// two handlers would be two copies of « resolve · tenant-check · mutate · save », and the direction lives in the
/// <b>URL</b> so no truncated body can flip « je ne facture pas » into « si, finalement ».</para>
///
/// <para><b>Not a clinical erasure.</b> It is <c>AnyClinicRole</c> like the rest of the worklist: reception is
/// who knows that the contrôle was offered, and nothing clinical is destroyed either way — « record yes, erase
/// no » governs the patient's record, and this is a note about money. Both directions are ordinary audited
/// writes, so <c>AuditSaveChangesInterceptor</c> answers « qui a dit que cette séance ne serait pas facturée ? »
/// even after the mark is withdrawn.</para>
/// </summary>
public class MarkNothingToBillCommand : IRequest<Result<bool>>
{
    public Guid AppointmentId { get; set; }

    /// <summary>True to record the mark, false to withdraw it.</summary>
    public bool NothingToBill { get; set; }

    /// <summary>
    /// Why. <b>Mandatory when marking</b>, ignored when withdrawing — the whole value of the mark is that
    /// « pourquoi cette séance n'a produit aucun document ? » stays answerable months later, and a blank motif
    /// answers nothing. Deliberately free text: a closed list would be a second thing to maintain, and the first
    /// clinic to need a motif that is not on it would type the nearest wrong one.
    /// </summary>
    public string? Reason { get; set; }
}

public class MarkNothingToBillCommandHandler : IRequestHandler<MarkNothingToBillCommand, Result<bool>>
{
    private readonly IAppointmentRepository _appointmentRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentClinicResolver _clinicResolver;
    private readonly IClinicContext _clinicContext;
    private readonly ILogger<MarkNothingToBillCommandHandler> _logger;

    public MarkNothingToBillCommandHandler(
        IAppointmentRepository appointmentRepository,
        IUnitOfWork unitOfWork,
        ICurrentClinicResolver clinicResolver,
        IClinicContext clinicContext,
        ILogger<MarkNothingToBillCommandHandler> logger)
    {
        _appointmentRepository = appointmentRepository;
        _unitOfWork = unitOfWork;
        _clinicResolver = clinicResolver;
        _clinicContext = clinicContext;
        _logger = logger;
    }

    public async Task<Result<bool>> Handle(MarkNothingToBillCommand request, CancellationToken cancellationToken)
    {
        try
        {
            if (request.NothingToBill && string.IsNullOrWhiteSpace(request.Reason))
            {
                return Result<bool>.Failure("Le motif est obligatoire.");
            }

            var clinicResult = await _clinicResolver.GetClinicIdAsync(cancellationToken);
            if (clinicResult.IsFailure)
            {
                return Result<bool>.Failure(clinicResult.Error ?? ErrorMessages.Generic);
            }

            var appointment = await _appointmentRepository.GetByIdAsync(request.AppointmentId, cancellationToken);

            // One refusal for « does not exist » and « belongs to another clinic », deliberately: telling the two
            // apart would confirm that an id exists somewhere in the deployment.
            if (appointment is null || appointment.ClinicId != clinicResult.Value)
            {
                return Result<bool>.Failure("Rendez-vous introuvable.");
            }

            if (request.NothingToBill)
            {
                appointment.MarkNothingToBill(
                    request.Reason!, _clinicContext.GetUserId() ?? string.Empty, DateTime.UtcNow);
            }
            else
            {
                appointment.ClearNothingToBill();
            }

            await _appointmentRepository.UpdateAsync(appointment, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return Result<bool>.Success(appointment.IsNothingToBill);
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            _logger.LogError(ex, "Failed to set nothing-to-bill on appointment {AppointmentId}", request.AppointmentId);
            return Result<bool>.Failure(ErrorMessages.Generic);
        }
    }
}
