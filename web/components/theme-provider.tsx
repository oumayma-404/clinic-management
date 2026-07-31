"use client"

import { ThemeProvider as NextThemesProvider } from "next-themes"
import type { ReactNode } from "react"

/**
 * Light / dark / système (AC-38).
 *
 * A thin `"use client"` wrapper because `app/layout.tsx` is a server component and next-themes' provider is
 * not. It is mounted **outermost inside `<body>`**, above the session provider, so the theme class is on
 * `<html>` before anything reads a colour.
 *
 * ⚠️ **`attribute="class"` is not optional here, and getting it wrong fails silently.** next-themes defaults
 * to `attribute="data-theme"`, but `globals.css` declares `@custom-variant dark (&:is(.dark *):not(…))` — a
 * **class**-based variant. With the default, the library would faithfully write `data-theme="dark"` on
 * `<html>`, the toggle would appear to work, `resolvedTheme` would read `"dark"` … and **none of the 336
 * `dark:` utilities in the app would apply**. Nothing errors; the page simply stays light.
 *
 * ⚠️ `disableTransitionOnChange` because `globals.css` puts a transition on `*` — without it, switching theme
 * cross-fades every element on the page at once, which reads as a rendering fault rather than a setting.
 *
 * `suppressHydrationWarning` is already on `<html>` (P1 added it for exactly this): the pre-hydration script
 * sets the class before React runs, so server and client markup necessarily differ on that attribute.
 */
export function ThemeProvider({ children }: { children: ReactNode }) {
  return (
    <NextThemesProvider
      attribute="class"
      defaultTheme="system"
      enableSystem
      disableTransitionOnChange
    >
      {children}
    </NextThemesProvider>
  )
}
