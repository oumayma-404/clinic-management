using MediatR;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Domain.ValueObjects;

namespace ClinicManagement.Application.Features.Patients.Commands;

public class CreatePatientCommand : IRequest<Result<PatientDto>>
{
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public DateTime DateOfBirth { get; set; }
    public string Gender { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
    public string? MedicalHistory { get; set; }
    public string? Allergies { get; set; }
    public AddressDto? Address { get; set; }
    public InsuranceInfoDto? InsuranceInfo { get; set; }
    public CnamInfoDto? CnamInfo { get; set; }
    public string? EmergencyContactName { get; set; }
    public string? EmergencyContactPhone { get; set; }
    public List<MedicalHistoryEntryDto>? MedicalHistoryEntries { get; set; }
    public List<FamilyHistoryEntryDto>? FamilyHistoryEntries { get; set; }

    // "Signaler ce patient" toggle + note at creation (feeds the "Urgents" KPI / flagged filter).
    public bool? IsFlagged { get; set; }
    public string? FlagNotes { get; set; }
}

public class MedicalHistoryEntryDto
{
    public string Description { get; set; } = string.Empty;
    public DateTime? Date { get; set; }
    public string? Notes { get; set; }
}

public class FamilyHistoryEntryDto
{
    public string Relationship { get; set; } = string.Empty;
    public string Condition { get; set; } = string.Empty;
    public string? Notes { get; set; }
}

public class CreatePatientCommandHandler : IRequestHandler<CreatePatientCommand, Result<PatientDto>>
{
    // Description stamped on the flag created by the "Signaler ce patient" toggle.
    private const string SignaledFlagDescription = "Patient signalé";

    private readonly IPatientRepository _patientRepository;
    private readonly IUserRepository _userRepository;
    private readonly IClinicContext _clinicContext;
    private readonly IUnitOfWork _unitOfWork;

