import type { StaffConversationDto, StaffQuickMessageAudience } from "@/lib/api/staff-chat"

/**
 * Which quick messages a conversation offers (staff-chat FR-4): writing to a dentist — or, as a secretary, in a
 * dentist's secretariat — offers « dentiste »; writing to a secretary or one's own secretariat offers
 * « secrétariat »; « Tout le cabinet » and anyone else offer both. « Réponse » always.
 */
export function audiencesFor(conversation: StaffConversationDto, viewerId: string | undefined): StaffQuickMessageAudience[] {
  const viewer = conversation.members.find((m) => m.userId === viewerId)

  switch (conversation.kind) {
    case "desk":
      return viewer?.isSecretary ? ["dentist", "reply"] : ["desk", "reply"]
    case "direct": {
      const other = conversation.members.find((m) => m.userId !== viewerId)
      if (other?.isDentist) return ["dentist", "reply"]
      if (other?.isSecretary) return ["desk", "reply"]
      return ["dentist", "desk", "reply"]
    }
    default:
      return ["dentist", "desk", "reply"]
  }
}
