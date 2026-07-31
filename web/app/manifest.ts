import type { MetadataRoute } from "next"
import { PRODUCT_NAME } from "@/lib/brand"

/**
 * The web-app manifest, so the clinic can install the app to a home screen (AC-36).
 *
 * ⚠️ **`icons` is deliberately empty, and that is not an oversight.** `layout.tsx` declares four icon files
 * (`/icon-light-32x32.png`, `/icon-dark-32x32.png`, `/icon.svg`, `/apple-icon.png`) and **none of them
 * exists** — `public/` still holds only the untouched `create-next-app` SVGs. Listing them here would make
 * the manifest *look* complete while every entry 404s, and an installed app with a broken icon is worse than
 * an uninstallable one: the home screen shows a blank or generic tile that nobody can explain.
 *
 * A manifest with no `icons` is valid; browsers fall back to a screenshot or the favicon. So install works,
 * the name and colours are right, and AC-36's icon clause stays honestly open until real assets exist. See
 * the P7 note in `stories/progress.md`.
 *
 * `display: "standalone"` is what removes the browser chrome — including its **back button**, which is why
 * AC-37's in-app back affordance is part of the same AC and not a nicety.
 *
 * `theme-color` matches the light `--background` (oklch(0.995 0.002 225) ≈ #fdfdfe) rather than the accent:
 * it tints the status bar, which sits directly above the app's own ground, and a teal bar over a near-white
 * header reads as a rendering seam.
 */
export default function manifest(): MetadataRoute.Manifest {
  return {
    name: PRODUCT_NAME,
    short_name: PRODUCT_NAME,
    description: "Gestion de cabinet dentaire — patients, rendez-vous, facturation.",
    start_url: "/",
    display: "standalone",
    orientation: "any",
    background_color: "#fdfdfe",
    theme_color: "#fdfdfe",
    lang: "fr",
    dir: "ltr",
    icons: [],
  }
}
