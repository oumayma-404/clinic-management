"use client"

import { Toaster } from "sonner"
import { useTheme } from "next-themes"
import { COARSE_POINTER_QUERY, useMediaQuery } from "@/lib/hooks/use-media-query"

/**
 * The app's toast host (AC-9).
 *
 * A thin client wrapper exists because `position` and `visibleToasts` are *props*, not CSS — a media query in a
 * class name cannot move where sonner anchors its stack. sonner's own `mobileOffset` is keyed on a hardcoded
 * 600px viewport width, which is the wrong question: a 1180px iPad held at the chair is a touch device and a
 * 600px desktop window is not.
 *
 * What changes on a coarse pointer, and why:
 *
 *   position       top-right → bottom-center. At 390px a toast is effectively full-width, and anchored at the
 *                  top it lands on the header — over the notification bell, and over the title and close
 *                  control of any open sheet. The bottom is also where the thumb already is.
 *   offset         cleared above the bottom bar via `--bottom-inset`, so a toast never covers navigation.
 *   visibleToasts  5 → 3. Five expanded French sentences at 8 s each (the duration `showErrorToast` uses for
 *                  errors) is most of a phone screen, and the error toast is the only place the reason for a
 *                  failure exists — burying it under four others defeats the point.
 *
 * Desktop keeps exactly what it had: top-right, five toasts, no offset.
 */
export function AppToaster() {
  const isCoarse = useMediaQuery(COARSE_POINTER_QUERY)
  const { resolvedTheme } = useTheme()

  return (
    <Toaster
      /*
       * sonner renders in its own portal with its own surface colours, so it does NOT pick the theme up from
       * the `.dark` class the way the rest of the app does — left alone it stays light-on-light and the
       * `richColors` variants lose their contrast. `resolvedTheme` rather than `theme`, because sonner needs
       * a concrete light/dark and « système » is neither.
       */
      theme={resolvedTheme === "dark" ? "dark" : "light"}
      position={isCoarse ? "bottom-center" : "top-right"}
      offset={isCoarse ? { bottom: "calc(1rem + var(--bottom-inset))" } : undefined}
      visibleToasts={isCoarse ? 3 : 5}
      richColors
      closeButton
      /*
       * 4 s, not 3. Several of this app's toasts are full French sentences with a `description` under them
       * (« La connexion a été interrompue. Réessayez une fois la connexion rétablie. »), and 3 s is not
       * enough time to read one — the message is gone before it has been understood, which is the same as
       * not having shown it.
       *
       * Error toasts get their own, longer life at the call site (`showErrorToast` in `lib/errors.ts`), which
       * is the only place sonner lets a duration vary by kind: a success toast is a confirmation the user can
       * afford to miss (the row already changed on screen), while an error toast is the *only* place the
       * reason exists. `closeButton` means the longer duration never traps anyone.
       */
      duration={4000}
      expand
    />
  )
}
