namespace ClinicManagement.Application.Features.ProcedureTypes;

/// <summary>
/// The refusals a procedure-type write can produce, in French, plus the money ceiling behind one of them.
///
/// <para>Stated once because create and update are two doors onto the same rules, and the price ceiling was
/// missing from <b>both</b>. <c>ConfigureConventions</c> applies <c>decimal(18,3)</c> model-wide, so a tarif of
/// 999 999 999 was accepted by the handler and refused by PostgreSQL — and the refusal reached a French-speaking
/// dentist as an English EF sentence. A bound checked here answers in French before the round trip, and the
/// figure is the column's own rather than a guess: 15 integer digits at scale 3.</para>
/// </summary>
public static class ProcedureTypeRefusals
{
    /// <summary>
    /// The largest tarif the <c>decimal(18,3)</c> column can hold. Deliberately the column's own limit rather
    /// than a « plausible price » — a cabinet's prices are its own business, and a rule invented here would be a
    /// second authority over one the database already states.
    /// </summary>
    public const decimal MaxCost = 999_999_999_999_999m;

    public const string CostTooLarge =
        "Le tarif par défaut est trop élevé. Saisissez un montant en dinars, par exemple 70,000.";

    public const string CostNegative = "Le tarif par défaut ne peut pas être négatif.";

    /// <summary>
    /// A name this cabinet already uses. ⚠️ It was English — « A procedure type with the name 'Détartrage'
    /// already exists » — in a French UI that renders a refusal verbatim, on both the create and the update path.
    /// </summary>
    public static string DuplicateName(string name) =>
        $"Un acte nommé « {name} » existe déjà dans votre catalogue.";
}
