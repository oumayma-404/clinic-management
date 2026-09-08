import { existsSync, readFileSync } from "node:fs"
import { join } from "node:path"

/**
 * Where the suite points, and who it signs in as.
 *
 * ⚠️ **No credential is hard-coded here, and that is deliberate rather than tidy.** This repository's own
 * convention is explicit — `appsettings.json` carries « SECRET — do NOT commit a value here » where a
 * connection string would go — and a password plus a TOTP secret in a git history is permanent, survives a
 * change of repository visibility, and belongs to an **admin** account on a database holding real-shaped
 * patient data. So they are read, in order, from:
 *
 *   1. the environment (`CLINIC_PASSWORD`, `CLINIC_TOTP_SECRET`, …) — what CI supplies;
 *   2. `e2e/credentials.local.json`, which `.gitignore` excludes — what a developer keeps on their machine.
 *
 * With neither present the suite **fails at setup with instructions**, rather than signing in as nobody and
 * reporting a green run. See `e2e/README.md` § « Running it locally ».
 */

type LocalCredentials = {
  origin?: string
  api?: string
  email?: string
  password?: string
  totpSecret?: string
  clinicId?: string
}

/**
 * ⚠️ Read once, at import. A file that appears mid-run would otherwise change the account between specs, which
 * is the kind of thing that takes an afternoon to notice.
 */
const local: LocalCredentials = (() => {
  const path = join(__dirname, "..", "credentials.local.json")
  if (!existsSync(path)) return {}
  try {
    return JSON.parse(readFileSync(path, "utf8")) as LocalCredentials
  } catch (e) {
    throw new Error(
      `e2e/credentials.local.json is present but unreadable (${(e as Error).message}). ` +
        "Fix it or delete it — a half-parsed credentials file is worse than none.",
    )
  }
})()

export const ORIGIN = process.env.CLINIC_ORIGIN ?? local.origin ?? "http://localhost:3000"
export const API = process.env.CLINIC_API ?? local.api ?? "http://localhost:5000/api"

/**
 * The clinic the account resolves to. Never sent — clinic membership is resolved server-side from the token —
 * but the suite prints it so a run against the wrong database is obvious in the first line of the log rather
 * than in a confusing assertion twenty tests later.
 */
export const CLINIC_ID = process.env.CLINIC_ID ?? local.clinicId ?? "(unknown)"

/**
 * Whether the run may write. `false` is the deploy smoke: reads only, so the suite can be pointed at a live
 * hosted backend without touching a practice's ledger. Mutating specs are tagged `@mutating` and skipped.
 */
export const MAY_MUTATE = process.env.CLINIC_E2E_READONLY !== "1"

const HOW_TO = `
Supply the QA account one of two ways.

  1 · environment (what CI does)
        CLINIC_EMAIL=…  CLINIC_PASSWORD=…  CLINIC_TOTP_SECRET=…  npx playwright test

  2 · a local file, git-ignored — cp e2e/credentials.local.example.json e2e/credentials.local.json
        { "email": "…", "password": "…", "totpSecret": "…", "clinicId": "…" }

On a developer machine the values are the throwaway dev admin recorded in
~/.claude/skills/clinic-browser/SKILL.md. They are NOT in this repository, on purpose.
`

function required(name: string, envVar: string, value: string | undefined): string {
  if (value && value.trim()) return value.trim()
  throw new Error(`the suite needs ${name} and found none (${envVar} unset, no credentials.local.json).${HOW_TO}`)
}

export const ACCOUNT = {
  get email() {
    return required("an account e-mail", "CLINIC_EMAIL", process.env.CLINIC_EMAIL ?? local.email)
  },
  get password() {
    return required("the account password", "CLINIC_PASSWORD", process.env.CLINIC_PASSWORD ?? local.password)
  },
  get totpSecret() {
    // ⚠️ Whitespace-stripped: the API issues the secret grouped in fours for a human to type, and a value
    // pasted in that form into an env var fails as an arithmetically-correct-looking wrong code.
    return required(
      "the account's TOTP secret",
      "CLINIC_TOTP_SECRET",
      (process.env.CLINIC_TOTP_SECRET ?? local.totpSecret)?.replace(/\s/g, ""),
    ).toUpperCase()
  },
}
