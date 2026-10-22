import type { BrowserContext, Page } from "@playwright/test"
import { expect, mutatingOnly, test } from "../lib/fixtures"
import { gotoApp } from "../lib/goto"
import { ORIGIN } from "../lib/env"

/**
 * HP-19 — a form the Windows app carries across a page load (`clinic-pc-copy` D23).
 *
 * ⚠️ **Browser-only by construction.** The carried draft is page state handed to the shell and taken back by the next
 * page load; the server never sees it. Here the shell's slot is emulated per tab with desktop `CarriedDraftSlot`'s
 * semantics — `carryDraft` replaces, `takeCarriedDraft` hands over once — on a page of its own, so the fake bridge
 * cannot leak into the worker's shared page.
 *
 * ⚠️ CARRY-01 is the defect of 2026-10-18: after ONE full load with a form carried, every later navigation went back to
 * that form's page — the patient search, « Remplir la fiche », the rail — for the draft's whole two hours.
 */
async function pageWithShellSlot(context: BrowserContext): Promise<Page> {
  const page = await context.newPage()
  await page.addInitScript(() => {
    const w = window as unknown as { __clinicShell?: Record<string, unknown> }
    w.__clinicShell = {
      carryDraft: (json: string | null) => {
        if (json) sessionStorage.setItem("__carried", json)
        else sessionStorage.removeItem("__carried")
      },
      takeCarriedDraft: () => {
        const json = sessionStorage.getItem("__carried")
        sessionStorage.removeItem("__carried")
        return Promise.resolve(json)
      },
    }
  })
  return page
}

/** Types into the patient form so it carries itself (`useCarriedDraft` pushes 400 ms after a change). */
async function carryPatientForm(page: Page, text: string) {
  await page.getByRole("button", { name: "Modifier le patient" }).first().click()
  const field = page.locator("#consultationReason")
  await expect(field).toBeVisible()
  await field.fill(text)
  await expect
    .poll(() => page.evaluate(() => sessionStorage.getItem("__carried")), { message: "the form never reached the shell" })
    .toContain(text)
}

/** Follows a rail link and asserts the page STAYS there — a resume redirect is a full load a moment later. */
async function railTo(page: Page, href: string) {
  await page.locator(`a[href="${href}"]`).locator("visible=true").first().click()
  await page.waitForLoadState("networkidle")
  await page.waitForTimeout(1500)
  expect(new URL(page.url()).pathname, `a click on ${href} was sent elsewhere`).toBe(href)
}

test.describe("HP-19 · un formulaire porté d'une page à l'autre @mutating", () => {
  mutatingOnly()

  test("CARRY-01 · after one reload with a form carried, every navigation lands where it was sent", async ({
    sharedContext,
    patient,
  }) => {
    const p = await patient("CARRY")
    const page = await pageWithShellSlot(sharedContext)
    try {
      await gotoApp(page, `/patients/${p.id}`, sharedContext)
      await carryPatientForm(page, "porté-01")

      // The switch to the PC de secours, F5, the shell reopening the page: one full load while the form is carried.
      await page.reload({ waitUntil: "networkidle" })
      expect(new URL(page.url()).pathname).toBe(`/patients/${p.id}`)

      await railTo(page, "/a-cloturer")
      await railTo(page, "/patients")
      await railTo(page, "/appointments")
    } finally {
      await page.close()
    }
  })

  test("CARRY-02 · the resume itself still happens — once — and the form comes back with its typing", async ({
    sharedContext,
    patient,
  }) => {
    const p = await patient("CARRY")
    const page = await pageWithShellSlot(sharedContext)
    try {
      await gotoApp(page, `/patients/${p.id}`, sharedContext)
      await carryPatientForm(page, "porté-02")

      // The shell opens the other server on its home page; the draft belongs to another screen, so it goes there.
      await page.goto(`${ORIGIN}/`, { waitUntil: "networkidle" })
      await expect.poll(() => new URL(page.url()).pathname, { timeout: 20_000 }).toBe(`/patients/${p.id}`)
      await expect(page.locator("#consultationReason")).toHaveValue("porté-02")

      // Leave the form without saving; the next click is honoured.
      await page.keyboard.press("Escape")
      const discard = page.getByRole("alertdialog").getByRole("button", { name: "Abandonner" })
      if (await discard.isVisible().catch(() => false)) await discard.click()
      await expect(page.getByRole("dialog")).toBeHidden()
      await railTo(page, "/patients")
    } finally {
      await page.close()
    }
  })
})