    public CreatePatientCommandHandler(
        IPatientRepository patientRepository,
        IUserRepository userRepository,
        IClinicContext clinicContext,
        IUnitOfWork unitOfWork)
    {
        _patientRepository = patientRepository;
        _userRepository = userRepository;
        _clinicContext = clinicContext;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<PatientDto>> Handle(CreatePatientCommand request, CancellationToken cancellationToken)
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

            // AC-5: a provided phone must be a deliverable Tunisian number (the same rule the reminder engine
            // uses), else reject at entry so it never silently fails at dispatch. An empty phone is still
            // allowed (keeps the legacy placeholder) — the patient simply can't receive reminders.
            if (!string.IsNullOrWhiteSpace(request.PhoneNumber) && !PhoneNumber.IsDeliverable(request.PhoneNumber))
            {
                return Result<PatientDto>.Failure(
                    "Numéro de téléphone invalide. Utilisez un numéro tunisien à 8 chiffres (ou +216…).");
            }

            // Provide default values if email or phone are empty
            var emailValue = string.IsNullOrWhiteSpace(request.Email)
                ? "noemail@example.com"
                : request.Email;
            var phoneValue = string.IsNullOrWhiteSpace(request.PhoneNumber)
                ? "0000000000"
                : request.PhoneNumber;

            var email = new Email(emailValue);
            var phoneNumber = new PhoneNumber(phoneValue);

            // Convert AddressDto to Address value object if provided and valid
            Address? address = null;
            if (request.Address != null && 
                !string.IsNullOrWhiteSpace(request.Address.Street) &&
                !string.IsNullOrWhiteSpace(request.Address.City) &&
                !string.IsNullOrWhiteSpace(request.Address.State) &&
                !string.IsNullOrWhiteSpace(request.Address.ZipCode))
            {
                address = new Address(
                    request.Address.Street,
                    request.Address.City,
                    request.Address.State,
                    request.Address.ZipCode,
                    request.Address.Country);
            }

            // Convert InsuranceInfoDto to InsuranceInfo value object if provided and valid
            InsuranceInfo? insuranceInfo = null;
            if (request.InsuranceInfo != null &&
                !string.IsNullOrWhiteSpace(request.InsuranceInfo.Provider) &&
                !string.IsNullOrWhiteSpace(request.InsuranceInfo.PolicyNumber))
            {
                insuranceInfo = new InsuranceInfo(
                    request.InsuranceInfo.Provider,
                    request.InsuranceInfo.PolicyNumber,
                    request.InsuranceInfo.GroupNumber,
                    request.InsuranceInfo.ExpiryDate);
            }

            // Provide defaults for required fields if not provided
            var dateOfBirth = request.DateOfBirth == default(DateTime) 
                ? DateTime.UtcNow.AddYears(-30) // Default to 30 years ago if not provided
                : request.DateOfBirth;
            var gender = string.IsNullOrWhiteSpace(request.Gender) 
                ? "Unknown" 
                : request.Gender;

            var patient = new Patient(
                Guid.NewGuid(),
                clinicId,
                request.FirstName,
                request.LastName,
                dateOfBirth,
                gender,
                email,
                phoneNumber,
                address,
                insuranceInfo);

            // Set medical history and allergies after creation
            if (!string.IsNullOrWhiteSpace(request.MedicalHistory) || !string.IsNullOrWhiteSpace(request.Allergies))
            {
                patient.UpdateMedicalHistory(request.MedicalHistory, request.Allergies);
            }

            // Optional CNAM identity (ToDomain returns null for an omitted/empty block).
            patient.UpdateCnamInfo(request.CnamInfo.ToDomain());

            // Optional emergency contact (finding #11): name + a Tunisian phone. An empty block clears both.
            if (!string.IsNullOrWhiteSpace(request.EmergencyContactName) || !string.IsNullOrWhiteSpace(request.EmergencyContactPhone))
            {
                var emergencyPhone = string.IsNullOrWhiteSpace(request.EmergencyContactPhone)
                    ? null
                    : new PhoneNumber(request.EmergencyContactPhone);
                patient.UpdateEmergencyContact(
                    string.IsNullOrWhiteSpace(request.EmergencyContactName) ? null : request.EmergencyContactName.Trim(),
                    emergencyPhone);
            }

            // Optional "Signaler ce patient" flag at creation.
            if (request.IsFlagged == true)
            {
                patient.AddFlag(new PatientFlag(
                    Guid.NewGuid(), patient.Id, PatientFlagType.HighPriority, SignaledFlagDescription, request.FlagNotes));
            }

            await _patientRepository.AddAsync(patient, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            // Create medical history entries if provided
            if (request.MedicalHistoryEntries != null && request.MedicalHistoryEntries.Any())
            {
                foreach (var entryDto in request.MedicalHistoryEntries)
                {
                    if (!string.IsNullOrWhiteSpace(entryDto.Description))
                    {
                        var entry = new PatientMedicalHistory(
                            Guid.NewGuid(),
                            patient.Id,
                            entryDto.Description,
                            entryDto.Date,
                            entryDto.Notes);

                        patient.AddMedicalHistoryEntry(entry);
                        await _patientRepository.AddMedicalHistoryEntryAsync(entry, cancellationToken);
                    }
                }
            }

            // Create family history entries if provided
            if (request.FamilyHistoryEntries != null && request.FamilyHistoryEntries.Any())
            {
                foreach (var entryDto in request.FamilyHistoryEntries)
                {
                    if (!string.IsNullOrWhiteSpace(entryDto.Relationship) && !string.IsNullOrWhiteSpace(entryDto.Condition))
                    {
                        var entry = new PatientFamilyHistory(
                            Guid.NewGuid(),
                            patient.Id,
                            entryDto.Relationship,
                            entryDto.Condition,
                            entryDto.Notes);

                        patient.AddFamilyHistoryEntry(entry);
                        await _patientRepository.AddFamilyHistoryEntryAsync(entry, cancellationToken);
                    }
                }
            }

            // Save all changes including history entries
            await _patientRepository.UpdateAsync(patient, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

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
                CreatedAt = patient.CreatedAt
            };

            // Map address to DTO
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

            // Map insurance info to DTO
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

            dto.CnamInfo = patient.CnamInfo.ToDto();

            dto.Flags = patient.Flags.Select(f => new PatientFlagDto
            {
                Id = f.Id,
                FlagType = f.FlagType.ToString(),
                Description = f.Description,
                Notes = f.Notes,
                IsActive = f.IsActive
            }).ToList();

            return Result<PatientDto>.Success(dto);
        }
        catch (Exception ex)
        {
            return Result<PatientDto>.Failure($"Error creating patient: {ex.Message}");
        }
    }
}
