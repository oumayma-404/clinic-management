using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.Platform;

/// <summary>
/// Why a cabinet's deletion was refused, in French, with the code beside each sentence
/// (<c>clinic-account-removal</c>) — <c>SubscriptionRefusals</c>' shape and for its reason: the sentence and the
/// code are one statement, and two copies is how a reworded message stops matching the code a client branches on.
/// </summary>
public static class ClinicDeletionRefusals
{
    public const string UnknownClinicCode = "clinic_not_found";

    public const string ConfirmationMismatchCode = "clinic_confirmation_mismatch";

    public const string ReasonRequiredCode = "clinic_deletion_reason_required";

    public const string UnknownClinic =
        "Ce cabinet n'existe pas — ou n'existe plus. Rien n'a été supprimé.";

    /// <summary>
    /// ⚠️ Neither sentence <b>repeats the expected value</b>. The panel already shows it, and the point of typing it
    /// is that the vendor reads the cabinet they are about to destroy — a refusal that spells the answer out turns
    /// the check into a copy-paste.
    /// </summary>
    public const string AddressMismatch =
        "L'adresse saisie n'est pas celle d'un compte de ce cabinet. Rien n'a été supprimé : relisez les adresses "
        + "affichées au-dessus et recopiez-en une exactement.";

    public const string NameMismatch =
        "Le nom saisi ne correspond pas à celui du cabinet. Rien n'a été supprimé : relisez le nom affiché "
        + "au-dessus et ressaisissez-le exactement.";

    public const string ReasonRequired =
        "Indiquez le motif de la suppression : c'est la seule chose qui restera de ce cabinet, et la seule réponse "
        + "à « pourquoi ces données ont-elles disparu ? » que vos collègues pourront lire ensuite.";

    /// <summary>
    /// What the vendor must type to confirm. <b>The cabinet's own account address wherever it has one</b>, and its
    /// name only where it has none.
    ///
    /// <para>⚠️ <b>The name is not good enough, and that is a measured hole rather than a preference.</b>
    /// <c>Clinic.Name</c> carries <b>no unique index</b> — only <c>Code</c> does, and nothing checks the name at
    /// creation — so two cabinets may both be called « Cabinet Test ». A typed name therefore catches « the wrong
    /// row, under another name » and <i>passes</i> « the wrong row, under the same name », which is the likeliest
    /// mistake on a deployment holding a pile of trials. An address is unique per install by construction
    /// (<c>Users.Email</c>, filtered on a password being present), so it can only confirm one cabinet.</para>
    ///
    /// <para>⚠️ The name stays as the fall-back rather than being dropped: a cabinet with no password-backed
    /// account has no address to name, and a confirmation nobody can satisfy is a cabinet nobody can delete.</para>
    /// </summary>
    public enum ConfirmationKind
    {
        Address = 0,
        Name = 1,
    }

    public static ConfirmationKind KindFor(IReadOnlyCollection<string> addresses) =>
        addresses.Count > 0 ? ConfirmationKind.Address : ConfirmationKind.Name;

    /// <summary>
    /// Whether what the vendor typed confirms <i>this</i> cabinet: <b>any one</b> of its addresses, or its name when
    /// it has none. Trimmed, inner whitespace collapsed and compared case-insensitively — the check is « did you
    /// read the right row », not « can you reproduce the capitals », and being fussy about either buys nothing while
    /// making a real deletion feel broken.
    ///
    /// <para>⚠️ <b>Any one of them, not all</b>: a cabinet with four colleagues would otherwise need four addresses
    /// typed, and each of the four already identifies the cabinet on its own.</para>
    ///
    /// <para>⚠️ <b>Where an address exists, the cabinet's NAME is refused</b> — deliberately, and it is the whole
    /// point of the change. Accepting either would leave the weaker answer available, which is the one somebody
    /// reaches for.</para>
    /// </summary>
    public static bool Confirms(string? typed, IReadOnlyCollection<string> addresses, string clinicName)
    {
        if (string.IsNullOrWhiteSpace(typed))
        {
            return false;
        }

        var candidate = Collapse(typed);

        return KindFor(addresses) == ConfirmationKind.Address
            ? addresses.Any(a => string.Equals(Collapse(a), candidate, StringComparison.OrdinalIgnoreCase))
            : string.Equals(Collapse(clinicName), candidate, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The refusal that matches what was being asked for, so the sentence names the right field.</summary>
    public static string MismatchFor(IReadOnlyCollection<string> addresses) =>
        KindFor(addresses) == ConfirmationKind.Address ? AddressMismatch : NameMismatch;

    private static string Collapse(string value) =>
        string.Join(' ', value.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
}

/// <summary>
/// Which addresses a deletion frees. One implementation, read by the preview, by the confirmation check and by the
/// deletion, so the panel cannot promise an address the write does not release — nor ask for one it will not accept.
/// </summary>
public static class ClinicDeletionAddresses
{
    /// <summary>
    /// Every password-backed account of the cabinet, by address.
    ///
    /// <para>⚠️ <b>The <c>PasswordHash</c> test is what makes the list true</b>: <c>Users.Email</c> is unique
    /// <i>filtered on that column being present</i>, and <c>GetByEmailAsync</c> — the read that refuses a signup —
    /// carries the same term. An account with no password holds no address anybody is waiting for, and could not
    /// confirm a deletion uniquely either.</para>
    ///
    /// <para>Reads the whole staff list (<c>paging: null</c>) deliberately: a cabinet has a handful of accounts,
    /// and a page would free some addresses and quietly leave the rest taken.</para>
    /// </summary>
    public static async Task<IReadOnlyList<string>> FreedByDeletingAsync(
        IUserRepository users, Guid clinicId, CancellationToken cancellationToken)
    {
        var staff = await users.GetByClinicIdAsync(clinicId, null, null, cancellationToken);

        return staff.Items
            .Where(u => !string.IsNullOrWhiteSpace(u.PasswordHash) && !string.IsNullOrWhiteSpace(u.Email))
            .Select(u => u.Email!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(e => e, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
