using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.Patients.Commands;

/// <summary>
/// Delete a patient. Refused whenever anything at all is attached — the message names what actually blocks it,
/// and archiving is offered instead.
///
/// This used to rely on catching <see cref="DbUpdateException"/>, which was a lie twice over: appointments,
/// tooth states, dental records and files <b>cascaded away</b> rather than blocking, and invoices and treatment
/// plans have no foreign key at all so nothing ever raised for them — they were silently orphaned. The check is
/// now an explicit count taken before the delete.
/// </summary>
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

            var counts = await _patientRepository.GetLinkedDataCountsAsync(patient.Id, cancellationToken);
            if (counts.Any)
            {
                return Result.Failure(
                    $"Impossible de supprimer {patient.GetFullName()} : "
                    + $"{PatientDeletionBlockers.Describe(counts)} y sont rattachés. "
                    + "Archivez le patient pour le retirer des listes sans rien supprimer.");
            }

            await _patientRepository.DeleteAsync(patient.Id, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return Result.Success();
        }
        catch (DbUpdateException)
        {
            // Defence in depth. The pre-check above should have caught everything, so reaching here means a row
            // was attached between the count and the delete — a race, not the normal path.
            return Result.Failure(
                "Impossible de supprimer ce patient : des données lui ont été rattachées entre-temps. "
                + "Réessayez.");
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            // AC-13.2: the detail goes to the log; the caller only ever sees French guidance.
            _logger.LogError(ex, "Unhandled failure deleting patient");
            return Result.Failure("Erreur lors de la suppression du patient. Veuillez réessayer.");
        }
    }
}
