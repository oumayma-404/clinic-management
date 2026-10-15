import { apiDelete, apiGet, apiPost, apiPut } from "./client"

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

export type StaffQuickMessageAudience = "dentist" | "desk" | "reply"

export interface StaffQuickMessageDto {
  key: string
  label: string
  for: StaffQuickMessageAudience
  asksForPatient: boolean
  urgent: boolean
  builtIn: boolean
  /** The cabinet's own order — the editor's; the list itself comes in the caller's order. */
  order: number
}

export interface StaffQuickMessageListDto {
  /** 0 while the cabinet uses the defaults. */
  version: number
  /** What this person sends most over 60 days first, then the cabinet's order. */
  items: StaffQuickMessageDto[]
}

/** One row as the editor saves it; a null key is a new message. */
export interface StaffQuickMessageInput {
  key: string | null
  label: string
  for: StaffQuickMessageAudience
  asksForPatient: boolean
  urgent: boolean
}

export const STAFF_QUICK_MESSAGE_LABEL_MAX = 40
export const STAFF_QUICK_MESSAGES_MAX = 30

/** Codes the client branches on — never the French sentence. */
export const StaffChatCode = {
  ConversationNotFound: "staffchat-conversation-not-found",
  RecipientInactive: "staffchat-recipient-inactive",
  Empty: "staffchat-empty",
  TooLong: "staffchat-too-long",
  UnknownQuickMessage: "staffchat-unknown-quick-message",
  InvalidQuickMessages: "staffchat-invalid-quick-messages",
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
  send: (conversation: string, body: string | null, quickMessageKey: string | null = null) =>
    apiPost<StaffMessageDto>("/staff-chat/messages", { conversation, body, quickMessageKey }),

  quickMessages: () => apiGet<StaffQuickMessageListDto>("/staff-chat/quick-messages"),

  /** `version` is the one the editor opened with — 0 for the defaults; a colleague's save in between is a 409. */
  saveQuickMessages: (version: number, items: StaffQuickMessageInput[]) =>
    apiPut<StaffQuickMessageListDto>("/staff-chat/quick-messages", { version, items }),

  resetQuickMessages: () => apiDelete<StaffQuickMessageListDto>("/staff-chat/quick-messages"),

  markRead: (conversation: string, upToMessageId: string) =>
    apiPost<void>("/staff-chat/conversations/read", { conversation, upToMessageId }),
}
