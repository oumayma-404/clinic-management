using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Entities;

namespace ClinicManagement.Application.Features.CnamNomenclature.Commands;

// Shared entity → DTO mapping for the CNAM command handlers (keeps the projection in one place).
internal static class CnamEntryMapper
{
    public static CnamNomenclatureEntryDto ToDto(CnamNomenclatureEntry e) => new()
    {
        Id = e.Id,
        CodeActe = e.CodeActe,
        DesignationFr = e.DesignationFr,
        LettreCle = e.LettreCle,
        Coefficient = e.Coefficient,
        Category = e.Category,
        IsActive = e.IsActive,
        IsProvisional = e.IsProvisional,
    };

    public static CnamLetterValueDto ToDto(CnamLetterValue v) => new()
    {
        Id = v.Id,
        LettreCle = v.LettreCle,
        Value = v.Value,
        IsProvisional = v.IsProvisional,
    };
}
