namespace ClinicManagement.Application.Features.Files;

/// <summary>
/// The French sentences a coffre refusal is expressed in, and the codes a client branches on — kept together,
/// because the sentence and the code are one statement and separate copies are how a reworded message silently
/// stops matching the code it was paired with.
///
/// <para>⚠️ Every sentence says what <b>still works</b> before what does not. These are read chairside, with a
/// patient in the chair and a study on a USB stick, by somebody who needs to know whether the rest of the file
/// is still there. None of them mentions signing in or out — nothing here ends a session.</para>
/// </summary>
public static class FileResidencyRefusals
{
    /// <summary>
    /// This deployment keeps large files at the cabinet, and the machine asking has no coffre. Branched on by the
    /// picker, which turns it into the « ouvrez APEXA au cabinet » path rather than a generic error.
    /// </summary>
    public const string UnavailableCode = "vault_unavailable";

    /// <summary>Past even the coffre's ceiling. Branched on so the picker can name the file rather than the door.</summary>
    public const string TooLargeCode = "vault_too_large";

    public static string Unavailable() =>
        "Les fichiers volumineux sont conservés au cabinet. Vous pouvez consulter et ajouter les autres fichiers "
        + "ici ; pour ajouter celui-ci, ouvrez APEXA sur le poste du cabinet où le coffre est configuré.";

    public static string TooLarge(long maxBytes) =>
        $"Ce fichier dépasse la taille acceptée par le coffre du cabinet ({maxBytes / (1024L * 1024 * 1024)} Go maximum). "
        + "Les autres fichiers du dossier restent accessibles.";

    /// <summary>
    /// A file the catalog files in the coffre, offered to the hosted door. Defense in depth — the picker is told
    /// where each file belongs before it sends anything — so no client branches on it and it carries no code.
    /// </summary>
    public static string BelongsInTheVault() =>
        "Ce fichier est conservé au cabinet plutôt que sur le serveur. Enregistrez-le depuis le poste du cabinet "
        + "où le coffre est configuré ; les autres fichiers s'envoient normalement.";

    /// <summary>
    /// A file small enough for the server, offered to the coffre. The mirror of
    /// <see cref="BelongsInTheVault"/>, and no code for its reason.
    /// </summary>
    public static string BelongsOnTheServer() =>
        "Ce fichier est assez petit pour être conservé sur le serveur : envoyez-le comme les autres, "
        + "il restera accessible depuis n'importe quel poste.";

    /// <summary>
    /// Someone asked the server for a coffre original. It never held one — that is the whole point of the
    /// residency — so this names where the file is rather than reporting a failure.
    /// </summary>
    public static string OriginalIsAtTheCabinet() =>
        "L'original est conservé au cabinet et n'a jamais été transmis au serveur. Ouvrez-le depuis un poste qui "
        + "accède au coffre ; l'aperçu et la fiche restent consultables ici.";
}
