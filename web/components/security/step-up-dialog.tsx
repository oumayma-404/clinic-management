"use client"

import { useEffect, useRef, useState } from 'react'
import { Button } from '@/components/ui/button'
import { Dialog, DialogContent, DialogDescription, DialogHeader, DialogTitle } from '@/components/ui/dialog'
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
 * ⚠️ **A bottom sheet below `md:` and a dialog above it — from ONE `Dialog`** (§ 5). That shape is
 * `DialogContent`'s `mobile="bottom"` default, not something this component builds; see the note above the
 * return. It is sized in `dvh`, never `vh`: a `vh` cap does not shrink when the on-screen keyboard opens, so
 * the confirm button ends up underneath it, on a surface whose only purpose is to be confirmed.
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

  /*
   * ⚠️ ONE Dialog, and the responsive half is `DialogContent`'s job — not a pair of wrappers.
   *
   * This used to render a `Sheet` inside `<div className="md:hidden">` and a `Dialog` inside
   * `<div className="hidden md:block">`. Both Radix primitives render their content through a PORTAL onto
   * `document.body`, so the content never sits inside those wrapper divs and the Tailwind visibility classes
   * reached nothing at all. Both mounted, at every viewport width: a centred dialog AND a bottom sheet on
   * screen together, asking the same question twice. It read as « double modal » and it was.
   *
   * `DialogContent` already owns this decision — `mobile="bottom"` is its DEFAULT and is exactly the shape
   * that was being hand-rolled: bottom edge, `max-h-[90dvh]`, and the safe-area padding for the home
   * indicator that the hand-rolled Sheet did not have. So the correct fix removes code rather than adding a
   * `useMediaQuery`: one Dialog, and the breakpoint lives in the one place the whole app shares.
   */
  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="md:max-w-md">
        <DialogHeader>
          <DialogTitle>Confirmer votre identité</DialogTitle>
          <DialogDescription>{purpose}</DialogDescription>
        </DialogHeader>
        {body}
      </DialogContent>
    </Dialog>
  )
}
