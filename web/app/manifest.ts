import type { MetadataRoute } from "next"
import { PRODUCT_NAME } from "@/lib/brand"

/**
 * The web-app manifest, so the clinic can install the app to a home screen (AC-36; AC-1/AC-2 of
 * `mobile-native-shells`).
 *
 * ⚠️ **`icons` was deliberately empty until this part, and the reason has now expired.** All seven declared assets
 * exist — generated from `branding/icon.svg` by `scripts/generate-icons.mjs` — so listing them no longer makes the
 * manifest *look* complete while every entry 404s.
 *
 * The trio here is the minimum that produces a correct tile, and each entry earns its place:
 *   192 / 512 `purpose: "any"`   the platform does NOT mask these, so they carry their own rounded plate.
 *   512 `purpose: "maskable"`    Android masks it to a circle/squircle/teardrop, so it is full-bleed with the mark
 *                                inside the 80 % safe circle. Without a maskable entry Android crops the "any"
 *                                icon and the plate's corners are shaved off.
 * ⚠️ Two separate entries, never `purpose: "any maskable"` on one file: a maskable image is *designed* padded, so
 * declaring it for "any" too renders it small and lost inside its own margin on every platform that does not mask.
 *
 * `display: "standalone"` is what removes the browser chrome — including its **back button**, which is why AC-37's
 * in-app back affordance is part of the same AC and not a nicety.
 *
 * `theme_color` matches the light `--background` rather than the accent: it tints the status bar, which sits
 * directly above the app's own ground, and a teal bar over a near-white header reads as a rendering seam.
 * ⚠️ The value tracks `app/globals.css`'s `--background` and must be re-derived when that token moves — it was
 * `#fdfdfe` from an older `oklch(0.995 0.002 225)` and had been left behind by the « menthe clinique » palette,
 * which is `oklch(0.975 0.008 215)` ≈ `#f1f8fa`. A stale value here is a visible seam, not a rounding error.
 */
export default function manifest(): MetadataRoute.Manifest {
  return {
    // A stable identity, so a browser treats this as the same installed app even if `start_url` ever moves.
    id: "/",
    name: PRODUCT_NAME,
    short_name: PRODUCT_NAME,
    description: "Gestion de cabinet dentaire — patients, rendez-vous, facturation.",
    start_url: "/",
    display: "standalone",
    orientation: "any",
    background_color: "#f1f8fa",
    theme_color: "#f1f8fa",
    lang: "fr",
    dir: "ltr",
    icons: [
      { src: "/icon-192.png", sizes: "192x192", type: "image/png", purpose: "any" },
      { src: "/icon-512.png", sizes: "512x512", type: "image/png", purpose: "any" },
      { src: "/icon-maskable-512.png", sizes: "512x512", type: "image/png", purpose: "maskable" },
    ],
  }
}
