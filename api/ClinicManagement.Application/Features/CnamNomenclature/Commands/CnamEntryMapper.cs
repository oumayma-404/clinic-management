using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Services;

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
        Version = e.Version,
    };

    // The convention fields are projected here rather than at each call site so the letter-values read and the
    // update command's response cannot disagree about what the convention says — the update's response is what
    // the admin screen re-renders straight after pressing « Appliquer ».
    public static CnamLetterValueDto ToDto(CnamLetterValue v)
    {
        var conventionValue = CnamConventionTariffs.ValueFor(v.LettreCle);
        return new CnamLetterValueDto
        {
            Id = v.Id,
            LettreCle = v.LettreCle,
            Value = v.Value,
            IsProvisional = v.IsProvisional,
            Version = v.Version,
            // Null together, always: a source with no value to attribute it to would read as provenance for the
            // clinic's own figure (Vd/Rd — the convention settles nothing for them).
            ConventionValue = conventionValue,
            ConventionSource = conventionValue is null ? null : CnamConventionTariffs.Source,
            ConventionRevisionIntervalYears =
                conventionValue is null ? null : CnamConventionTariffs.RevisionIntervalYears,
        };
    }
}
