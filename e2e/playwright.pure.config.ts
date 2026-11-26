import { defineConfig } from "@playwright/test"

/**
 * The PURE specs — functions tested with no page and no API (`photo-match.spec.ts`, the camera import's matcher and
 * EXIF reader; `token-renewal.spec.ts`, the session cookie never presented twice at once). The main config signs in
 * during its global setup, which needs the whole stack running; these need
 * nothing, so they get a config of their own:
 *
 *   npx playwright test --config=playwright.pure.config.ts
 *
 * They still run in CI's `e2e` job with everything else, under the main config.
 */
export default defineConfig({
  testDir: "./specs",
  testMatch: /(photo-match|token-renewal)\.spec\.ts$/,
  reporter: "list",
})
