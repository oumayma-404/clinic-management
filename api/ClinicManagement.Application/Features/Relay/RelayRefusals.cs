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
    public const string NotAdminCode = "relay_admin_only";
    public const string InvalidRequestCode = "relay_invalid";
    public const string NotRetiredCode = "relay_not_retired";

    /// <summary>« Effacer la copie » on a PC the cloud has not retired (AC-8.2): the copy is still the cabinet's spare.</summary>
    public const string NotRetired =
        "Ce PC est encore le PC de secours du cabinet : retirez-le d'abord depuis « Paramètres » sur le cloud.";

    public static string AlreadyPaired(string label) =>
        $"Un PC de secours est déjà installé (ou en cours d'installation) : {label}.";

    public const string PairingCodeExpired =
        "Ce code d'installation a expiré ou a déjà servi. Relancez l'installation depuis l'application Windows.";

    public const string UnknownRelay = "Ce PC de secours n'est plus reconnu par le cloud.";

    public const string Retired = "Ce PC de secours a été retiré : il ne suit plus le cabinet.";

    public const string NoRelay = "Ce cabinet n'a pas de PC de secours.";

    public const string VersionMismatch =
        "Le PC de secours doit se mettre à jour avant de reprendre la copie.";

    public const string Standby =
        "Ce PC de secours garde une copie du cabinet : il n'accepte des enregistrements que pendant une coupure d'internet.";

    public const string NotAdmin = "Seul un administrateur du cabinet peut gérer le PC de secours.";

    public const string InvalidKey = "La clé du PC de secours est invalide.";
}
