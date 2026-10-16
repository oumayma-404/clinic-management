import type { StaffConversationDto, StaffMessageDto } from "@/lib/api/staff-chat"

/** What a message of a secretariat thread offers this person about « Je m'en occupe » (FR-6). */
export type ClaimView =
  | { kind: "offer" }
  | { kind: "mine" }
  | { kind: "taken"; name: string; at: string }
  | null

/** The dentist a secretariat thread belongs to — its one member who is not a secretary. */
export function deskOwnerOf(conversation: StaffConversationDto): string | undefined {
  return conversation.kind === "desk" ? conversation.members.find((m) => !m.isSecretary)?.userId : undefined
}

/**
 * Only the owner's own requests can be taken, only by a secretary of the thread; once taken, everyone in the thread
 * sees who has it (AC-5.1). A message still being sent offers nothing yet.
 */
export function claimViewFor(
  message: StaffMessageDto & { localId?: string },
  conversation: StaffConversationDto,
  viewerId: string | undefined,
): ClaimView {
  if (message.localId || message.sender.userId !== deskOwnerOf(conversation)) return null

  if (message.claimedBy) {
    return message.claimedBy.userId === viewerId
      ? { kind: "mine" }
      : { kind: "taken", name: message.claimedBy.name, at: message.claimedBy.at }
  }

  const viewer = conversation.members.find((m) => m.userId === viewerId)
  return viewer?.isSecretary && viewer.active ? { kind: "offer" } : null
}

/** « Vu » under my last message of a two-person thread, « Vu par Salma, Amel » in a secretariat (AC-5.4). */
export function seenLine(
  message: StaffMessageDto,
  conversation: StaffConversationDto,
  viewerId: string | undefined,
): string | null {
  if (message.sender.userId !== viewerId || message.seenBy.length === 0) return null
  if (conversation.kind === "direct") return "Vu"
  if (conversation.kind === "desk") return `Vu par ${message.seenBy.map((s) => s.name).join(", ")}`
  return null
}
