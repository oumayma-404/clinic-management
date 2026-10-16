/**
 * A colleague's colour in « Messages » (staff-chat): the server ranks each account of the cabinet by order of arrival,
 * and this maps that rank onto eight pastels — so a person is the same colour on every screen, and a new colleague
 * changes nobody's. Decorative, never a status.
 *
 * ⚠️ Complete literal class strings: Tailwind never generates a `bg-person-${n}` composed at runtime.
 */
const PERSON_TONES = [
  "bg-person-1",
  "bg-person-2",
  "bg-person-3",
  "bg-person-4",
  "bg-person-5",
  "bg-person-6",
  "bg-person-7",
  "bg-person-8",
] as const

/** No rank (an account that is gone): the neutral wash. */
export function personToneClass(tone: number | null | undefined): string {
  return tone == null || tone < 0 ? "bg-muted" : PERSON_TONES[tone % PERSON_TONES.length]
}
