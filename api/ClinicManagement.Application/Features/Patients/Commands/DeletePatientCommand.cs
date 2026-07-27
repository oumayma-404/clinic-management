using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.Patients.Commands;

/// <summary>Delete a patient (finding #15). Tenant-checked; a clear French message if related records block it.</summary>
public class DeletePatientCommand : IRequest<Result>
{
    public Guid Id { get; set; }
}

public class DeletePatientCommandHandler : IRequestHandler<DeletePatientCommand, Result>
{
    private readonly IPatientRepository _patientRepository;
    private readonly ICurrentClinicResolver _clinicResolver;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<DeletePatientCommandHandler> _logger;

    public DeletePatientCommandHandler(
        IPatientRepository patientRepository,
        ICurrentClinicResolver clinicResolver,
        IUnitOfWork unitOfWork,
        ILogger<DeletePatientCommandHandler> logger)
    {
        _patientRepository = patientRepository;
        _clinicResolver = clinicResolver;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<Result> Handle(DeletePatientCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var clinicResult = await _clinicResolver.GetClinicIdAsync(cancellationToken);
            if (clinicResult.IsFailure)
            {
                return Result.Failure(clinicResult.Error ?? "Cabinet introuvable.");
            }

            // Tenant isolation: a patient from another clinic reads as "not found".
            var patient = await _patientRepository.GetByIdAsync(request.Id, cancellationToken);
            if (patient == null || patient.ClinicId != clinicResult.Value)
            {
                return Result.Failure("Patient introuvable.");
            }

            await _patientRepository.DeleteAsync(patient.Id, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return Result.Success();
        }
        catch (DbUpdateException)
        {
            // A related record (facture, rendez-vous, dossier…) with a restricting FK blocks the delete —
            // surface a clear French message instead of a raw 500.
            return Result.Failure("Impossible de supprimer ce patient : des données liées (factures, rendez-vous, dossiers) existent.");
        }
        catch (Exception ex)
        {
            // AC-13.2: the detail goes to the log; the caller only ever sees French guidance.
            _logger.LogError(ex, "Unhandled failure deleting patient");
            return Result.Failure("Erreur lors de la suppression du patient. Veuillez réessayer.");
        }
    }
}
