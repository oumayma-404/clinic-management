namespace ClinicManagement.Domain.Enums;

/// <summary>
/// What a smoker's daily quantity is counted in — « cigarettes » or « paquets ».
///
/// <para>⚠️ <b>Never convert between the two.</b> A pack is twenty cigarettes by convention and not by fact, and
/// the patient answered in the unit they think in: « un paquet » and « vingt cigarettes » are the same number
/// and not the same statement. Store the unit that was given and display it back — the same rule the 3D viewer
/// follows when it refuses to invent millimetres for a mesh that records no unit.</para>
/// </summary>
public enum TobaccoUnit
{
    /// <summary>« cigarettes / jour ».</summary>
    Cigarettes = 1,

    /// <summary>« paquets / jour ».</summary>
    Packs = 2,
}
