using MediatR;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.Recall.Commands;

/// <summary>Snooze a patient's recall — drops them off the "à relancer" list for <see cref="Days"/> days (default 30).</summary>
public class SnoozeRecallCommand : IRequest<Result<bool>>
{
    public Guid PatientId { get; set; }
    public int? Days { get; set; }
    public string? Reason { get; set; }
}

public class SnoozeRecallCommandHandler : IRequestHandler<SnoozeRecallCommand, Result<bool>>
{
    private const int DefaultSnoozeDays = 30;

    private readonly IPatientRepository _patientRepository;
    private readonly ICurrentClinicResolver _clinicResolver;
    private readonly IUnitOfWork _unitOfWork;

    public SnoozeRecallCommandHandler(
        IPatientRepository patientRepository,
        ICurrentClinicResolver clinicResolver,
        IUnitOfWork unitOfWork)
    {
        _patientRepository = patientRepository;
        _clinicResolver = clinicResolver;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<bool>> Handle(SnoozeRecallCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var clinic = await _clinicResolver.GetClinicIdAsync(cancellationToken);
            if (clinic.IsFailure)
                return Result<bool>.Failure(clinic.Error ?? "Cabinet introuvable.");

            var patient = await _patientRepository.GetByIdAsync(request.PatientId, cancellationToken);
            if (patient == null || patient.ClinicId != clinic.Value)
                return Result<bool>.Failure("Patient introuvable.");

            var days = request.Days is > 0 ? request.Days.Value : DefaultSnoozeDays;
            patient.SnoozeRecall(DateTime.UtcNow.AddDays(days), request.Reason);

            await _patientRepository.UpdateAsync(patient, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return Result<bool>.Success(true);
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            return Result<bool>.Failure($"Erreur lors du report de la relance : {ex.Message}");
        }
    }
}
