using MediatR;
using Microsoft.Extensions.Logging;
using ClinicManagement.Application.Common.Models;
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
    private readonly ICurrentClinicResolver _clinicResolver;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<DeleteDentalRecordCommandHandler> _logger;

    public DeleteDentalRecordCommandHandler(
        IDentalRecordRepository dentalRecordRepository,
        IPatientRepository patientRepository,
        ICurrentClinicResolver clinicResolver,
        IUnitOfWork unitOfWork,
        ILogger<DeleteDentalRecordCommandHandler> logger)
    {
        _dentalRecordRepository = dentalRecordRepository;
        _patientRepository = patientRepository;
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
                return Result<bool>.Failure(clinicResult.Error ?? "Unable to resolve current clinic");
            }

            var dentalRecord = await _dentalRecordRepository.GetByIdAsync(request.Id, cancellationToken);
            if (dentalRecord == null)
            {
                return Result<bool>.Failure("Dossier dentaire introuvable.");
            }

            if (dentalRecord.PatientId != request.PatientId)
            {
                return Result<bool>.Failure("Ce dossier dentaire n'appartient pas à ce patient.");
            }

            // Verify the owning patient belongs to the caller's clinic before deleting.
            var patient = await _patientRepository.GetByIdAsync(dentalRecord.PatientId, cancellationToken);
            if (patient == null || patient.ClinicId != clinicResult.Value)
            {
                return Result<bool>.Failure("Dossier dentaire introuvable.");
            }

            await _dentalRecordRepository.DeleteAsync(request.Id, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return Result<bool>.Success(true);
        }
        catch (Exception ex)
        {
            // AC-13.2: the detail goes to the log; the caller only ever sees French guidance.
            _logger.LogError(ex, "Unhandled failure deleting dental record");
            return Result<bool>.Failure("Erreur lors de la suppression du dossier dentaire. Veuillez réessayer.");
        }
    }
}









