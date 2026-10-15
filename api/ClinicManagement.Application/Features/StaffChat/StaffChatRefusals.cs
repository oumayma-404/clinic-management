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

    public const string ConversationNotFound = "Conversation introuvable.";
    public const string MessageNotFound = "Message introuvable.";
    public const string RecipientInactive = "Ce compte est désactivé : la conversation est en lecture seule.";
    public const string Empty = "Le message est vide.";
    public static readonly string TooLong = $"Un message ne peut pas dépasser {StaffMessage.BodyMaxLength} caractères.";
    public const string UnknownQuickMessage = "Ce message rapide n'existe plus. Rechargez la liste.";
    public const string NotClaimable = "Ce message ne se prend pas en charge.";
    public const string NotYourClaim = "Seule la personne qui a pris ce message peut le rendre.";

    /// <summary>AC-5.2 — the name is the point: the second secretary needs to know who to ask, not that something failed.</summary>
    public static string AlreadyClaimed(string name) => $"Déjà pris par {name}.";

    public const string SessionInvalid = "Session invalide, veuillez vous reconnecter.";
    public const string UserNotFound = "Utilisateur introuvable.";
}
