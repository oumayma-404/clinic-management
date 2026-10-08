using ClinicManagement.Application.Common;

namespace ClinicManagement.Application.Features.Relay;

/// <summary>The step-up actions of the PC de secours: each one proves the authenticator is in hand (AC-1.4, AC-8.4, AC-7.1).</summary>
public static class RelayStepUpActions
{
    public const string Pairing = "relay-pairing";
    public const string Lost = "relay-lost";
    public const string Reclaim = "relay-reclaim";

    /// <summary>« Effacer la copie » on a retired PC (AC-8.2) — an authenticator code, never a password.</summary>
    public const string Erase = "relay-erase";
}

/// <summary>Each PC de secours refusal: its code and its French sentence, kept together (the client branches on the code).</summary>
public static class RelayRefusals
{
    public const string AlreadyPairedCode = "relay_already_paired";
    public const string PairingCodeExpiredCode = "pairing_code_expired";
    public const string UnknownRelayCode = "relay_unknown";
    public const string RetiredCode = "relay_retired";
    public const string NoRelayCode = "no_relay";
    public const string VersionMismatchCode = "relay_version_mismatch";
    public const string StandbyCode = "relay_standby";

    /// <summary>D13: the cabinet's PC de secours fell silent while armed, so the cloud stops recording that cabinet's work.</summary>
    public const string SilentCode = "relay_silent";

    /// <summary>US-4: the PC said it holds the cabinet's saves, so the cloud is read-only for that cabinet.</summary>
    public const string OnRelayCode = "clinic_on_relay";

    /// <summary>FR-5: on a PC de secours holding the cabinet's saves, the « online only » list waits for the internet.</summary>
    public const string OnlineOnlyCode = "online_only";

    /// <summary>AC-7.3: this PC keeps work saved during a cut that never reached the cloud; nothing may erase it.</summary>
    public const string CutWorkKeptCode = "relay_cut_work_kept";
    public const string NotAdminCode = "relay_admin_only";
    public const string InvalidRequestCode = "relay_invalid";
    public const string NotRetiredCode = "relay_not_retired";
    public const string AuthenticatorRequiredCode = "relay_authenticator_required";

    /// <summary>
    /// ⚠️ Not <c>must_change_password</c>: the browser routes that code to « /change-password » with a session this door
    /// never left behind.
    /// </summary>
    public const string PasswordChangeRequiredCode = "relay_password_change_required";

    /// <summary>The credentials door (AC-1.5): a password alone must not be able to install a copy of every record.</summary>
    public const string AuthenticatorRequired =
        "Installer le PC de secours demande un authentificateur : configurez-le d'abord dans « Sécurité ».";

    public const string PasswordChangeRequired =
        "Ce compte doit d'abord choisir un nouveau mot de passe : connectez-vous à APEXA, puis recommencez.";

    /// <summary>« Effacer la copie » on a PC the cloud has not retired (AC-8.2): the copy is still the cabinet's spare.</summary>
    public const string NotRetired =
        "Ce PC est encore le PC de secours du cabinet : retirez-le d'abord depuis « Paramètres » sur le cloud.";

    public static string AlreadyPaired(string label) =>
        $"Un PC de secours est déjà installé (ou en cours d'installation) : {label}.";

    public const string PairingCodeExpired =
        "Ce code d'installation a expiré ou a déjà servi. Relancez l'installation depuis l'application Windows.";

    public const string UnknownRelay = "Ce PC de secours n'est plus reconnu par le cloud.";

    public const string Retired = "Ce PC de secours a été retiré : il ne suit plus le cabinet.";

    /// <summary>A save on a retired PC: it will never accept one again, so « pendant une coupure » would be untrue.</summary>
    public const string RetiredReadOnly =
        "Ce PC de secours a été retiré : sa copie se consulte, elle n'accepte plus d'enregistrement.";

    public const string NoRelay = "Ce cabinet n'a pas de PC de secours.";

    public const string VersionMismatch =
        "Le PC de secours doit se mettre à jour avant de reprendre la copie.";

    /// <summary>AC-6.3: the PC may be about to take over — the form stays open and the save can be pressed again.</summary>
    public const string Silent = "Le PC de secours ne répond plus — réessayez dans un instant.";

    /// <summary>AC-3.5: refused with the form left open; it works again once the cabinet is back on the cloud.</summary>
    public const string OnlineOnly = "Possible uniquement quand internet est revenu au cabinet.";

    public const string CutWorkKept =
        "Ce PC garde du travail enregistré pendant une coupure d'internet qui n'est jamais arrivé dans le cloud : sa copie "
        + "ne peut être ni effacée ni remplacée. Contactez la personne qui a installé votre logiciel.";

    /// <summary>
    /// « 10:42 » on the cabinet's clock — or « le 06/10 à 10:42 » once the takeover is not of today, since a bare hour
    /// would then name the wrong moment.
    /// </summary>
    public static string SinceClinicTime(DateTime sinceUtc, DateTime nowUtc)
    {
        var since = ClinicClock.ToClinicLocal(sinceUtc);
        return since.Date == ClinicClock.ToClinicLocal(nowUtc).Date
            ? since.ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture)
            : since.ToString("'le 'dd/MM' à 'HH:mm", System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>AC-4.2, with the cabinet's own time of the takeover (« depuis 10:42 »).</summary>
    public static string OnRelay(string sinceClinicTime) =>
        $"Le cabinet travaille sur le PC de secours depuis {sinceClinicTime}. Ici, vous pouvez consulter mais pas "
        + "enregistrer jusqu'au retour d'internet au cabinet.";

    public const string Standby =
        "Ce PC de secours garde une copie du cabinet : il n'accepte des enregistrements que pendant une coupure d'internet.";

    public const string NotAdmin = "Seul un administrateur du cabinet peut gérer le PC de secours.";

    public const string InvalidKey = "La clé du PC de secours est invalide.";

    /// <summary>D10: the installer of this cloud build is not published yet (a deploy in progress, or none yet).</summary>
    public const string InstallerUnavailable = "Le cloud ne propose pas encore l'installation du PC de secours.";
}
