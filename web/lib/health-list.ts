import type { KeyboardEvent } from "react"

/**
 * The bullet the three health fields type for the user, and the one every reader strips back off.
 *
 * <p>⚠️ <b>A `•` and a trailing space, matching what `splitPatientWarnings` has always accepted.</b> That
 * function — the notes strip's — already split a textarea on newlines and stripped a hand-typed bullet, so this
 * is not a new convention, it is the same one made to type itself.</p>
 */
export const HEALTH_BULLET = "• "

/**
 * A line that is nothing but a bullet — what the user has in front of them when they press Enter twice.
 *
 * ⚠️ The character class is `splitPatientWarnings`' verbatim: people paste lists written with `-`, `–`, `—` and
 * `*` as often as with `•`, and a reader that only knew its own bullet would print somebody else's back at them.
 */
const BULLET_ONLY = /^\s*[-–—•*]\s*$/
const LEADING_BULLET = /^\s*[-–—•*]\s*/

/**
 * « Allergies », « Maladies » or « Médicaments » as the list the practitioner typed.
 *
 * <p>⚠️ <b>Newlines first, commas only as a fallback, and the fallback is what protects every record written
 * before this existed.</b> The three fields were free text with no shape at all, so a practice that has been
 * running for a year has « Hypertension, diabète de type 2 » on file — one line, two facts. Splitting on
 * newlines alone would render that as a single item and quietly stop showing it as the two things it is;
 * splitting on both always would break « Kardégic 75 mg, 1 le matin », where the comma is part of one
 * medication's posology. So: a value that has newlines is a list the user built, and its lines are taken as
 * written, commas included. A value with no newline at all is legacy or a quick one-liner, and its commas are
 * the only separator it can have.</p>
 *
 * <p>Bullets are stripped on the way out because they are an input affordance, not data: the rendered list
 * draws its own markers, and printing « • • Pénicilline » is what a second bullet would produce.</p>
 */
export function splitHealthList(value: string | null | undefined): string[] {
  if (!value) return []

  const lines = value.split("\n")
  const source = lines.length > 1 ? lines : value.split(",")

  return source.map((line) => line.replace(LEADING_BULLET, "").trim()).filter(Boolean)
}

/**
 * Turn Enter into « next bullet » inside a health textarea.
 *
 * <p>Three behaviours, and each is what a person who has used any list editor already expects:</p>
 * <ul>
 *   <li><b>Enter</b> ends the line and opens the next one with a bullet — and back-fills a bullet onto the
 *       first line, so the list does not read as one bare item above a bulleted rest.</li>
 *   <li><b>Enter on a line holding only a bullet</b> removes it and leaves a blank line. Without this the field
 *       cannot be left except by deleting the bullet by hand, and every abandoned list ends in a dangling « • »
 *       that {@link splitHealthList} then has to drop.</li>
 *   <li><b>Shift+Enter</b> is left alone — a plain newline, for a posology that genuinely runs to two lines.</li>
 * </ul>
 *
 * <p>⚠️ <b>`isComposing` is checked before anything else.</b> On an IME the first Enter commits the candidate
 * rather than ending the line, so acting on it would insert a bullet in the middle of a word being composed.</p>
 *
 * <p>⚠️ <b>The DOM value and the caret are set SYNCHRONOUSLY, before `onChange`, and a deferred restore is a
 * real defect rather than a style choice.</b> These are controlled textareas, so the obvious shape — call
 * `onChange` and put the caret back in a `requestAnimationFrame` — loses a race against the user: typing
 * « Metformine » straight after Enter put « Metf » at the stale caret, then the frame fired and moved the caret,
 * and the rest of the word landed somewhere else entirely (measured:
 * <code>• Kardégic 75 mg\n• ormine 850 mg\n• MetfAmlodipine 5 mg</code>). Nobody types slowly enough for a frame
 * to be safe.</p>
 *
 * <p>Writing `el.value` first is what makes the synchronous `setSelectionRange` correct — the caret is an offset
 * into the NEW text — and React then renders the identical string, so it skips the DOM write altogether and
 * leaves the selection alone.</p>
 */
export function handleHealthBulletKeyDown(
  event: KeyboardEvent<HTMLTextAreaElement>,
  onChange: (next: string) => void,
): void {
  if (event.key !== "Enter" || event.shiftKey || event.nativeEvent.isComposing) return

  const el = event.currentTarget
  const { value, selectionStart, selectionEnd } = el

  const lineStart = value.lastIndexOf("\n", selectionStart - 1) + 1
  const currentLine = value.slice(lineStart, selectionStart)

  event.preventDefault()

  let next: string
  let caret: number

  if (BULLET_ONLY.test(currentLine) && currentLine.length > 0) {
    // Leaving the list: drop the empty bullet, keep the newline that ends the previous item.
    next = `${value.slice(0, lineStart)}\n${value.slice(selectionEnd)}`
    caret = lineStart + 1
  } else {
    const before = value.slice(0, selectionStart)
    const after = value.slice(selectionEnd)

    // Back-fill the first line so a two-item list is not one bare item above a bulleted one.
    const firstBreak = before.indexOf("\n")
    const head = firstBreak === -1 ? before : before.slice(0, firstBreak)
    const needsBackfill = head.trim().length > 0 && !LEADING_BULLET.test(head)
    const bulleted = needsBackfill ? `${HEALTH_BULLET}${before}` : before

    next = `${bulleted}\n${HEALTH_BULLET}${after}`
    caret = bulleted.length + 1 + HEALTH_BULLET.length
  }

  el.value = next
  el.setSelectionRange(caret, caret)
  onChange(next)
}
