import { expect, type Page } from "@playwright/test"

/**
 * Accepts every confirmation a save raises, in whatever order it raises them, and reports which ones it saw.
 *
 * ⚠️ **A drain loop rather than a fixed sequence, because the set of prompts is a fact about the CALENDAR, not
 * about the scenario.** The booking dialogs have four advisory refusals — slot taken, out of hours, past time,
 * and « ce patient existe déjà » — and which of them fire depends on what else is already in the diary. Measured
 * twice while building this suite:
 *
 * - `BOOK-05` asked only for the duplicate prompt and got a third one, « Créneau déjà occupé », because the
 *   dialog's default time collided with a real appointment. The dialog stayed open and the test reported « the
 *   save was refused » — which was false: the patient had been created and the form said so in as many words.
 * - `EDIT-06` expected a **409** and never reached it, because the same slot prompt was answered first. A
 *   concurrency test that never provokes a conflict passes or fails for reasons that have nothing to do with
 *   concurrency.
 *
 * ⚠️ **The affirmative action differs per prompt** — « Continuer » for past-time / out-of-hours / slot-taken,
 * « Créer quand même » for the duplicate, whose *cancel* is « Choisir un patient existant » rather than
 * « Annuler ». Matching « Annuler » here would silently abandon the save.
 *
 * ⚠️ Confirmations are `role="alertdialog"`, never `role="dialog"` — a selector list holding only the latter
 * reports « the confirmation has no text », which is this repo's own recorded probe failure.
 *
 * Returns the titles confirmed, so a caller can assert the one it cares about actually appeared instead of
 * passing because nothing did.
 */
export async function drainConfirmations(page: Page, max = 5): Promise<string[]> {
  const seen: string[] = []
  for (let i = 0; i < max; i++) {
    const box = page.locator("[role='alertdialog']").first()

    /*
     * ⚠️ **WAIT for the prompt rather than testing whether one is up.** A confirmation appears only after the
     * server has answered, so an `isVisible()` the instant after clicking Enregistrer is a race the harness
     * loses — and losing it looks exactly like a product defect. Measured on four tests at once: the drain
     * found nothing, the assertion ran, and the screenshot showed « Créneau déjà occupé » on screen with the
     * form still open. The first prompt gets a generous window (a cold API answers slowly); later ones are
     * already in flight, so a short one is enough and keeps a promptless save cheap.
     */
    const appeared = await box
      .waitFor({ state: "visible", timeout: i === 0 ? 8_000 : 2_500 })
      .then(() => true)
      .catch(() => false)
    if (!appeared) break

    const title = ((await box.locator("h2, [data-slot='alert-dialog-title']").first().textContent()) ?? "")
      .replace(/\s+/g, " ")
      .trim()
    seen.push(title)

    const affirm = box.locator("button", { hasText: /^(Continuer|Créer quand même)$/ }).first()
    if ((await affirm.count()) === 0) {
      const buttons = await box.locator("button").allTextContents()
      throw new Error(`« ${title} » has no affirmative action; its buttons were: ${buttons.join(" | ")}`)
    }
    await affirm.click()
    await page.waitForTimeout(600)
  }
  return seen
}

/** Asserts a named prompt was among those confirmed — so a scenario cannot pass because nothing was raised. */
export function sawPrompt(seen: string[], pattern: RegExp, what: string) {
  expect(
    seen.some((t) => pattern.test(t)),
    `expected the « ${what} » confirmation to be raised. Confirmed instead: ${seen.join(" · ") || "(none)"}`,
  ).toBeTruthy()
}
