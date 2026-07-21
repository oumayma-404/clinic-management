using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Entities;

namespace ClinicManagement.Application.Features.Medications.Commands;

// Shared entity → DTO mapping for the medication catalog handlers (keeps the projection in one place).
internal static class MedicationMapper
{
    public static MedicationDto ToDto(Medication m) => new()
    {
        Id = m.Id,
        BrandName = m.BrandName,
        Form = m.Form,
        Strength = m.Strength,
        Dcis = m.ActiveIngredients
            .Select(i => i.Dci)
            .OrderBy(d => d, StringComparer.OrdinalIgnoreCase)
            .ToList(),
        IsActive = m.IsActive,
        IsProvisional = m.IsProvisional,
    };
}
