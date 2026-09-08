namespace ClinicManagement.Domain.Enums;

/// <summary>
/// Whether a patient smokes — the « Tabac » question on the medical section of the fiche patient.
///
/// <para>⚠️ <b>There is deliberately no <c>Unknown</c> member.</b> « Nobody has asked yet » is expressed by the
/// whole <see cref="ValueObjects.TobaccoUse"/> being null, so there is exactly one way to say it. A second
/// representation inside the enum would let the same fact be stored two ways, and every read would have to
/// know both.</para>
///
/// <para>The distinction that does matter is <see cref="NonSmoker"/> versus a null block: one is an answer the
/// patient gave, the other is a question nobody put. This file's own neighbours were made optional for exactly
/// that reason — a required « Sexe » and a required « Date de naissance » produced records that said M and
/// 01/01/1980 with no way to tell a guess from an answer.</para>
/// </summary>
public enum SmokingStatus
{
    /// <summary>« Non-fumeur » — asked and answered no.</summary>
    NonSmoker = 1,

    /// <summary>« Fumeur » — the only status that carries a quantity.</summary>
    Smoker = 2,

    /// <summary>« Ancien fumeur ».</summary>
    FormerSmoker = 3,
}
