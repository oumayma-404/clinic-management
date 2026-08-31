using ClinicManagement.Application.Common;
using MediatR;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.Patients.Queries;

public class GetPatientQuery : IRequest<Result<PatientDto>>
{
    public Guid Id { get; set; }
}

public class GetPatientQueryHandler : IRequestHandler<GetPatientQuery, Result<PatientDto>>
{
    private readonly IPatientRepository _patientRepository;
    private readonly IUserRepository _userRepository;
    private readonly IClinicContext _clinicContext;

    public GetPatientQueryHandler(
        IPatientRepository patientRepository,
        IUserRepository userRepository,
        IClinicContext clinicContext)
    {
        _patientRepository = patientRepository;
        _userRepository = userRepository;
        _clinicContext = clinicContext;
    }

    public async Task<Result<PatientDto>> Handle(GetPatientQuery request, CancellationToken cancellationToken)
    {
        try
        {
            // Get user ID from token
            var userId = _clinicContext.GetUserId();
            if (string.IsNullOrEmpty(userId))
            {
                return Result<PatientDto>.Failure("Session invalide, veuillez vous reconnecter.");
            }

            // Get user from database to get clinic ID
            var user = await _userRepository.GetByAuth0SubAsync(userId, cancellationToken);
            if (user == null)
            {
                return Result<PatientDto>.Failure("Utilisateur introuvable.");
            }

            var clinicId = user.ClinicId;

            var patient = await _patientRepository.GetByIdWithAppointmentsAsync(request.Id, cancellationToken);

            if (patient == null)
            {
                return Result<PatientDto>.Failure("Patient introuvable.");
            }

            // Verify patient belongs to user's clinic
            if (patient.ClinicId != clinicId)
            {
                return Result<PatientDto>.Failure("Patient introuvable.");
            }

            var dto = patient.ToDto();

            // The fiche's own « à compléter » banner asks the same question the worklist does, through the same
            // helper — a second resolution here is how the two surfaces start disagreeing about who is suggested.
            await PatientMappingExtensions.AttachSuggestedDuplicatesAsync(
                new[] { patient }, new[] { dto }, _patientRepository, cancellationToken);

            return Result<PatientDto>.Success(dto);
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            return Result<PatientDto>.Failure(ErrorMessages.Generic, ex);
        }
    }
}


