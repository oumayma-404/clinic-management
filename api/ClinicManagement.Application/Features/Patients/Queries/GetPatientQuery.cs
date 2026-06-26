using MediatR;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
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
                return Result<PatientDto>.Failure("User ID not found in token");
            }

            // Get user from database to get clinic ID
            var user = await _userRepository.GetByAuth0SubAsync(userId, cancellationToken);
            if (user == null)
            {
                return Result<PatientDto>.Failure("User not found");
            }

            var clinicId = user.ClinicId;

            var patient = await _patientRepository.GetByIdWithAppointmentsAsync(request.Id, cancellationToken);

            if (patient == null)
            {
                return Result<PatientDto>.Failure("Patient not found");
            }

            // Verify patient belongs to user's clinic
            if (patient.ClinicId != clinicId)
            {
                return Result<PatientDto>.Failure("Patient not found");
            }

        var dto = new PatientDto
        {
            Id = patient.Id,
            ClinicId = patient.ClinicId,
            FirstName = patient.FirstName,
            LastName = patient.LastName,
            DateOfBirth = patient.DateOfBirth,
            Gender = patient.Gender,
            Email = patient.Email.Value,
            PhoneNumber = patient.PhoneNumber.Value,
            MedicalHistory = patient.MedicalHistory,
            Allergies = patient.Allergies,
            EmergencyContactName = patient.EmergencyContactName,
            EmergencyContactPhone = patient.EmergencyContactPhone?.Value,
            Flags = patient.Flags.Select(f => new PatientFlagDto
            {
                Id = f.Id,
                FlagType = f.FlagType.ToString(),
                Description = f.Description,
                Notes = f.Notes,
                IsActive = f.IsActive
            }).ToList(),
            CreatedAt = patient.CreatedAt
        };

        if (patient.Address != null)
        {
            dto.Address = new AddressDto
            {
                Street = patient.Address.Street,
                City = patient.Address.City,
                State = patient.Address.State,
                ZipCode = patient.Address.ZipCode,
                Country = patient.Address.Country
            };
        }

        if (patient.InsuranceInfo != null)
        {
            dto.InsuranceInfo = new InsuranceInfoDto
            {
                Provider = patient.InsuranceInfo.Provider,
                PolicyNumber = patient.InsuranceInfo.PolicyNumber,
                GroupNumber = patient.InsuranceInfo.GroupNumber,
                ExpiryDate = patient.InsuranceInfo.ExpiryDate
            };
        }

        return Result<PatientDto>.Success(dto);
        }
        catch (Exception ex)
        {
            return Result<PatientDto>.Failure($"Error retrieving patient: {ex.Message}");
        }
    }
}


