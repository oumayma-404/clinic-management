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

    public const string ConversationNotFound = "Conversation introuvable.";
    public const string MessageNotFound = "Message introuvable.";
    public const string RecipientInactive = "Ce compte est désactivé : la conversation est en lecture seule.";
    public const string Empty = "Le message est vide.";
    public static readonly string TooLong = $"Un message ne peut pas dépasser {StaffMessage.BodyMaxLength} caractères.";

    public const string SessionInvalid = "Session invalide, veuillez vous reconnecter.";
    public const string UserNotFound = "Utilisateur introuvable.";
}
