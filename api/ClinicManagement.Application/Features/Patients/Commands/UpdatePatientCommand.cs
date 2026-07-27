using System.Text.Json.Serialization;
using MediatR;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Domain.ValueObjects;

namespace ClinicManagement.Application.Features.Patients.Commands;

public class UpdatePatientCommand : IRequest<Result<PatientDto>>
{
    /// <summary>
    /// The <c>Version</c> the client read. Round-tripped so the save is validated against the copy the user
    /// actually edited rather than the one the handler just loaded. Omit (0) to skip the check — the seam
    /// server-internal writers use; see <c>IUnitOfWork.SetExpectedVersion</c>.
    /// </summary>
    public uint Version { get; set; }

    public Guid Id { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public DateTime? DateOfBirth { get; set; }
    public string? Gender { get; set; }
    /// <summary>
    /// Tri-state, same mechanism as <c>UpdateAppointmentCommand</c>: omit the key to leave the value alone,
    /// send an explicit <c>null</c> (or an empty string) to clear it, send a value to set it.
    ///
    /// <para>
    /// Plain nullability is not enough. The old handler read "blank ⇒ keep the existing value", so once a
    /// patient had an e-mail on file there was no request that could remove it — making the columns nullable
    /// alone would have left clearing a silent no-op. System.Text.Json only invokes a setter for a key that is
    /// physically present in the payload, which is what makes the distinction observable.
    /// </para>
    /// </summary>
    public string? Email
    {
        get => _email;
        set { _email = value; EmailSpecified = true; }
    }
    private string? _email;

    [JsonIgnore]
    public bool EmailSpecified { get; private set; }

    /// <inheritdoc cref="Email"/>
    public string? PhoneNumber
    {
        get => _phoneNumber;
        set { _phoneNumber = value; PhoneNumberSpecified = true; }
    }
    private string? _phoneNumber;

    [JsonIgnore]
    public bool PhoneNumberSpecified { get; private set; }
    public AddressDto? Address { get; set; }
    public InsuranceInfoDto? InsuranceInfo { get; set; }
    public CnamInfoDto? CnamInfo { get; set; }
    public string? MedicalHistory { get; set; }
    public string? Allergies { get; set; }
    // Emergency contact (finding #11). null (omitted) = leave unchanged; a present value (even empty) sets/clears.
    public string? EmergencyContactName { get; set; }
    public string? EmergencyContactPhone { get; set; }

    // "Signaler ce patient" toggle + note. null = leave the flag state unchanged (backward-compatible with
    // callers that don't send it); true = ensure an active flag; false = clear any active flag.
    public bool? IsFlagged { get; set; }
    public string? FlagNotes { get; set; }
}

public class UpdatePatientCommandHandler : IRequestHandler<UpdatePatientCommand, Result<PatientDto>>
{
    // Description stamped on the flag created by the "Signaler ce patient" toggle.
    private const string SignaledFlagDescription = "Patient signalé";

    private readonly IPatientRepository _patientRepository;
    private readonly ICurrentClinicResolver _clinicResolver;
    private readonly IUnitOfWork _unitOfWork;

