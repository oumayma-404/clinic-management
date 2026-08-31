namespace ClinicManagement.Domain.Enums;

/// <summary>
/// Why an indicative CNAM reimbursement estimate could not be computed. Two causes that look identical on
/// screen (« — ») and need different sentences: one is a gap in the act catalogue the admin can close, the
/// other is a lettre clé the convention itself settles no value for.
/// </summary>
/// <remarks>
/// Carried as the member's own <b>name</b> on <c>ReimbursementEstimateDto</c>, never as a French sentence —
/// recovering an outcome by matching prose is how rewording a message once changed behaviour in this solution.
/// </remarks>
public enum ReimbursementUnavailability
{
    /// <summary>The act carries no cotation. The DCH « Liste des actes » publishes none; the NGAP arrêté does.</summary>
    MissingCoefficient,

    /// <summary>The lettre clé has no valeur (Rd — the convention settles none), so there is nothing to multiply.</summary>
    NoLetterValue,
}
