using MediatR;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Domain.ValueObjects;

namespace ClinicManagement.Application.Features.Patients.Commands;

public class UpdatePatientCommand : IRequest<Result<PatientDto>>
{
    public Guid Id { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public DateTime? DateOfBirth { get; set; }
    public string? Gender { get; set; }
    public string? Email { get; set; }
    public string? PhoneNumber { get; set; }
    public AddressDto? Address { get; set; }
    public InsuranceInfoDto? InsuranceInfo { get; set; }
    public string? MedicalHistory { get; set; }
    public string? Allergies { get; set; }
}

public class UpdatePatientCommandHandler : IRequestHandler<UpdatePatientCommand, Result<PatientDto>>
{
    private readonly IPatientRepository _patientRepository;
    private readonly IUnitOfWork _unitOfWork;

    public UpdatePatientCommandHandler(IPatientRepository patientRepository, IUnitOfWork unitOfWork)
    {
        _patientRepository = patientRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<PatientDto>> Handle(UpdatePatientCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var patient = await _patientRepository.GetByIdAsync(request.Id, cancellationToken);
            if (patient == null)
            {
                return Result<PatientDto>.Failure("Patient not found");
            }

            // Update personal info if any fields are provided
            if (request.FirstName != null || request.LastName != null || request.DateOfBirth.HasValue || 
                request.Gender != null || request.Email != null || request.PhoneNumber != null || request.Address != null)
            {
                var firstName = request.FirstName ?? patient.FirstName;
                var lastName = request.LastName ?? patient.LastName;
                var dateOfBirth = request.DateOfBirth ?? patient.DateOfBirth;
                
                if (dateOfBirth.Kind == DateTimeKind.Unspecified)
                {
                    dateOfBirth = DateTime.SpecifyKind(dateOfBirth, DateTimeKind.Utc);
                }
                else if (dateOfBirth.Kind == DateTimeKind.Local)
                {
                    dateOfBirth = dateOfBirth.ToUniversalTime();
                }

                var gender = request.Gender ?? patient.Gender;
                var email = !string.IsNullOrWhiteSpace(request.Email) 
                    ? new Email(request.Email) 
                    : patient.Email;
                var phoneNumber = !string.IsNullOrWhiteSpace(request.PhoneNumber) 
                    ? new PhoneNumber(request.PhoneNumber) 
                    : patient.PhoneNumber;

                Address? address = null;
                if (request.Address != null)
                {
                    address = new Address(
                        request.Address.Street,
                        request.Address.City,
                        request.Address.State,
                        request.Address.ZipCode,
                        request.Address.Country);
                }
                else
                {
                    address = patient.Address;
                }

                patient.UpdatePersonalInfo(firstName, lastName, dateOfBirth, gender, email, phoneNumber, address);
            }

            // Update insurance info if provided
            if (request.InsuranceInfo != null)
            {
                var insuranceInfo = new InsuranceInfo(
                    request.InsuranceInfo.Provider,
                    request.InsuranceInfo.PolicyNumber,
                    request.InsuranceInfo.GroupNumber,
                    request.InsuranceInfo.ExpiryDate);
                patient.UpdateInsuranceInfo(insuranceInfo);
            }
            else if (request.InsuranceInfo == null && patient.InsuranceInfo != null)
            {
                // If InsuranceInfo is explicitly set to null, clear it
                // Note: This requires checking if the DTO has a way to indicate "clear"
                // For now, we'll only update if InsuranceInfo is provided
            }

            // Update medical history if provided
            if (request.MedicalHistory != null || request.Allergies != null)
            {
                var medicalHistory = request.MedicalHistory ?? patient.MedicalHistory;
                var allergies = request.Allergies ?? patient.Allergies;
                patient.UpdateMedicalHistory(medicalHistory, allergies);
            }

            await _patientRepository.UpdateAsync(patient, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            // Map to DTO
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
                CreatedAt = patient.CreatedAt,
                Address = patient.Address != null ? new AddressDto
                {
                    Street = patient.Address.Street,
                    City = patient.Address.City,
                    State = patient.Address.State,
                    ZipCode = patient.Address.ZipCode,
                    Country = patient.Address.Country
                } : null,
                InsuranceInfo = patient.InsuranceInfo != null ? new InsuranceInfoDto
                {
                    Provider = patient.InsuranceInfo.Provider,
                    PolicyNumber = patient.InsuranceInfo.PolicyNumber,
                    GroupNumber = patient.InsuranceInfo.GroupNumber,
                    ExpiryDate = patient.InsuranceInfo.ExpiryDate
                } : null,
                Flags = patient.Flags.Select(f => new PatientFlagDto
                {
                    Id = f.Id,
                    FlagType = f.FlagType.ToString(),
                    Description = f.Description,
                    Notes = f.Notes,
                    IsActive = f.IsActive
                }).ToList()
            };

            return Result<PatientDto>.Success(dto);
        }
        catch (Exception ex)
        {
            return Result<PatientDto>.Failure($"Error updating patient: {ex.Message}");
        }
    }
}

