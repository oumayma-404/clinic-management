"use client"

import { useCallback, useEffect, useRef } from "react"

import { cn } from "@/lib/utils"

interface FormErrorBannerProps {
  /** Nothing renders when this is null/blank — callers can pass state straight through. */
  message?: string | null
  /**
   * Optional recovery action. A conflict is the motivating case: the user's input is still on screen and
   * intact, and the only useful next step is « Recharger » to see what the other person wrote.
   */
  action?: { label: string; onClick: () => void; disabled?: boolean }
  className?: string
}

/**
 * The in-form error banner: red, inline, above the actions.
 *
 * Extracted from `payment-modal.tsx`, which was the only modal that had one — everywhere else a failed save
 * produced a toast that vanished after four seconds while the dialog sat there looking unchanged, so the
 * user's next move was to click « Enregistrer » again. A conflict in particular must persist: it is not a
 * transient blip, it means someone else's version is now on the server.
 *
 * Deliberately not a toast and not a nested dialog. It lives inside the form so it scrolls and dismisses
 * with it, and the message stays visible while the user re-reads what they typed.
 *
 * Living inside the form is also what made it invisible: in a tall dialog the banner sits in the scrolling
 * body while the submit button sits in a pinned footer, so an error raised from that footer rendered below
 * the fold and « Créer le rendez-vous » read as doing nothing at all. Hence the scroll-into-view below —
 * placed here rather than in the ~48 call sites, because a banner nobody scrolls to is not a banner.
 */
export function FormErrorBanner({ message, action, className }: FormErrorBannerProps) {
  const ref = useRef<HTMLDivElement>(null)
  const text = message?.trim()

  const reveal = useCallback(() => {
    // `nearest`: no movement when it is already on screen, minimum scroll when it is not.
    const reduced = window.matchMedia?.("(prefers-reduced-motion: reduce)").matches
    ref.current?.scrollIntoView({ block: "nearest", behavior: reduced ? "auto" : "smooth" })
  }, [])

  useEffect(() => {
    if (text) reveal()
  }, [text, reveal])

  /*
   * ⚠️ The effect above is NOT enough, and the reason is easy to re-break: a handler that does
   * `setError(null)` and then `setError(sameMessage)` is auto-batched into no state change at all, so
   * pressing « Créer » twice on the same refusal re-renders nothing and never re-reveals the banner.
   * The form's own submit event is the only honest signal that the user asked again.
   */
  useEffect(() => {
    if (!text) return
    const form = ref.current?.closest("form")
    if (!form) return
    const onSubmit = () => requestAnimationFrame(reveal)
    form.addEventListener("submit", onSubmit)
    return () => form.removeEventListener("submit", onSubmit)
  }, [text, reveal])

  if (!text) return null

  return (
    <div
      ref={ref}
      role="alert"
      aria-live="polite"
      /*
       * On the theme's own destructive family, not on `red-*` literals.
       *
       * `--destructive-wash` was added for exactly this pairing and had three consumers in the whole app, while
       * this primitive — the one every dialog is supposed to route through — hand-wrote `bg-red-50` plus a
       * `dark:` twin. Two consequences, both of which shipped: the banner did not follow the palette (it was the
       * only red in the product that was not `--destructive`), and it maintained dark mode by hand, so every one
       * of the ~18 places that copied this block instead of importing it copied the hand-maintenance too.
       */
      className={cn(
        "space-y-2 rounded-lg border border-destructive/25 bg-destructive-wash p-3 text-sm text-destructive",
        className,
      )}
    >
      <p>{message}</p>
      {action && (
        <button
          type="button"
          onClick={action.onClick}
          disabled={action.disabled}
          className="font-medium underline underline-offset-2 hover:no-underline disabled:opacity-60"
        >
          {action.label}
        </button>
      )}
    </div>
  )
}
