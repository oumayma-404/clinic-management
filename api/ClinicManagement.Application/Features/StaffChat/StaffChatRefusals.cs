using ClinicManagement.Domain.Entities;

namespace ClinicManagement.Application.Features.StaffChat;

/// <summary>The staff chat's refusals: a code the client branches on, and the French sentence it shows.</summary>
public static class StaffChatRefusals
{
    public const string ConversationNotFoundCode = "staffchat-conversation-not-found";
    public const string MessageNotFoundCode = "staffchat-message-not-found";
    public const string RecipientInactiveCode = "staffchat-recipient-inactive";
    public const string EmptyCode = "staffchat-empty";
    public const string TooLongCode = "staffchat-too-long";
    public const string UnknownQuickMessageCode = "staffchat-unknown-quick-message";
    public const string InvalidQuickMessagesCode = "staffchat-invalid-quick-messages";
    public const string AlreadyClaimedCode = "staffchat-already-claimed";
    public const string NotClaimableCode = "staffchat-not-claimable";
    public const string NotYourClaimCode = "staffchat-not-your-claim";
    public const string PatientNotFoundCode = "staffchat-patient-not-found";
    public const string AppointmentNotFoundCode = "staffchat-appointment-not-found";
    public const string ArrivalAlreadySentCode = "staffchat-arrival-already-sent";
    public const string NoDentistCode = "staffchat-no-dentist";
    public const string DentistNoAccountCode = "staffchat-dentist-no-account";
    public const string DentistInactiveCode = "staffchat-dentist-inactive";
    public const string OwnAppointmentCode = "staffchat-own-appointment";
    public const string AppointmentNotTodayCode = "staffchat-appointment-not-today";
    public const string AppointmentCancelledCode = "staffchat-appointment-cancelled";
    public const string NoPatientCode = "staffchat-no-patient";

    public const string ConversationNotFound = "Conversation introuvable.";
    public const string MessageNotFound = "Message introuvable.";
    public const string RecipientInactive = "Ce compte est désactivé : la conversation est en lecture seule.";
    public const string Empty = "Le message est vide.";
    public static readonly string TooLong = $"Un message ne peut pas dépasser {StaffMessage.BodyMaxLength} caractères.";
    public const string UnknownQuickMessage = "Ce message rapide n'existe plus. Rechargez la liste.";
    public const string NotClaimable = "Ce message ne se prend pas en charge.";
    public const string NotYourClaim = "Seule la personne qui a pris ce message peut le rendre.";
    public const string PatientNotFound = "Patient introuvable.";
    public const string AppointmentNotFound = "Rendez-vous introuvable.";

    // AC-3.4 — each says why « Signaler l'arrivée » cannot go, beside the action that stays visible.
    public const string NoDentist = "Aucun praticien sur ce rendez-vous.";
    public const string DentistNoAccount = "Ce praticien n'a pas de compte dans l'application.";
    public const string DentistInactive = "Ce praticien n'est plus actif.";
    public const string OwnAppointment = "C'est votre propre rendez-vous.";
    public const string AppointmentNotToday = "L'arrivée se signale le jour du rendez-vous.";
    public const string AppointmentCancelled = "Ce rendez-vous est annulé.";
    public const string NoPatient = "Ce créneau n'a pas de patient.";

    /// <summary>AC-3.3 / EC-5 — when and by whom, so the second person decides knowingly.</summary>
    public static string ArrivalAlreadySent(string time, string name) => $"Signalé à {time} par {name}.";

    /// <summary>AC-5.2 — the name is the point: the second secretary needs to know who to ask, not that something failed.</summary>
    public static string AlreadyClaimed(string name) => $"Déjà pris par {name}.";

    public const string SessionInvalid = "Session invalide, veuillez vous reconnecter.";
    public const string UserNotFound = "Utilisateur introuvable.";
}
