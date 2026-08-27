using ClinicManagement.Application.Common.Models;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.Subscriptions;

/// <summary>
/// Resolves <b>which cabinet</b> from what the vendor typed — a clinic id, or the e-mail address of somebody who
/// works there (AC-5.1). One implementation for all three vendor commands, so a grant, a cancellation and a
/// suspension cannot disagree about what identifies a practice, nor about how they refuse when it does not exist
/// (AC-5.7's « naming which »).
///
/// <para>An e-mail resolves through its owner's <c>ClinicId</c> and the role is deliberately <b>not</b> checked:
/// the question being answered is « which cabinet », and whose address it is does not change the answer. Refusing a
/// secretary's address would be a puzzling refusal about the wrong subject.</para>
///
/// <para>⚠️ The two refusals name the value that failed, and they are <b>different sentences</b>. « Aucun cabinet
/// avec cet identifiant » and « aucun compte avec cette adresse » send the operator to different places, and a
/// shared « cabinet introuvable » would hide a typo in the e-mail as an unknown practice.</para>
/// </summary>
public static class SubscriptionCabinetLookup
{
    public const string NothingSuppliedError =
        "Indiquez le cabinet : --clinic <identifiant> ou --email <adresse de l'administrateur>.";

    public static async Task<Result<Guid>> ResolveAsync(
        Guid? clinicId,
        string? adminEmail,
        IClinicRepository clinics,
        IUserRepository users,
        CancellationToken cancellationToken = default)
    {
        if (clinicId is { } id && id != Guid.Empty)
        {
            return await clinics.ExistsAsync(id, cancellationToken)
                ? Result<Guid>.Success(id)
                : Result<Guid>.Failure($"Aucun cabinet avec l'identifiant {id}.");
        }

        if (!string.IsNullOrWhiteSpace(adminEmail))
        {
            var trimmed = adminEmail.Trim();
            var user = await users.GetByEmailAsync(trimmed, cancellationToken);
            if (user is null)
            {
                return Result<Guid>.Failure($"Aucun compte avec l'adresse « {trimmed} ».");
            }

            // The id branch checks the cabinet exists; this one has to as well, or an account attached to no
            // practice resolves to Guid.Empty and the verb goes on to blame OUR bookkeeping (« l'abonnement de ce
            // cabinet est introuvable ») for an address that never belonged to a cabinet — the exact confusion the
            // two-distinct-sentences design above exists to avoid. A third accurate sentence, not a shared one.
            return user.ClinicId != Guid.Empty && await clinics.ExistsAsync(user.ClinicId, cancellationToken)
                ? Result<Guid>.Success(user.ClinicId)
                : Result<Guid>.Failure($"Le compte « {trimmed} » n'est rattaché à aucun cabinet.");
        }

        return Result<Guid>.Failure(NothingSuppliedError);
    }
}
