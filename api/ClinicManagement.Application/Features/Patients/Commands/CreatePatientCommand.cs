using MediatR;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Domain.ValueObjects;

namespace ClinicManagement.Application.Features.Patients.Commands;

public class CreatePatientCommand : IRequest<Result<PatientDto>>
{
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public DateTime? DateOfBirth { get; set; }
    public string? Gender { get; set; }
    public string? Email { get; set; }
    public string? PhoneNumber { get; set; }
    public AddressDto? Address { get; set; }
    public InsuranceInfoDto? InsuranceInfo { get; set; }
}

public class CreatePatientCommandHandler : IRequestHandler<CreatePatientCommand, Result<PatientDto>>
{
    private readonly IPatientRepository _patientRepository;
    private readonly IUnitOfWork _unitOfWork;

    public CreatePatientCommandHandler(IPatientRepository patientRepository, IUnitOfWork unitOfWork)
    {
        _patientRepository = patientRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<PatientDto>> Handle(CreatePatientCommand request, CancellationToken cancellationToken)
    {
        try
        {
            // Validate required fields
            if (string.IsNullOrWhiteSpace(request.FirstName))
            {
                return Result<PatientDto>.Failure("First name is required");
            }

            if (string.IsNullOrWhiteSpace(request.LastName))
            {
                return Result<PatientDto>.Failure("Last name is required");
            }

            // Use provided values or defaults
            var email = !string.IsNullOrWhiteSpace(request.Email) 
                ? new Email(request.Email) 
                : new Email("unknown@example.com");
            
            var phoneNumber = !string.IsNullOrWhiteSpace(request.PhoneNumber) 
                ? new PhoneNumber(request.PhoneNumber) 
                : new PhoneNumber("000-000-0000");

            // Use provided date of birth or default to today
            var dateOfBirth = request.DateOfBirth ?? DateTime.UtcNow;
            if (dateOfBirth.Kind == DateTimeKind.Unspecified)
            {
                dateOfBirth = DateTime.SpecifyKind(dateOfBirth, DateTimeKind.Utc);
            }
            else if (dateOfBirth.Kind == DateTimeKind.Local)
            {
                dateOfBirth = dateOfBirth.ToUniversalTime();
            }

            // Use provided gender or default
            var gender = !string.IsNullOrWhiteSpace(request.Gender) 
                ? request.Gender 
                : "Unknown";

            Address? address = null;
            InsuranceInfo? insuranceInfo = null;

            if (request.Address != null)
            {
                address = new Address(
                    request.Address.Street,
                    request.Address.City,
                    request.Address.State,
                    request.Address.ZipCode,
                    request.Address.Country);
            }

            if (request.InsuranceInfo != null)
            {
                insuranceInfo = new InsuranceInfo(
                    request.InsuranceInfo.Provider,
                    request.InsuranceInfo.PolicyNumber,
                    request.InsuranceInfo.GroupNumber,
                    request.InsuranceInfo.ExpiryDate);
            }

            var patient = new Patient(
                Guid.NewGuid(),
                request.FirstName,
                request.LastName,
                dateOfBirth,
                gender,
                email,
                phoneNumber,
                address,
                insuranceInfo);

            await _patientRepository.AddAsync(patient, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            var dto = new PatientDto
            {
                Id = patient.Id,
                FirstName = patient.FirstName,
                LastName = patient.LastName,
                DateOfBirth = patient.DateOfBirth,
                Gender = patient.Gender,
                Email = patient.Email.Value,
                PhoneNumber = patient.PhoneNumber.Value,
                Address = request.Address,
                InsuranceInfo = request.InsuranceInfo,
                CreatedAt = patient.CreatedAt
            };

            return Result<PatientDto>.Success(dto);
        }
        catch (Exception ex)
        {
            return Result<PatientDto>.Failure($"Error creating patient: {ex.Message}");
        }
    }
}


