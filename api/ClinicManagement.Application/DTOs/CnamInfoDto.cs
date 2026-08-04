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

    /// <summary>
    /// How many dependants the insured person declares — the input to the annual-ceiling barème (L10).
    /// Not derivable from <see cref="MaladeLien"/>: that describes this patient's relation to the insured person,
    /// while the ceiling depends on the household's size.
    /// </summary>
    public int? DependantCount { get; set; }

    /// <summary>
    /// The household's real annual ceiling when somebody knows it — always wins over the computed barème, whose
    /// figures are sourced rather than officially confirmed. Also where the dependent-parent / disabled-child /
    /// pregnancy supplements land, since each turns on a fact this product does not record.
    /// </summary>
    public decimal? AnnualCeilingOverride { get; set; }
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
            dto.MaladeLienRang,
            dto.DependantCount,
            dto.AnnualCeilingOverride);

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
            MaladeLienRang = cnam.MaladeLienRang,
            DependantCount = cnam.DependantCount,
            AnnualCeilingOverride = cnam.AnnualCeilingOverride
        };
    }
}
