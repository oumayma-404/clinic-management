"use client"

import * as React from "react"
import { Eye, EyeOff } from "lucide-react"

import { Input } from "@/components/ui/input"
import { cn } from "@/lib/utils"

/**
 * A password field with a show/hide toggle — the ONLY way this app renders `type="password"`.
 *
 * <p>Typing a password blind is where « mot de passe incorrect » comes from on a phone keyboard, and this
 * product asks for one at the front door, at the first sign-in, at every step-up and on three secret fields in
 * the settings. `check:responsive`'s `password-has-a-reveal` fails on a raw `type="password"` anywhere else, so
 * the toggle cannot be wired to one screen and forgotten on the next six.</p>
 *
 * <p>⚠️ The toggle is a real `<button type="button">`: inside a `<form>` a bare `<button>` submits, which on the
 * login screen would spend a sign-in attempt against the 5-per-15-minutes lockout just for peeking.</p>
 *
 * <p>Painted at 28 px so it fits the `h-8` secret fields in `reminder-settings`, with `touch-target` supplying
 * the 44 px hit area on a finger — the same split `ui/dialog.tsx`'s close button uses.</p>
 */
function PasswordInput({ className, disabled, ...props }: Omit<React.ComponentProps<"input">, "type">) {
  const [revealed, setRevealed] = React.useState(false)

  return (
    <div className="relative">
      <Input
        {...props}
        type={revealed ? "text" : "password"}
        disabled={disabled}
        className={cn("pe-9", className)}
      />
      <button
        type="button"
        onClick={() => setRevealed((v) => !v)}
        disabled={disabled}
        aria-label={revealed ? "Masquer le mot de passe" : "Afficher le mot de passe"}
        aria-pressed={revealed}
        className={cn(
          "touch-target absolute end-1 top-1/2 flex size-7 -translate-y-1/2 items-center justify-center",
          "rounded-md text-muted-foreground transition-colors",
          "hover-hover:hover:text-foreground focus-visible:outline-none focus-visible:ring-[3px] focus-visible:ring-ring/50",
          "disabled:pointer-events-none disabled:opacity-50"
        )}
      >
        {revealed ? <EyeOff className="size-4" aria-hidden="true" /> : <Eye className="size-4" aria-hidden="true" />}
      </button>
    </div>
  )
}

export { PasswordInput }
