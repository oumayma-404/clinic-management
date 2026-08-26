"use client"

import * as React from "react"

import {
  AlertDialog,
  AlertDialogCancel,
  AlertDialogContent,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogHeader,
  AlertDialogTitle,
} from "@/components/ui/alert-dialog"
import { Button } from "@/components/ui/button"
import { Input } from "@/components/ui/input"
import { Label } from "@/components/ui/label"
import { cn } from "@/lib/utils"
import { quoteFr } from "@/lib/format"

/**
 * A confirmation that cannot be clicked through: the operator must **type** the given phrase before the
 * destructive action unlocks (AC-P3.47).
 *
 * Why this exists as a primitive rather than an inline pattern: the repo has exactly one destructive-confirm
 * shape — a two-button `AlertDialog` — and it is used for everything from deleting a procedure type to
 * deleting a patient. An irreversible, multi-table operation (anonymisation, finalising a bordereau) needs to
 * *feel* different from the dialog the user dismisses twenty times a day, or "says so unambiguously" resolves
 * to the same two buttons and the same reflex click. Adding it here, in P3's accessibility pass, means P7 and
 * P8 inherit one implementation instead of improvising three (AC-P3.44).
 *
 * Accessibility, matching the bar this part sets for every new surface:
 * - a real `<Label htmlFor>` on the input, not a placeholder standing in for one;
 * - the required phrase is rendered as text, so it is readable and copyable, not only in the prompt;
 * - `aria-describedby` ties the input to the "not yet matching" hint, and the hint is `role="status"` so a
 *   screen-reader user learns the button unlocked without polling it;
 * - the confirm button carries the in-flight state (AC-P3.45) and the dialog stays open on failure, with the
 *   typed phrase intact.
 */
export interface ConfirmByTypingDialogProps {
  open: boolean
  onOpenChange: (open: boolean) => void
  title: string
  /** What will happen, in full. This is the operator's last chance to read it. */
  description: React.ReactNode
  /** The exact phrase to type. Compared trimmed and case-sensitively — a deliberate gesture, not a guess. */
  requiredPhrase: string
  /** Label above the input. Defaults to naming the phrase. */
  inputLabel?: string
  confirmLabel: string
  /** Shown on the confirm button while the action is in flight. */
  pendingLabel?: string
  cancelLabel?: string
  pending?: boolean
  onConfirm: () => void
}

export function ConfirmByTypingDialog({
  open,
  onOpenChange,
  title,
  description,
  requiredPhrase,
  inputLabel,
  confirmLabel,
  pendingLabel,
  cancelLabel = "Annuler",
  pending = false,
  onConfirm,
}: ConfirmByTypingDialogProps) {
  const [typed, setTyped] = React.useState("")
  const inputId = React.useId()
  const hintId = React.useId()

  // Clear the phrase whenever the dialog opens, so a previous attempt can never leave the button pre-unlocked.
  React.useEffect(() => {
    if (open) setTyped("")
  }, [open])

  const matches = typed.trim() === requiredPhrase

  return (
    <AlertDialog
      open={open}
      onOpenChange={(next) => {
        // An in-flight irreversible action must not be dismissed out from under itself.
        if (pending) return
        onOpenChange(next)
      }}
    >
      <AlertDialogContent>
        <AlertDialogHeader>
          <AlertDialogTitle>{title}</AlertDialogTitle>
          <AlertDialogDescription>{description}</AlertDialogDescription>
        </AlertDialogHeader>

        <div className="space-y-2">
          <Label htmlFor={inputId}>
            {inputLabel ?? (
              <>
                Pour confirmer, saisissez{" "}
                <span className="font-mono font-semibold">{requiredPhrase}</span>
              </>
            )}
          </Label>
          <Input
            id={inputId}
            value={typed}
            onChange={(event) => setTyped(event.target.value)}
            aria-describedby={hintId}
            autoComplete="off"
            disabled={pending}
          />
          <p
            id={hintId}
            role="status"
            className={cn("text-xs", matches ? "text-muted-foreground" : "text-amber-700 dark:text-amber-400")}
          >
            {matches
              ? "Confirmation saisie — l'action est déverrouillée."
              : `Saisissez exactement ${quoteFr(requiredPhrase)} pour déverrouiller l'action.`}
          </p>
        </div>

        <AlertDialogFooter>
          <AlertDialogCancel disabled={pending}>{cancelLabel}</AlertDialogCancel>
          {/* Deliberately a plain Button, not AlertDialogAction: the action must not close the dialog on
              click — it closes only once the caller reports success, so a failure leaves the typed phrase
              and the explanation on screen (AC-P3.45). */}
          <Button
            variant="destructive"
            disabled={!matches || pending}
            onClick={onConfirm}
          >
            {pending ? (pendingLabel ?? "Traitement…") : confirmLabel}
          </Button>
        </AlertDialogFooter>
      </AlertDialogContent>
    </AlertDialog>
  )
}
