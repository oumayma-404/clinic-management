using MediatR;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace ClinicManagement.Application.Features.Patients.Queries;

public class GetPatientsQuery : IRequest<Result<IEnumerable<PatientDto>>>
{
}

public class GetPatientsQueryHandler : IRequestHandler<GetPatientsQuery, Result<IEnumerable<PatientDto>>>
{
    private readonly IPatientRepository _patientRepository;
    private readonly IUserRepository _userRepository;
    private readonly IClinicContext _clinicContext;

    public GetPatientsQueryHandler(
        IPatientRepository patientRepository,
        IUserRepository userRepository,
        IClinicContext clinicContext)
    {
        _patientRepository = patientRepository;
        _userRepository = userRepository;
        _clinicContext = clinicContext;
    }

    public async Task<Result<IEnumerable<PatientDto>>> Handle(GetPatientsQuery request, CancellationToken cancellationToken)
    {
        try
        {
            // Get user ID from token
            var userId = _clinicContext.GetUserId();
            if (string.IsNullOrEmpty(userId))
            {
                return Result<IEnumerable<PatientDto>>.Failure("User ID not found in token");
            }

            // Get user from database to get clinic ID
            var user = await _userRepository.GetByAuth0SubAsync(userId, cancellationToken);
            if (user == null)
            {
                return Result<IEnumerable<PatientDto>>.Failure("User not found");
            }

            var clinicId = user.ClinicId;

            var patients = await _patientRepository.GetByClinicIdAsync(clinicId, cancellationToken);

            var dtos = patients.Select(p => new PatientDto
            {
                Id = p.Id,
                ClinicId = p.ClinicId,
                FirstName = p.FirstName,
                LastName = p.LastName,
                DateOfBirth = p.DateOfBirth,
                Gender = p.Gender,
                Email = p.Email.Value,
                PhoneNumber = p.PhoneNumber.Value,
                MedicalHistory = p.MedicalHistory,
                Allergies = p.Allergies,
                CreatedAt = p.CreatedAt
            });

            return Result<IEnumerable<PatientDto>>.Success(dtos);
        }
        catch (Exception ex)
        {
            return Result<IEnumerable<PatientDto>>.Failure($"Error retrieving patients: {ex.Message}");
        }
    }
}
