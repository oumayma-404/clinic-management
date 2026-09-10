using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Domain.ValueObjects;

namespace ClinicManagement.Application.Features.Patients;

/// <summary>
/// The single Patient → <see cref="PatientDto"/> mapping, matching the co-located-static-helper convention used
/// by Invoices, TreatmentPlans and DentalRecords.
///
/// Patient previously mapped inline in four handlers. That was survivable while the shape was stable, but the
/// archive flag has to appear on every response — the list, the detail, and both write paths — or the frontend
/// cannot tell an archived patient from a live one depending on where it loaded them.
/// </summary>
public static class PatientMappingExtensions
{
    public static PatientDto ToDto(this Patient patient)
    {
        var dto = new PatientDto
        {
            Id = patient.Id,
            ClinicId = patient.ClinicId,
            FirstName = patient.FirstName,
            LastName = patient.LastName,
            DateOfBirth = patient.DateOfBirth,
            Gender = patient.Gender,
            Dentition = patient.Dentition.ToString(),
            DentitionAnswered = patient.DentitionAnsweredAtUtc != null,
            Email = patient.Email?.Value,
            PhoneNumber = patient.PhoneNumber?.Value,
            PhoneE164 = PhoneNumber.ToE164(patient.PhoneNumber?.Value),
            MedicalHistory = patient.MedicalHistory,
            Allergies = patient.Allergies,
            Medications = patient.Medications,
            EmergencyContactName = patient.EmergencyContactName,
            EmergencyContactPhone = patient.EmergencyContactPhone?.Value,
            ReferredBy = patient.ReferredBy,
            Notes = patient.Notes,
            ImportantNotes = patient.ImportantNotes,
            IsArchived = patient.IsArchived,
            ArchivedAt = patient.ArchivedAt,
            ArchiveReason = patient.ArchiveReason,
            CalendarImportPendingReviewSince = patient.CalendarImportPendingReviewSince,
            ReminderConsent = patient.ReminderConsent.ToString(),
            ReminderConsentRecordedAtUtc = patient.ReminderConsentRecordedAtUtc,
            ReminderConsentRecordedBy = patient.ReminderConsentRecordedBy,
            CreatedAt = patient.CreatedAt,
            Version = patient.Version,
            ConsultationReason = patient.ConsultationReason,
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

        // Sent as the enum member's own name, never a French sentence — the storage-key/display-map convention
        // `lib/specialties.ts` and the appointment labels already follow, and what keeps a reworded label from
        // changing behaviour.
        if (patient.TobaccoUse is { } tobacco)
        {
            dto.TobaccoUse = new TobaccoUseDto
            {
                Status = tobacco.Status.ToString(),
                PerDay = tobacco.PerDay,
                Unit = tobacco.Unit?.ToString(),
            };
        }

        dto.CnamInfo = patient.CnamInfo.ToDto();

        return dto;
    }

    /// <summary>
    /// Resolves the « S'agit-il de ce patient ? » suggestion onto every row that carries one
    /// (<c>calendar-import-duplicate-merge</c> AC-8). <b>One batched read</b> for the whole set, and the single
    /// place the nested DTO is built — the pending-review list and the fiche's own banner both call it, so the
    /// question cannot appear on one surface and not the other.
    ///
    /// <para>⚠️ A suggested id that no longer resolves leaves <c>SuggestedDuplicate</c> null rather than failing
    /// the read: the suggested patient may have been deleted or merged away since, and an expired question is no
    /// question. Same reason the column carries no foreign key.</para>
    ///
    /// <para>⚠️ It is <b>not</b> part of <see cref="ToDto(Patient)"/>, which is pure and synchronous and is called
    /// from write paths that have no business issuing a second read. Callers that render the question opt in.</para>
    /// </summary>
    public static async Task AttachSuggestedDuplicatesAsync(
        IReadOnlyCollection<Patient> patients,
        IReadOnlyCollection<PatientDto> dtos,
        IPatientRepository patientRepository,
        CancellationToken cancellationToken = default)
    {
        // Paired on Id rather than by position: the two collections come from one page in one order today, and a
        // caller that maps or filters between them would otherwise attach the question to the wrong patient.
        var asking = patients.Where(p => p.CalendarImportSuggestedDuplicateId.HasValue).ToList();
        if (asking.Count == 0)
        {
            return;
        }

        // The batch read takes the clinic and drops ids outside it, so a suggestion pointing at another practice's
        // patient resolves to nothing rather than leaking a name. Every patient here shares one clinic.
        var suggested = await patientRepository.GetByIdsAsync(
            asking[0].ClinicId,
            asking.Select(p => p.CalendarImportSuggestedDuplicateId!.Value).Distinct().ToList(),
            cancellationToken);

        var byId = dtos.ToDictionary(d => d.Id);

        foreach (var patient in asking)
        {
            if (!suggested.TryGetValue(patient.CalendarImportSuggestedDuplicateId!.Value, out var other))
            {
                continue;
            }

            if (!byId.TryGetValue(patient.Id, out var dto))
            {
                continue;
            }

            var ownPhone = PhoneNumber.ToE164(patient.PhoneNumber?.Value);
            var otherPhone = PhoneNumber.ToE164(other.PhoneNumber?.Value);

            dto.SuggestedDuplicate = new SuggestedDuplicateDto
            {
                Id = other.Id,
                FullName = other.GetFullName(),
                DateOfBirth = other.DateOfBirth,
                Phone = other.PhoneNumber?.Value,
                PhoneMatches = ownPhone != null && ownPhone == otherPhone,
            };
        }
    }
}
