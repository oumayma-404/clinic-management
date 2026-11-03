/**
 * « Me guider » — in-house guides over the real screens (`guided-first-days` F).
 *
 * A guide is a list of steps. Each step lights the controls carrying one `data-guide` mark, says in one sentence
 * what to do with them, and moves on when the person has done it — the guide watches the page, it never acts on it.
 *
 * ⚠️ **Marks, never ids or labels.** `#firstName` exists in two dialogs and a label is reworded by the next pass;
 * `data-guide="…"` exists for the guide alone. `check:responsive`'s `guide-marks-exist` holds both directions: every
 * mark a guide names is on a control, and every `data-guide` on a control is named by a guide.
 */

/** The five guided steps of « Premiers pas ». */
export type GuideKey = "secretary" | "patients" | "appointment" | "dentalRecord" | "letterhead"

/** When a step is over. The guide checks it on every frame; nothing here needs the page's cooperation. */
export type GuideAdvance =
  /** A control carrying this mark is on screen (a dialog opened, a step of a dialog appeared). */
  | { visible: string }
  /** The address matches (creating a patient opens their page). */
  | { route: RegExp }
  /**
   * The lit control was pressed AND the `scope` mark has gone — a save that closed its dialog. Pressed and still
   * open is a refusal on screen: the step stays. Gone without the press is a cancel: the guide ends.
   */
  | { savedAndClosed: true }
  /** Only the person knows (typing a name): the bubble offers « Suivant ». */
  | { manual: true }

export interface GuideStep {
  /** The `data-guide` value to light. Every visible element carrying it is lit, as one rectangle. */
  mark: string
  /** What to do, in one sentence, for a mouse. */
  text: string
  /** The same for a finger, when the verb differs (« Touchez »). */
  touchText?: string
  next: GuideAdvance
  /**
   * The surface this step lives in (a dialog's mark). If it disappears, the step cannot be done any more: the guide
   * ends — except on a `savedAndClosed` step, where its going is the success.
   */
  scope?: string
}

export interface Guide {
  key: GuideKey
  /** As on « Premiers pas ». */
  title: string
  /** Where the guide begins. Never a deep link that opens a dialog itself: the guide's first step is that click. */
  startHref: string
  /** The pages the guide may be on; leaving them ends it. */
  routes: RegExp
  steps: GuideStep[]
  /** Said when the guide ends because the step it is on can be done no more (« ajoutez d'abord un patient »). */
  missingText?: string
  /** Said once the last step is done. */
  doneText: string
}
