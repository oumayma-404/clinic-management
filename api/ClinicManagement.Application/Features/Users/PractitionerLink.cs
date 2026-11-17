using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.Users;

/// <summary>
/// Which practitioner record an account is — one rule for the three doors that tie the two together
/// (<c>cabinet-de-groupe</c> US-3): inviting a colleague, promoting one to « Médecin », and « Lier à un praticien ».
///
/// <para><b>Why it exists.</b> A dentist added in Paramètres → Praticiens has a record and no account; inviting them
/// afterwards created a <i>second</i> record, and nothing could link or merge the two. Inviting a dentist straight as
/// « Administrateur » created none, so their ordonnances printed no cachet and their work was credited to nobody.
/// A record is now <b>linked, never duplicated</b>: an existing one is chosen, and a record already linked to
/// somebody else is refused rather than stolen (<see cref="Doctor.LinkToUser"/> enforces that too).</para>
/// </summary>
public static class PractitionerLink
{
    public const string UnavailableCode = "practitioner_unavailable";
    public const string UnavailableMessage =
        "Ce praticien ne peut pas être lié : il a déjà un compte, ou il ne fait plus partie du cabinet.";

    public const string AlreadyLinkedCode = "practitioner_already_linked";
    public const string AlreadyLinkedMessage = "Ce compte a déjà un profil praticien.";

    public const string NotAPractitionerMessage = "Un compte secrétaire n'est pas un praticien.";

    public const string InfoRequiredMessage =
        "Le prénom, le nom et la spécialité du praticien sont requis pour le rôle « médecin ».";

    /// <summary>A practitioner of this cabinet, still on the roster and linked to nobody — or the named refusal.</summary>
    public static async Task<Result<Doctor>> FindLinkableAsync(
        IDoctorRepository doctors, Guid clinicId, Guid doctorId, CancellationToken cancellationToken)
    {
        var doctor = await doctors.GetByIdAsync(doctorId, cancellationToken);
        return doctor is null || doctor.ClinicId != clinicId || !doctor.IsActive || doctor.UserId is not null
            ? Result<Doctor>.Failure(UnavailableMessage, UnavailableCode)
            : Result<Doctor>.Success(doctor);
    }

    /// <summary>The three fields a practitioner record needs before it can be created.</summary>
    public static bool HasPractitioner(DoctorPersonalInfoDto? info) =>
        info != null
        && !string.IsNullOrWhiteSpace(info.FirstName)
        && !string.IsNullOrWhiteSpace(info.LastName)
        && !string.IsNullOrWhiteSpace(info.Specialty);

    /// <summary>
    /// The one unlinked practitioner on the roster whose e-mail is the account's — the only thing a role change with
    /// no form behind it can safely link on. Two or none: none, so a guess never ties the wrong record.
    /// </summary>
    public static Doctor? SoleUnlinkedWithEmail(IEnumerable<Doctor> roster, string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return null;
        }

        var matches = roster
            .Where(d => d.IsActive && d.UserId is null && !string.IsNullOrWhiteSpace(d.Email)
                        && string.Equals(d.Email.Trim(), email.Trim(), StringComparison.OrdinalIgnoreCase))
            .Take(2)
            .ToList();
        return matches.Count == 1 ? matches[0] : null;
    }
}