    public UpdatePatientCommandHandler(
        IPatientRepository patientRepository,
        ICurrentClinicResolver clinicResolver,
        IUnitOfWork unitOfWork)
    {
        _patientRepository = patientRepository;
        _clinicResolver = clinicResolver;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<PatientDto>> Handle(UpdatePatientCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var clinicResult = await _clinicResolver.GetClinicIdAsync(cancellationToken);
            if (clinicResult.IsFailure)
            {
                return Result<PatientDto>.Failure(clinicResult.Error ?? "Unable to resolve current clinic");
            }

            var patient = await _patientRepository.GetByIdAsync(request.Id, cancellationToken);
            if (patient == null)
            {
                return Result<PatientDto>.Failure("Patient introuvable.");
            }

            // Explicit tenant check (defense-in-depth alongside the global query filter): a patient
            // from another clinic reads as "not found".
            if (patient.ClinicId != clinicResult.Value)
            {
                return Result<PatientDto>.Failure("Patient introuvable.");
            }

            // AC-5: a provided phone must be a deliverable Tunisian number (same rule as the reminder engine).
            // A legacy patient whose stored number is non-conforming surfaces this error the next time it is
            // edited (the form re-submits the stored value) — the intended tightening, not a retro-invalidation.
            if (!string.IsNullOrWhiteSpace(request.PhoneNumber) && !PhoneNumber.IsDeliverable(request.PhoneNumber))
            {
                return Result<PatientDto>.Failure(
                    "Numéro de téléphone invalide. Utilisez un numéro tunisien à 8 chiffres (ou +216…).");
            }

            // Update personal info if any fields are provided. Contact is deliberately NOT in this condition
            // any more — it has its own tri-state block below, and routing it through UpdatePersonalInfo (six
            // positional parameters) would rewrite name, birth date, gender and address on every contact edit.
            if (request.FirstName != null || request.LastName != null || request.DateOfBirth.HasValue ||
                request.Gender != null || request.Address != null)
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

                patient.UpdatePersonalInfo(
                    firstName, lastName, dateOfBirth, gender, patient.Email, patient.PhoneNumber, address);
            }

            // Contact, tri-state. Each field is resolved independently: an unspecified one keeps whatever is
            // stored, a specified-but-blank one clears.
            if (request.EmailSpecified || request.PhoneNumberSpecified)
            {
                var email = request.EmailSpecified
                    ? (string.IsNullOrWhiteSpace(request.Email) ? null : new Email(request.Email))
                    : patient.Email;
                var phoneNumber = request.PhoneNumberSpecified
                    ? (string.IsNullOrWhiteSpace(request.PhoneNumber) ? null : new PhoneNumber(request.PhoneNumber))
                    : patient.PhoneNumber;

                patient.UpdateContact(email, phoneNumber);
            }

            // Update insurance info. A null/omitted InsuranceInfo clears the stored insurance
            // (the edit dialog sends undefined when both insurance fields are emptied).
            if (request.InsuranceInfo != null)
            {
                var insuranceInfo = new InsuranceInfo(
                    request.InsuranceInfo.Provider,
                    request.InsuranceInfo.PolicyNumber,
                    request.InsuranceInfo.GroupNumber,
                    request.InsuranceInfo.ExpiryDate);
                patient.UpdateInsuranceInfo(insuranceInfo);
            }
            else
            {
                patient.UpdateInsuranceInfo(null);
            }

            // CNAM identity. Unlike insurance, a null/omitted block LEAVES it unchanged (DEV-1) — the edit
            // dialog always sends a present block, so a present-but-empty block still clears the stored value.
            if (request.CnamInfo != null)
            {
                patient.UpdateCnamInfo(request.CnamInfo.ToDomain());
            }

            // Update medical history if provided
            if (request.MedicalHistory != null || request.Allergies != null)
            {
                var medicalHistory = request.MedicalHistory ?? patient.MedicalHistory;
                var allergies = request.Allergies ?? patient.Allergies;
                patient.UpdateMedicalHistory(medicalHistory, allergies);
            }

            // Emergency contact (finding #11): a present block (either field non-null) sets or clears both;
            // an omitted block (both null) leaves the stored value unchanged.
            if (request.EmergencyContactName != null || request.EmergencyContactPhone != null)
            {
                var emergencyPhone = string.IsNullOrWhiteSpace(request.EmergencyContactPhone)
                    ? null
                    : new PhoneNumber(request.EmergencyContactPhone);
                patient.UpdateEmergencyContact(
                    string.IsNullOrWhiteSpace(request.EmergencyContactName) ? null : request.EmergencyContactName.Trim(),
                    emergencyPhone);
            }

            // Patient flag ("Signaler ce patient"): a single active HighPriority flag carries the toggle
            // + note; it feeds the "Urgents" KPI and the flagged filter. A null IsFlagged leaves it unchanged.
            if (request.IsFlagged.HasValue)
            {
                var activeFlag = patient.Flags.FirstOrDefault(f => f.IsActive);
                if (request.IsFlagged.Value)
                {
                    if (activeFlag != null)
                    {
                        activeFlag.Update(activeFlag.Description, request.FlagNotes);
                    }
                    else
                    {
                        patient.AddFlag(new PatientFlag(
                            Guid.NewGuid(), patient.Id, PatientFlagType.HighPriority, SignaledFlagDescription, request.FlagNotes));
                    }
                }
                else
                {
                    foreach (var flag in patient.Flags.Where(f => f.IsActive).ToList())
                    {
                        flag.Deactivate();
                    }
                }
            }

            // Validate the save against the version the USER was editing, not the one this
            // handler just loaded — that one always matches and would detect nothing.
            _unitOfWork.SetExpectedVersion(patient, request.Version);
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
                Email = patient.Email?.Value,
                PhoneNumber = patient.PhoneNumber?.Value,
                MedicalHistory = patient.MedicalHistory,
                Allergies = patient.Allergies,
                EmergencyContactName = patient.EmergencyContactName,
                EmergencyContactPhone = patient.EmergencyContactPhone?.Value,
                CreatedAt = patient.CreatedAt,
                Version = patient.Version,
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
                CnamInfo = patient.CnamInfo.ToDto(),
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
        catch (Exception ex) when (ex is not ConflictException)
        {
            return Result<PatientDto>.Failure($"Error updating patient: {ex.Message}");
        }
    }
}

