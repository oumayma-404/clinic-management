using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Entities;

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
    /// <param name="includeFlags">
    /// The list read eagerly loads only active flags; the detail read loads all of them. Callers that did not
    /// load the collection at all pass false so EF is never asked to lazy-load one.
    /// </param>
    public static PatientDto ToDto(this Patient patient, bool includeFlags = true)
    {
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
            IsArchived = patient.IsArchived,
            ArchivedAt = patient.ArchivedAt,
            ArchiveReason = patient.ArchiveReason,
            CreatedAt = patient.CreatedAt,
            Flags = includeFlags
                ? patient.Flags.Select(f => new PatientFlagDto
                {
                    Id = f.Id,
                    FlagType = f.FlagType.ToString(),
                    Description = f.Description,
                    Notes = f.Notes,
                    IsActive = f.IsActive
                }).ToList()
                : new List<PatientFlagDto>()
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

        dto.CnamInfo = patient.CnamInfo.ToDto();

        return dto;
    }
}
