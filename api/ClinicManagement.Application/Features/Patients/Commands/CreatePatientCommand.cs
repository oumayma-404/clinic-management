using MediatR;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Application.Common.Exceptions;
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
    /// <summary>Optional — a walk-in registered with nothing but a name has none (AC-18).</summary>
    public DateTime? DateOfBirth { get; set; }
    public string Gender { get; set; } = string.Empty;
    /// <summary>
    /// <c>"Child"</c> or <c>"Adult"</c>. Required by the form, but **optional on the wire**: omitted or unrecognised
    /// falls back to <see cref="DentitionRules.FromDateOfBirth"/>, which itself answers null with no date of birth —
    /// so an undated patient is left unasserted rather than charted on adult teeth. The fallback is what keeps the
    /// server-internal creators working — the AI dispatcher and the Google→App sync's placeholder patient know
    /// nothing about teeth, and hard-rejecting them would break appointment sync to make a form field mandatory.
    /// </summary>
    public string? Dentition { get; set; }
    public string Email { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
    public string? MedicalHistory { get; set; }
    public string? Allergies { get; set; }
    public AddressDto? Address { get; set; }
    public InsuranceInfoDto? InsuranceInfo { get; set; }
    public CnamInfoDto? CnamInfo { get; set; }
    public string? EmergencyContactName { get; set; }
    public string? EmergencyContactPhone { get; set; }
    /// <summary>Optional « adressé par » — the referring practitioner, free text.</summary>
    public string? ReferredBy { get; set; }
    /// <summary>Optional patient-level notes; <see cref="ImportantNotes"/> is shown highlighted on the file.</summary>
    public string? Notes { get; set; }
    /// <inheritdoc cref="Notes"/>
    public string? ImportantNotes { get; set; }
    public List<MedicalHistoryEntryDto>? MedicalHistoryEntries { get; set; }
    public List<FamilyHistoryEntryDto>? FamilyHistoryEntries { get; set; }

    // "Signaler ce patient" toggle + note at creation (feeds the "Urgents" KPI / flagged filter).
    public bool? IsFlagged { get; set; }
    public string? FlagNotes { get; set; }

    /// <summary>
    /// « Créer quand même » — the caller has been shown that this person appears to be on file already and has
    /// confirmed they are a different patient.
    ///
    /// <para><b>Absent means "check first"</b>, which is why this is an opt-<i>in</i> override and not an
    /// opt-out guard: every existing caller keeps the safe behaviour without being edited, and a new one has to
    /// say out loud that it means to create a second record. See <see cref="PatientDuplicateIndex"/> for why the
    /// answer cannot simply be to refuse — two patients really can share a name, and this product's own
    /// « Nouveau patient » form is where an emergency walk-in is registered with nothing but a name.</para>
    /// </summary>
    public bool? AllowDuplicate { get; set; }
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

            /*
             * « Ce patient existe déjà » — the duplicate guard, run before anything is built or written.
             *
             * Until this existed, the CSV import was the *only* door that checked: the hand-typed form and the
             * appointment dialog's « Nouveau patient » switch both created a second file for a patient already on
             * record without a word, and a duplicate is the one mistake this product cannot undo (no merge, no soft
             * delete — and `DeletePatientCommand` refuses as soon as anything is attached). The concrete report was
             * a receptionist booking an appointment with the inline new-patient form and finding the person listed
             * twice on /patients afterwards.
             *
             * ⚠️ Advisory, not a prohibition. Two different people can share a name, and this form is also how a
             * walk-in with nothing but a name gets registered — so a match is refused with
             * `PatientDuplicateIndex.RefusalCode`, and the client offers « Créer quand même », which comes back as
             * `AllowDuplicate`. Same contract as the appointment collision.
             *
             * ⚠️ It reads the clinic's whole identity projection (five columns) rather than issuing a targeted
             * query. That is deliberate: matching folds names through `SearchTerm.Normalize` and phones through
             * `PhoneNumber.ToE164`, so a SQL predicate would be a *second* definition of "the same person" and would
             * be the one that drifts. Creating a patient is an occasional action, not a per-keystroke one.
             */
            if (request.AllowDuplicate != true)
            {
                var identities = await _patientRepository.GetIdentitiesAsync(clinicId, cancellationToken);
                var match = PatientDuplicateIndex.Build(identities).Match(
                    request.LastName,
                    request.FirstName,
                    // Null means "not supplied", and the index reads it as such — the name-alone rule fires instead
                    // of a date comparison.
                    request.DateOfBirth,
                    request.PhoneNumber);

                if (match.Found)
                {
                    return Result<PatientDto>.Failure(
                        PatientDuplicateIndex.Refusal(match),
                        PatientDuplicateIndex.RefusalCode);
                }
            }

            // Every validation and every field of a new patient — the phone rule, the blank-means-blank contact
            // handling, the all-four-parts address, the dentition fallback, the flag. It lives in
            // `PatientFromRequest` rather than here so the CSV import (L5) runs the identical rules without going
            // through MediatR per row; see that file for why sending the command 3 000 times was not an option.
            var built = PatientFromRequest.Build(request, clinicId);
            if (built.IsFailure)
            {
                return Result<PatientDto>.FailureFrom(built);
            }

            var patient = built.Value!;

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
                            patient.ClinicId,
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
                            patient.ClinicId,
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
                Dentition = patient.Dentition.ToString(),
                Email = patient.Email?.Value,
                PhoneNumber = patient.PhoneNumber?.Value,
                MedicalHistory = patient.MedicalHistory,
                Allergies = patient.Allergies,
                EmergencyContactName = patient.EmergencyContactName,
                EmergencyContactPhone = patient.EmergencyContactPhone?.Value,
                ReferredBy = patient.ReferredBy,
                Notes = patient.Notes,
                ImportantNotes = patient.ImportantNotes,
                CreatedAt = patient.CreatedAt,
                Version = patient.Version,
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
        catch (Exception ex) when (ex is not ConflictException)
        {
            return Result<PatientDto>.Failure($"Error creating patient: {ex.Message}");
        }
    }
}
