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
  /** Who said « Je m'en occupe » on a secretariat request, and when. */
  claimedBy: { userId: string; name: string; at: string } | null
  /** The thread's other members who have read up to this message — filled on a thread page. */
  /** The patient the message is about, as it is now. */
  patient: StaffMessagePatientLink | null
  /** The appointment the message is about, as it is now. */
  appointment: StaffMessageAppointmentLink | null
  seenBy: { userId: string; name: string }[]
}

export interface StaffMessagePatientLink {
  id: string
  /** Null when the patient no longer exists. */
  name: string | null
  archived: boolean
  missing: boolean
}

export interface StaffMessageAppointmentLink {
  id: string
  startsAt: string | null
  dentistName: string | null
  cancelled: boolean
  missing: boolean
}

/** The arrival action for one appointment of today. */
export interface StaffArrivalDto {
  appointmentId: string
  /** The latest arrival sent for it today, by anyone; null when none. */
  sentAt: string | null
  sentBy: { userId: string; name: string } | null
  /** Why this person cannot send it (AC-3.4); null when they can. */
  unavailable: string | null
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
  AlreadyClaimed: "staffchat-already-claimed",
  NotClaimable: "staffchat-not-claimable",
  NotYourClaim: "staffchat-not-your-claim",
  ArrivalAlreadySent: "staffchat-arrival-already-sent",
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
  send: (
    conversation: string,
    message: { body: string | null; quickMessageKey: string | null; patientId: string | null; appointmentId: string | null },
  ) => apiPost<StaffMessageDto>("/staff-chat/messages", { conversation, ...message }),

  arrivalsToday: () => apiGet<StaffArrivalDto[]>("/staff-chat/arrivals/today"),

  /** « Patient arrivé » to the appointment's dentist; 409 « Signalé à … » unless `resend`. */
  sendArrival: (appointmentId: string, resend: boolean) =>
    apiPost<StaffMessageDto>("/staff-chat/arrivals", { appointmentId, resend }),

  /** « Je m'en occupe »: 409 « Déjà pris par … » when a colleague was first. */
  claim: (messageId: string) => apiPost<StaffMessageDto>(`/staff-chat/messages/${messageId}/claim`, {}),

  /** « Je ne peux plus »: only the taker. */
  releaseClaim: (messageId: string) => apiDelete<StaffMessageDto>(`/staff-chat/messages/${messageId}/claim`),

  quickMessages: () => apiGet<StaffQuickMessageListDto>("/staff-chat/quick-messages"),

  /** `version` is the one the editor opened with — 0 for the defaults; a colleague's save in between is a 409. */
  saveQuickMessages: (version: number, items: StaffQuickMessageInput[]) =>
    apiPut<StaffQuickMessageListDto>("/staff-chat/quick-messages", { version, items }),

  resetQuickMessages: () => apiDelete<StaffQuickMessageListDto>("/staff-chat/quick-messages"),

  markRead: (conversation: string, upToMessageId: string) =>
    apiPost<void>("/staff-chat/conversations/read", { conversation, upToMessageId }),
}
