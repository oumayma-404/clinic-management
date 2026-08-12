"use client"

import { useEffect, useRef, useState } from 'react'
import { Button } from '@/components/ui/button'
import { Dialog, DialogContent, DialogDescription, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { Sheet, SheetContent, SheetDescription, SheetHeader, SheetTitle } from '@/components/ui/sheet'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { FormErrorBanner } from '@/components/ui/form-error-banner'
import { TotpCodeField } from '@/components/security/totp-code-field'
import { securityApi } from '@/lib/api/security'
import { getErrorMessage } from '@/lib/errors'

interface StepUpDialogProps {
  open: boolean
  onOpenChange: (open: boolean) => void
  /** What the confirmation will authorise. A token minted for one action does not open another. */
  action: string
  /** Shown above the fields, so the user knows what they are confirming. */
  purpose: string
  /** Whether this account holds a factor — decides which proof is offered first. */
  hasTotp: boolean
  onConfirmed: (confirmationToken: string) => void
}

/**
 * Re-authenticates for one sensitive action (`hosted-security-hardening` FR-1.8).
 *
 * ⚠️ **A sheet below `md:` and a dialog above it** — § 5. Both are sized in `dvh`, never `vh`: a `vh` cap does
 * not shrink when the on-screen keyboard opens, so the confirm button ends up underneath it, on a surface whose
 * only purpose is to be confirmed.
 *
 * ⚠️ **Focus lands on the field and returns to the trigger on close**, and `Escape` closes — Radix gives the
 * last two, and the first is explicit because this surface has exactly one thing to do.
 *
 * ⚠️ **It says up front that failing costs nothing but this action.** Three wrong attempts refuse on the
 * step-up's own counter with the session untouched, and a user who thinks they are about to lock their account
 * out will not attempt it at all.
 */
export function StepUpDialog({
  open,
  onOpenChange,
  action,
  purpose,
  hasTotp,
  onConfirmed,
}: StepUpDialogProps) {
  const [password, setPassword] = useState('')
  const [totpCode, setTotpCode] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  const fieldRef = useRef<HTMLInputElement>(null)

  useEffect(() => {
    if (!open) {
      setPassword('')
      setTotpCode('')
      setError(null)
      setBusy(false)
    }
  }, [open])

  const submit = async (e: React.FormEvent) => {
    e.preventDefault()
    setError(null)
    setBusy(true)
    try {
      const proof = hasTotp && totpCode ? { totpCode } : { password }
      const { confirmationToken } = await securityApi.stepUp(action, proof)
      onConfirmed(confirmationToken)
      onOpenChange(false)
    } catch (err) {
      // The form stays open with its input intact — § 13.
      setError(getErrorMessage(err))
    } finally {
      setBusy(false)
    }
  }

  const body = (
    <form onSubmit={submit} className="space-y-4">
      <FormErrorBanner message={error} />

      {hasTotp ? (
        <TotpCodeField
          value={totpCode}
          onChange={setTotpCode}
          autoFocus
          disabled={busy}
          hint="Saisissez le code affiché par votre application d'authentification."
        />
      ) : (
        <div className="space-y-2">
          <Label htmlFor="stepup-password">Mot de passe</Label>
          <Input
            id="stepup-password"
            ref={fieldRef}
            type="password"
            autoComplete="current-password"
            autoFocus
            required
            disabled={busy}
            value={password}
            onChange={(e) => setPassword(e.target.value)}
          />
        </div>
      )}

      <p className="text-sm text-muted-foreground">
        Votre session reste ouverte : une erreur ici n&apos;affecte que cette action.
      </p>

      <div className="flex flex-col-reverse gap-2 sm:flex-row sm:justify-end">
        <Button
          type="button"
          variant="outline"
          className="min-h-11"
          onClick={() => onOpenChange(false)}
          disabled={busy}
        >
          Annuler
        </Button>
        <Button type="submit" className="min-h-11" disabled={busy}>
          {busy ? 'Vérification…' : 'Confirmer'}
        </Button>
      </div>
    </form>
  )

  return (
    <>
      {/* Below `md:` — a sheet, sized in dvh so the keyboard cannot bury the confirm button. */}
      <div className="md:hidden">
        <Sheet open={open} onOpenChange={onOpenChange}>
          <SheetContent side="bottom" className="max-h-[90dvh] overflow-y-auto">
            <SheetHeader>
              <SheetTitle>Confirmer votre identité</SheetTitle>
              <SheetDescription>{purpose}</SheetDescription>
            </SheetHeader>
            <div className="mt-4">{body}</div>
          </SheetContent>
        </Sheet>
      </div>

      {/* From `md:` — the ordinary dialog. */}
      <div className="hidden md:block">
        <Dialog open={open} onOpenChange={onOpenChange}>
          <DialogContent className="max-h-[90dvh] overflow-y-auto md:max-w-md">
            <DialogHeader>
              <DialogTitle>Confirmer votre identité</DialogTitle>
              <DialogDescription>{purpose}</DialogDescription>
            </DialogHeader>
            {body}
          </DialogContent>
        </Dialog>
      </div>
    </>
  )
}
