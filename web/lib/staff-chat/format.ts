/** « 14:32 » today, « Hier 14:32 », otherwise « 12/10 14:32 » — 24-hour clock, the workstation's day (staff-chat FR-3). */
export function formatChatTime(iso: string, now: Date = new Date()): string {
  const at = new Date(iso)
  const time = `${pad(at.getHours())}:${pad(at.getMinutes())}`

  if (sameDay(at, now)) return time

  const yesterday = new Date(now)
  yesterday.setDate(now.getDate() - 1)
  if (sameDay(at, yesterday)) return `Hier ${time}`

  return `${pad(at.getDate())}/${pad(at.getMonth() + 1)} ${time}`
}

function sameDay(a: Date, b: Date): boolean {
  return a.getFullYear() === b.getFullYear() && a.getMonth() === b.getMonth() && a.getDate() === b.getDate()
}

function pad(n: number): string {
  return n.toString().padStart(2, "0")
}

/** One line for a conversation row: the quick message's label, else the text's first line. */
export function previewOf(message: { body: string | null; quickMessage: { label: string } | null }): string {
  if (message.quickMessage) return message.quickMessage.label
  return (message.body ?? "").split("\n")[0]
}
