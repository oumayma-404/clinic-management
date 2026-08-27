namespace ClinicManagement.Domain.Enums;

/// <summary>
/// Persistent clinical state of a single tooth on a patient's odontogram. <c>Sain</c> is the implicit
/// default (a tooth with no recorded state), so a tooth reset to <c>Sain</c> clears its stored row.
/// </summary>
public enum ToothCondition
{
    Sain = 0,
    Carie = 1,
    Obturation = 2,
    Couronne = 3,
    TraitementDeCanal = 4,
    Bridge = 5,
    Implant = 6,
    ExtraitAbsent = 7,
    ATraiter = 8
}
