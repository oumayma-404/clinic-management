using ClinicManagement.Domain.Common;

namespace ClinicManagement.Domain.ValueObjects;

// Optional CNAM (Caisse Nationale d'Assurance Maladie — Tunisia) identity for a patient, used to
// pre-fill the Bulletin de soins (BS1). Every field is optional (spec AC-1): a patient may carry any
// subset, and existing patients simply have none. Stored as an owned value object on Patient.
public class CnamInfo : ValueObject
{
    public string? IdentifiantUnique { get; private set; }
    public string? Regime { get; private set; } // CNSS | CNRPS | Convention bilatérale
    public string? AssureFirstName { get; private set; }
    public string? AssureLastName { get; private set; }
    public string? AssureAddress { get; private set; }
    public string? AssurePostalCode { get; private set; }
    public string? MaladeLien { get; private set; } // assuré lui-même | conjoint | enfant | ascendant
    public string? MaladeLienRang { get; private set; } // enfant rang, or père/mère for ascendant

    private CnamInfo() { } // For EF Core

    public CnamInfo(
        string? identifiantUnique,
        string? regime,
        string? assureFirstName,
        string? assureLastName,
        string? assureAddress,
        string? assurePostalCode,
        string? maladeLien,
        string? maladeLienRang)
    {
        IdentifiantUnique = identifiantUnique;
        Regime = regime;
        AssureFirstName = assureFirstName;
        AssureLastName = assureLastName;
        AssureAddress = assureAddress;
        AssurePostalCode = assurePostalCode;
        MaladeLien = maladeLien;
        MaladeLienRang = maladeLienRang;
    }

    // True when no CNAM field carries a value — the handler treats this as "no CNAM identity" and clears it.
    public bool IsEmpty =>
        string.IsNullOrWhiteSpace(IdentifiantUnique) &&
        string.IsNullOrWhiteSpace(Regime) &&
        string.IsNullOrWhiteSpace(AssureFirstName) &&
        string.IsNullOrWhiteSpace(AssureLastName) &&
        string.IsNullOrWhiteSpace(AssureAddress) &&
        string.IsNullOrWhiteSpace(AssurePostalCode) &&
        string.IsNullOrWhiteSpace(MaladeLien) &&
        string.IsNullOrWhiteSpace(MaladeLienRang);

    protected override IEnumerable<object> GetEqualityComponents()
    {
        yield return IdentifiantUnique ?? string.Empty;
        yield return Regime ?? string.Empty;
        yield return AssureFirstName ?? string.Empty;
        yield return AssureLastName ?? string.Empty;
        yield return AssureAddress ?? string.Empty;
        yield return AssurePostalCode ?? string.Empty;
        yield return MaladeLien ?? string.Empty;
        yield return MaladeLienRang ?? string.Empty;
    }
}
