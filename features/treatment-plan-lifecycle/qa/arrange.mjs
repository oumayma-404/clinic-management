// Mints this walk's own test data on the wire, so nothing in the shared 1366-patient dataset is written
// (`shared-stack.md` § 5). Every plan the browser rows touch is created here and named `QA-PHASE6-<runid>`.
import { bearer } from "./auth.mjs"

export const RUN = process.env.QA_RUN_ID ?? `p6${Date.now().toString(36).slice(-6)}`

export function makeArranger(token) {
  const call = bearer(token)

  const must = async (method, path, body, what) => {
    const r = await call(method, path, body)
    if (!r.ok) throw new Error(`${what} failed: HTTP ${r.status} ${r.text.slice(0, 400)}`)
    return r.json?.value ?? r.json
  }

  return {
    call,
    must,

    async procedureTypes() {
      const r = await must("GET", "/procedure-types?pageNumber=1&pageSize=200", undefined, "read procedure types")
      return r.items ?? r
    },

    async patient(suffix) {
      return must(
        "POST",
        "/patients",
        {
          firstName: "QA-PHASE6",
          lastName: `${RUN}-${suffix}`,
          dateOfBirth: "1990-05-05",
          gender: "Female",
          phone: "20 123 456",
        },
        `create patient ${suffix}`,
      )
    },

    async plan({ patientId, title, items }) {
      return must("POST", "/treatment-plans", { patientId, title, notes: `QA ${RUN}`, items }, `create plan ${title}`)
    },

    async get(id) {
      return must("GET", `/treatment-plans/${id}`, undefined, `read plan ${id}`)
    },
  }
}
