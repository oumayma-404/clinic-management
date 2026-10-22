/**
 * One unsent draft per conversation, per person, for this tab (staff-chat FR-7): it survives closing the panel, a
 * breakpoint crossing and a re-sign-in in the same tab, and dies with the tab. Storage can throw (private mode,
 * blocked site data), so every access is guarded and a failure only means the draft is not remembered.
 */
const PREFIX = "staffchat:draft:"

function keyOf(userId: string, conversation: string): string {
  return `${PREFIX}${userId}:${conversation}`
}

export function readDraft(userId: string | undefined, conversation: string): string {
  if (!userId) return ""
  try {
    return sessionStorage.getItem(keyOf(userId, conversation)) ?? ""
  } catch {
    return ""
  }
}

export function writeDraft(userId: string | undefined, conversation: string, text: string): void {
  if (!userId) return
  try {
    if (text) sessionStorage.setItem(keyOf(userId, conversation), text)
    else sessionStorage.removeItem(keyOf(userId, conversation))
  } catch {
    // Not remembered; the composer still holds the text.
  }
}
