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

    public const string NameMismatchCode = "clinic_name_mismatch";

    public const string ReasonRequiredCode = "clinic_deletion_reason_required";

    public const string UnknownClinic =
        "Ce cabinet n'existe pas — ou n'existe plus. Rien n'a été supprimé.";

    /// <summary>
    /// ⚠️ It <b>does not repeat the expected name</b>. The panel already shows it, and the point of typing it is
    /// that the vendor reads the cabinet they are about to destroy — a refusal that spells the answer out turns the
    /// check into a copy-paste.
    /// </summary>
    public const string NameMismatch =
        "Le nom saisi ne correspond pas à celui du cabinet. Rien n'a été supprimé : relisez le nom affiché "
        + "au-dessus et ressaisissez-le exactement.";

    public const string ReasonRequired =
        "Indiquez le motif de la suppression : c'est la seule chose qui restera de ce cabinet, et la seule réponse "
        + "à « pourquoi ces données ont-elles disparu ? » que vos collègues pourront lire ensuite.";

    /// <summary>
    /// Whether what the vendor typed names this cabinet. Trimmed, inner whitespace collapsed and compared
    /// case-insensitively: the check is « did you read the right row », not « can you reproduce the capitals », and
    /// being fussy about either buys nothing while making a real deletion feel broken.
    /// </summary>
    public static bool NamesTheClinic(string? typed, string clinicName) =>
        !string.IsNullOrWhiteSpace(typed)
        && string.Equals(Collapse(typed), Collapse(clinicName), StringComparison.OrdinalIgnoreCase);

    private static string Collapse(string value) =>
        string.Join(' ', value.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
}

/// <summary>
/// Which addresses a deletion frees. One implementation, read by the preview and by the deletion, so the panel
/// cannot promise an address the write does not release.
/// </summary>
public static class ClinicDeletionAddresses
{
    /// <summary>
    /// Every password-backed account of the cabinet, by address.
    ///
    /// <para>⚠️ <b>The <c>PasswordHash</c> test is what makes the list true</b>: <c>Users.Email</c> is unique
    /// <i>filtered on that column being present</i>, and <c>GetByEmailAsync</c> — the read that refuses a signup —
    /// carries the same term. An account with no password holds no address anybody is waiting for.</para>
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
