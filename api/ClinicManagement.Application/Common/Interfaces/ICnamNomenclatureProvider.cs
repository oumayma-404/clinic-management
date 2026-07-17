using ClinicManagement.Application.DTOs;

namespace ClinicManagement.Application.Common.Interfaces;

/// <summary>
/// Supplies the static, in-code CNAM dental nomenclature (curated reference data — no DB table, no
/// migration, not clinic-scoped). Implemented in Infrastructure so the curated catalogue lives
/// alongside other reference/config data. Values are best-effort defaults pending verification against
/// the current CNAM dental convention.
/// </summary>
public interface ICnamNomenclatureProvider
{
    IReadOnlyList<CnamNomenclatureEntryDto> GetAll();
}
