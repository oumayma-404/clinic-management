using MediatR;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.Recall.Commands;

/// <summary>
/// Record that a patient was contacted about their recall (audit stamp) and snooze it ~1 month so it leaves
/// the active list but reappears if they still haven't booked.
/// </summary>
public class MarkRecallContactedCommand : IRequest<Result<bool>>
{
    public Guid PatientId { get; set; }
    public string? Reason { get; set; }
}

public class MarkRecallContactedCommandHandler : IRequestHandler<MarkRecallContactedCommand, Result<bool>>
{
    private const int ContactedSnoozeDays = 30;

    private readonly IPatientRepository _patientRepository;
    private readonly ICurrentClinicResolver _clinicResolver;
    private readonly IUnitOfWork _unitOfWork;

    public MarkRecallContactedCommandHandler(
        IPatientRepository patientRepository,
        ICurrentClinicResolver clinicResolver,
        IUnitOfWork unitOfWork)
    {
        _patientRepository = patientRepository;
        _clinicResolver = clinicResolver;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<bool>> Handle(MarkRecallContactedCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var clinic = await _clinicResolver.GetClinicIdAsync(cancellationToken);
            if (clinic.IsFailure)
                return Result<bool>.Failure(clinic.Error ?? "Cabinet introuvable.");

            var patient = await _patientRepository.GetByIdAsync(request.PatientId, cancellationToken);
            if (patient == null || patient.ClinicId != clinic.Value)
                return Result<bool>.Failure("Patient introuvable.");

            patient.MarkRecallContacted(DateTime.UtcNow.AddDays(ContactedSnoozeDays), request.Reason);

            await _patientRepository.UpdateAsync(patient, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return Result<bool>.Success(true);
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            return Result<bool>.Failure($"Erreur lors de l'enregistrement du contact : {ex.Message}");
        }
    }
}
