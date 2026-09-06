"use client"

import * as React from "react"
import { Eye, EyeOff } from "lucide-react"

import { Input } from "@/components/ui/input"
import { cn } from "@/lib/utils"

/**
 * A password field with a show/hide toggle — the ONLY way this app renders `type="password"`.
 *
 * <p>Kept byte-identical to `web/components/ui/password-input.tsx` on purpose — the console is its own Next app
 * with its own copy of every primitive, and this one is reached over an SSH tunnel on whatever laptop the
 * operator has to hand, where typing a long password blind is exactly as unpleasant.</p>
 *
 * <p>⚠️ The recovery-code field on the sign-in form is deliberately NOT one of these: it is copied off paper and
 * the whole difficulty is reading it correctly.</p>
 *
 * <p>⚠️ The toggle is a real `<button type="button">`: inside a `<form>` a bare `<button>` submits, which on the
 * login screen would spend a sign-in attempt against the 5-per-15-minutes lockout just for peeking.</p>
 *
 * <p>Painted at 28 px, with `touch-target` supplying the 44 px hit area on a finger.</p>
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
