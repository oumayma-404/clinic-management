using ClinicManagement.Domain.ValueObjects;

namespace ClinicManagement.Application.DTOs;

// Optional CNAM identity block on a patient (spec AC-1). Every field is nullable.
public class CnamInfoDto
{
    public string? IdentifiantUnique { get; set; }
    public string? Regime { get; set; }
    public string? AssureFirstName { get; set; }
    public string? AssureLastName { get; set; }
    public string? AssureAddress { get; set; }
    public string? AssurePostalCode { get; set; }
    public string? MaladeLien { get; set; }
    public string? MaladeLienRang { get; set; }
}

public static class CnamInfoMapping
{
    // Build the domain value object from the DTO; returns null when the DTO is null or carries no value
    // (so an empty block clears the stored identity). Mirrors the inline InsuranceInfo handling.
    public static CnamInfo? ToDomain(this CnamInfoDto? dto)
    {
        if (dto == null)
        {
            return null;
        }

        var vo = new CnamInfo(
            dto.IdentifiantUnique,
            dto.Regime,
            dto.AssureFirstName,
            dto.AssureLastName,
            dto.AssureAddress,
            dto.AssurePostalCode,
            dto.MaladeLien,
            dto.MaladeLienRang);

        return vo.IsEmpty ? null : vo;
    }

    public static CnamInfoDto? ToDto(this CnamInfo? cnam)
    {
        if (cnam == null)
        {
            return null;
        }

        return new CnamInfoDto
        {
            IdentifiantUnique = cnam.IdentifiantUnique,
            Regime = cnam.Regime,
            AssureFirstName = cnam.AssureFirstName,
            AssureLastName = cnam.AssureLastName,
            AssureAddress = cnam.AssureAddress,
            AssurePostalCode = cnam.AssurePostalCode,
            MaladeLien = cnam.MaladeLien,
            MaladeLienRang = cnam.MaladeLienRang
        };
    }
}
