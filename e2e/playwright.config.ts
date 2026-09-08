import { defineConfig, devices } from "@playwright/test"

/**
 * ⚠️ **`channel: "chrome"` on a developer's machine, the bundled Chromium on CI.**
 *
 * The real Chrome is what a Tunisian practice actually uses, and running the local pass in it is the point.
 * But a GitHub runner has no Chrome unless `playwright install chrome` is asked for it, and naming a channel
 * that is absent fails the whole run with « Chromium distribution 'chrome' is not found » — a message that
 * looks nothing like « we installed the wrong browser ». `playwright install chromium` is what the CI job
 * runs, so CI gets the bundled build.
 */
const CHANNEL = process.env.CI ? undefined : (process.env.CLINIC_E2E_BROWSER_CHANNEL ?? "chrome") || undefined

/**
 * The hot-path end-to-end gate.
 *
 * ⚠️ **`workers: 1` and `fullyParallel: false` are load-bearing, not caution.** Half of what this suite asserts
 * is a *clinic-wide* money read — la caisse's day total, « Créances », the dashboard's counts — so two tests
 * billing a fiche at the same time make each other's expected figure wrong. The tests are ordinary and the
 * arithmetic is shared; running them in parallel would produce exactly the confident false finding
 * `.claude/rules/verification.md` § 2 is about.
 *
 * ⚠️ **`retries: 0` locally, deliberately.** A retry that turns a real intermittent defect green is the one
 * outcome this suite must not be able to produce. On CI it retries once — there, a flake is more likely to be
 * the cold-started API than the product (§ 6), and a first-attempt failure is still reported in the JSON.
 */
export default defineConfig({
  testDir: "./specs",
  outputDir: "./.artifacts",

  fullyParallel: false,
  workers: 1,
  retries: process.env.CI ? 1 : 0,

  // No `forbidOnly` guard is needed: `.only` cannot silently shrink the gate here because the reporter prints
  // the executed count and CI compares it (see `npm run test:t0`).
  timeout: 60_000,
  expect: { timeout: 10_000 },

  reporter: [
    ["list"],
    ["json", { outputFile: ".artifacts/results.json" }],
    ["html", { outputFolder: ".artifacts/report", open: "never" }],
  ],

  globalSetup: "./lib/global-setup.ts",

  use: {
    baseURL: process.env.CLINIC_ORIGIN ?? "http://localhost:3000",

    /*
     * ⚠️ **`storageState` is deliberately NOT set here.** It belongs to the worker-scoped `_ctx` fixture in
     * `lib/fixtures.ts` and to nothing else, because the BFF's refresh cookie **rotates on every
     * `/bff/auth/token`** — which the app calls on each navigation — and the server forgives exactly **one**
     * superseded generation (`SessionFamily.PreviousCredentialHash`). A second holder of the file's original
     * cookie therefore replays a credential several generations old, reuse detection ends the family
     * («&#160;Un identifiant de session déjà remplacé a été présenté.&#160») and the run degrades to `/login`
     * **partway through** — green early, « element not found » late. Measured exactly that way twice.
     *
     * One page at a time survives; two concurrent tabs survive (that is what the grace generation is for).
     * Two holders one of which never rotates do not.
     */

    // A trace on the first failure is what makes a red result readable without a re-run (§ 3).
    trace: "retain-on-failure",
    screenshot: "only-on-failure",
    video: "off",

    // The device this app is used on most is a tablet, but the hot paths are authored on a desk; the device
    // contract is checked explicitly in `device.spec.ts` at the five widths rather than by running everything
    // five times.
    ...devices["Desktop Chrome"],
    channel: CHANNEL,
    locale: "fr-FR",
    timezoneId: "Africa/Tunis",
  },

  projects: [
    { name: "hot-paths", testIgnore: /device\.spec\.ts/ },
    {
      name: "device",
      testMatch: /device\.spec\.ts/,
      use: { ...devices["Desktop Chrome"], channel: CHANNEL },
    },
  ],
})
