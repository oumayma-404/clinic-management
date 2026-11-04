/**
 * « Me guider » — in-house guides over the real screens (`guided-first-days` F).
 *
 * A guide is a list of steps. Each step lights the controls carrying one `data-guide` mark and says in one sentence
 * what they are for. ⚠️ **Every guide is a TOUR** (owner's call, 2026-11-04): the person only watches and presses
 * « Suivant » — no field to fill, no button to press, nothing ever saved. Where the next step lives in a window, the
 * step's `press` opens it (a « Nouveau », a patient's row — never a save). « Terminer » ticks the « Premiers pas »
 * step by hand and goes back to the dashboard, where the next step is highlighted.
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
  /**
   * On « Suivant », the guide presses something to open what the next step shows: `true` = the lit control, a
   * selector = the first drawn match inside it (a patient's row). ⚠️ Only ever what OPENS a window or a page — a
   * « Nouveau », a row — never a save, a send or a delete.
   */
  press?: true | string
  /**
   * When `mark` is absent (an empty list), light this mark instead, say `emptyText`, and « Terminer » ends the
   * tour — counted as watched. Waits a moment first, so a list still loading is not read as empty.
   */
  emptyMark?: string
  emptyText?: string
}

export interface Guide {
  /** One of the five tours, or `nextStep` — the dashboard prompt between two of them (`next-step.ts`). */
  key: GuideKey | "nextStep"
  /** As on « Premiers pas ». */
  title: string
  /** Where the guide begins. Never a deep link that opens a dialog itself: the guide's first step is that click. */
  startHref: string
  /** The pages the guide may be on; leaving them ends it. */
  routes: RegExp
  steps: GuideStep[]
  /** Said when the guide ends because the step it is on can be done no more (« ajoutez d'abord un patient »). */
  missingText?: string
  /** Said, with the confetti, once the last step is done. */
  doneText: string
  /** The forward button on the last step, when it is not « Terminer » (« C'est parti »). */
  continueLabel?: string
  /** The way out, when it is not « Quitter le guide » (« Plus tard »). */
  quitLabel?: string
}
