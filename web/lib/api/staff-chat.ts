import { apiGet, apiPost } from "./client"

/** A colleague as the chat shows them; `online` means the app is open right now, nothing more. */
export interface StaffColleagueDto {
  userId: string
  name: string
  isDentist: boolean
  isSecretary: boolean
  online: boolean
  active: boolean
}

export interface StaffMessageDto {
  id: string
  conversation: string
  sender: { userId: string; name: string }
  sentAt: string
  body: string | null
  quickMessage: { key: string; label: string; urgent: boolean } | null
}

export type StaffConversationKind = "direct" | "desk" | "clinic"

export interface StaffConversationDto {
  key: string
  kind: StaffConversationKind
  title: string
  members: StaffColleagueDto[]
  lastMessage: StaffMessageDto | null
  /** Everything unread here, including what reached this person silently on a shared desk. */
  unreadCount: number
  hasUrgentUnread: boolean
  /** The other side is deactivated: history readable, nothing can be sent. */
  readOnly: boolean
}

export interface StaffMessagePageDto {
  /** Newest first. */
  items: StaffMessageDto[]
  nextCursor: string | null
}

export interface StaffChatUnreadDto {
  /** What rings this person — a colleague's reply on a shared desk is not counted. */
  total: number
  hasUrgent: boolean
  recent: StaffMessageDto[]
}

/** Codes the client branches on — never the French sentence. */
export const StaffChatCode = {
  ConversationNotFound: "staffchat-conversation-not-found",
  RecipientInactive: "staffchat-recipient-inactive",
  Empty: "staffchat-empty",
  TooLong: "staffchat-too-long",
} as const

/** The server's own limit; the counter and the refusal count the same way (trimmed UTF-16 length). */
export const STAFF_MESSAGE_MAX_LENGTH = 1000

export const staffChatApi = {
  conversations: () => apiGet<StaffConversationDto[]>("/staff-chat/conversations"),

  colleagues: () => apiGet<StaffColleagueDto[]>("/staff-chat/colleagues"),

  messages: (conversation: string, before?: string | null) =>
    apiGet<StaffMessagePageDto>("/staff-chat/messages", before ? { conversation, before } : { conversation }),

  unread: () => apiGet<StaffChatUnreadDto>("/staff-chat/unread"),

  /** A retry of the same text reuses the request's Idempotency-Key, so the server answers once. */
  send: (conversation: string, body: string) =>
    apiPost<StaffMessageDto>("/staff-chat/messages", { conversation, body }),

  markRead: (conversation: string, upToMessageId: string) =>
    apiPost<void>("/staff-chat/conversations/read", { conversation, upToMessageId }),
}
