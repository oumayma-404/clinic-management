import type { Page } from "@playwright/test"
import type { ClinicApi } from "../lib/api"
import { expect, mutatingOnly, test } from "../lib/fixtures"
import { gotoApp } from "../lib/goto"

/**
 * HP-18 — « Messagerie interne du cabinet ».
 *
 * ⚠️ **One signed-in account, so the colleagues never sign in.** They are created over `POST /users` and only
 * ever *receive*: a second browser context would replay the shared session's refresh cookie and end it
 * (`lib/fixtures.ts`). Who rings, who claims and « Vu par » need real separate sessions and are held by the
 * domain tests and the `/test-in-browser` pass instead.
 *
 * ⚠️ **Every colleague is stamped and never deleted** — a fixture account, like a fixture patient, is evidence
 * for a red run. Deactivated ones accumulate on purpose.
 */

mutatingOnly()

// Each test creates an account, a patient and a visit before it opens a screen.
test.setTimeout(150_000)

const stamp = () => `${Date.now().toString(36)}${Math.floor(Math.random() * 1e4).toString(36)}`

/** A colleague who never signs in. A dentist gets a `Doctor` row linked to the account. */
async function colleague(api: ClinicApi, role: "doctor" | "secretary") {
  const s = stamp()
  const lastName = role === "doctor" ? `Dentiste-${s}` : `Secrétaire-${s}`
  const created = await api.ok<{ userId: string; fullName: string }>("POST", "/users", {
    email: `e2e.chat.${s}@example.com`,
    fullName: `E2E ${lastName}`,
    role,
    ...(role === "doctor" ? { doctorInfo: { firstName: "E2E", lastName, specialty: "Dentist" } } : {}),
  })
  let doctorId: string | null = null
  if (role === "doctor") {
    const status = await api.ok<{ doctors?: { id: string; userId?: string }[] }>("GET", "/clinics/user-status")
    doctorId = status.doctors?.find((d) => d.userId === created.userId)?.id ?? null
    if (!doctorId) throw new Error(`arrange: no Doctor row linked to the new dentist ${created.userId}`)
  }
  return { userId: created.userId, name: created.fullName, lastName, doctorId }
}

/** The caller's conversation with one colleague, read from the server's own list. */
async function directWith(api: ClinicApi, userId: string) {
  const list = await api.ok<any[]>("GET", "/staff-chat/conversations")
  const hit = list.find((c) => c.kind === "direct" && c.members.some((m: any) => m.userId === userId))
  if (!hit) throw new Error(`no direct conversation with ${userId} in the caller's list`)
  return hit
}

/** A visit today at noon for that dentist — noon so a run near midnight in either zone stays « today ». */
async function visitToday(api: ClinicApi, arrange: any, patientId: string, doctorId: string) {
  const act = await api.singleSeanceAct()
  return arrange.booking(patientId, act.id, {
    doctorId,
    appointmentDateTime: arrange.slot(0, 12).toISOString(),
  })
}

const panel = (page: Page) => page.locator("aside[aria-labelledby]")
const launcher = (page: Page) => page.getByRole("button", { name: /^Messages/ }).locator("visible=true").first()

async function openConversationWith(page: Page, name: string) {
  await launcher(page).click()
  await expect(panel(page)).toBeVisible()
  // The panel reopens on the conversation it was left on; go back to the list first when it did.
  const back = panel(page).getByRole("button", { name: "Retour aux conversations" })
  if (await back.count()) await back.first().click()
  await panel(page).getByRole("button", { name: new RegExp(name) }).first().click()
  // An empty thread has no message log yet; its heading and composer are what say it opened.
  await expect(panel(page).getByRole("heading", { name: new RegExp(name) })).toBeVisible()
  await expect(panel(page).getByPlaceholder("Écrire un message")).toBeVisible()
}

/** Opens the edit dialog of one visit and its « ⋯ » menu. */
async function openVisitMenu(page: Page, appointmentId: string) {
  await gotoApp(page, `/appointments?appointmentId=${appointmentId}`)
  const dialog = page.getByRole("dialog").filter({ hasText: /Rendez-vous/ }).first()
  await expect(dialog).toBeVisible({ timeout: 20_000 })
  await dialog.getByRole("button", { name: "Autres actions sur le rendez-vous" }).click()
  await expect(page.getByRole("menu")).toBeVisible()
}

test("CHAT-01 · a message to the secretary shows in the thread, and the sender's own badge does not move", async ({
  page,
  api,
}) => {
  const secretary = await colleague(api, "secretary")
  await gotoApp(page, "/appointments")
  const before = await launcher(page).getAttribute("aria-label")

  await openConversationWith(page, secretary.name)
  const text = `E2E bonjour ${stamp()}`
  await panel(page).getByPlaceholder("Écrire un message").fill(text)
  await panel(page).getByRole("button", { name: "Envoyer" }).click()

  await expect(panel(page).locator("ol[role=log]")).toContainText(text)
  // The bubble shows before the server answers; the stored message is what this asserts.
  await expect.poll(async () => (await directWith(api, secretary.userId)).lastMessage?.body).toBe(text)
  expect(await launcher(page).getAttribute("aria-label")).toBe(before)
})

