using MediatR;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.Patients.Queries;

public class GetPatientQuery : IRequest<Result<PatientDto>>
{
    public Guid Id { get; set; }
}

public class GetPatientQueryHandler : IRequestHandler<GetPatientQuery, Result<PatientDto>>
{
    private readonly IPatientRepository _patientRepository;

    public GetPatientQueryHandler(IPatientRepository patientRepository)
    {
        _patientRepository = patientRepository;
    }

    public async Task<Result<PatientDto>> Handle(GetPatientQuery request, CancellationToken cancellationToken)
    {
        var patient = await _patientRepository.GetByIdWithAppointmentsAsync(request.Id, cancellationToken);

        if (patient == null)
        {
            return Result<PatientDto>.Failure("Patient not found");
        }

        var dto = new PatientDto
        {
            Id = patient.Id,
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
}