test("CHAT-02 · « Signaler l'arrivée » from the visit's « ⋯ » lands in the dentist conversation with the RDV link", async ({
  page,
  api,
  arrange,
  patient,
}) => {
  const dentist = await colleague(api, "doctor")
  const p = await patient("Chat-arrivee")
  const visit = await visitToday(api, arrange, p.id, dentist.doctorId!)

  await openVisitMenu(page, visit.id)
  await page.getByRole("menuitem", { name: /^Signaler l'arrivée$/ }).click()
  await expect(page.getByText(/Arrivée signalée au Dr /).first()).toBeVisible()

  const conversation = await directWith(api, dentist.userId)
  expect(conversation.lastMessage?.quickMessage?.key).toBe("patient-arrived")
  expect(conversation.lastMessage?.appointment?.id).toBe(visit.id)
  expect(conversation.lastMessage?.patient?.id).toBe(p.id)

  // The menu now says who announced it and when — and a second press asks first.
  await page.keyboard.press("Escape")
  await openVisitMenu(page, visit.id)
  const announced = page.getByRole("menuitem", { name: /^Signalé à \d\d:\d\d par / })
  await expect(announced).toBeVisible()
  await announced.click()
  const confirm = page.getByRole("alertdialog")
  await expect(confirm).toContainText(/Signaler à nouveau l'arrivée/)
  await confirm.getByRole("button", { name: "Non" }).click()
  await expect(confirm).toBeHidden()

  // The thread shows the patient and the RDV as links.
  await gotoApp(page, "/appointments")
  await openConversationWith(page, dentist.lastName)
  const log = panel(page).locator("ol[role=log]")
  await expect(log.getByRole("link", { name: new RegExp(p.name) }).last()).toBeVisible()
  await expect(log.getByRole("link", { name: /^RDV \d\d:\d\d/ }).last()).toHaveAttribute(
    "href",
    `/appointments?appointmentId=${visit.id}`,
  )
})

test("CHAT-03 · an arrival already sent is refused with who sent it, unless resent on purpose", async ({
  api,
  arrange,
  patient,
}) => {
  const dentist = await colleague(api, "doctor")
  const p = await patient("Chat-renvoi")
  const visit = await visitToday(api, arrange, p.id, dentist.doctorId!)

  const first = await api.post("/staff-chat/arrivals", { appointmentId: visit.id, resend: false })
  expect(first.status, first.raw).toBe(201)
  expect(first.body.conversation).toBe((await directWith(api, dentist.userId)).key)

  const again = await api.post("/staff-chat/arrivals", { appointmentId: visit.id, resend: false })
  expect(again.status, again.raw).toBe(409)
  expect(again.body.code).toBe("staffchat-arrival-already-sent")
  expect(again.body.error).toMatch(/^Signalé à \d\d:\d\d par /)

  const resent = await api.post("/staff-chat/arrivals", { appointmentId: visit.id, resend: true })
  expect(resent.status, resent.raw).toBe(201)
})

test("CHAT-04 · a quick message sent more than the others moves first in the bar, never while the thread is open", async ({
  page,
  api,
}) => {
  const secretary = await colleague(api, "secretary")
  await gotoApp(page, "/appointments")
  await openConversationWith(page, secretary.name)

  const bar = panel(page).getByRole("list", { name: "Messages rapides" })
  const labels = async () => (await bar.getByRole("button").allInnerTexts()).map((t) => t.replace(/\s+/g, " ").trim())
  const catalogue = await api.ok<{ items: any[] }>("GET", "/staff-chat/quick-messages")
  const desk = catalogue.items.filter((i) => i.for === "desk")
  const shown = await labels()
  const candidates = desk.filter((i) => !i.asksForPatient && !i.urgent && shown.includes(i.label))
  test.skip(candidates.length < 2, "the cabinet offers fewer than two plain « secrétariat » quick messages")
  const target = candidates[candidates.length - 1]
  const orderBefore = await labels()

  // Send it until the server ranks it first among what this bar offers — three times on a fresh account.
  for (let sent = 0; sent < 15; sent++) {
    await bar.getByRole("button", { name: target.label, exact: true }).click()
    const ranked = (await api.ok<{ items: any[] }>("GET", "/staff-chat/quick-messages")).items
      .filter((i) => i.for === "desk" && shown.includes(i.label))
    if (ranked[0]?.key === target.key) break
  }

  // Open: nothing moved under the finger.
  expect(await labels()).toEqual(orderBefore)

  // Reopened: it leads.
  await panel(page).getByRole("button", { name: "Fermer les messages" }).click()
  await launcher(page).click()
  await expect(bar).toBeVisible()
  expect((await labels())[0]).toBe(target.label)
})

test("CHAT-05 · a visit whose dentist cannot receive the arrival keeps the action, disabled, with the reason", async ({
  page,
  api,
  arrange,
  patient,
}) => {
  const dentist = await colleague(api, "doctor")
  const p = await patient("Chat-inactif")
  const visit = await visitToday(api, arrange, p.id, dentist.doctorId!)
  await api.ok("PUT", `/users/${encodeURIComponent(dentist.userId)}/status`, { isActive: false })

  await openVisitMenu(page, visit.id)
  const item = page.getByRole("menuitem", { name: /^Signaler l'arrivée/ })
  await expect(item).toBeVisible()
  await expect(item).toHaveAttribute("data-disabled", "")
  const text = (await item.innerText()).replace(/\s+/g, " ").trim()
  expect(text.length, `the row states why: « ${text} »`).toBeGreaterThan("Signaler l'arrivée".length + 10)
})

test("CHAT-08 · a deactivated colleague's conversation refuses a new message by code", async ({ api }) => {
  const secretary = await colleague(api, "secretary")
  const conversation = await directWith(api, secretary.userId)
  await api.ok("PUT", `/users/${encodeURIComponent(secretary.userId)}/status`, { isActive: false })

  const r = await api.post("/staff-chat/messages", {
    conversation: conversation.key,
    body: "E2E encore là ?",
    quickMessageKey: null,
    patientId: null,
    appointmentId: null,
  })
  expect(r.status, r.raw).toBeGreaterThanOrEqual(400)
  expect(r.body.code).toBe("staffchat-recipient-inactive")
})
