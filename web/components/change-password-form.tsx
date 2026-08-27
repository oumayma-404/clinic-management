"use client"

import { useState } from 'react'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { FormErrorBanner } from '@/components/ui/form-error-banner'
import { usePasswordMinLength } from '@/lib/hooks/use-password-policy'

interface ChangePasswordFormProps {
  /** True when the user was forced here by an admin password reset (AC-5.2). */
  forced: boolean
}

export function ChangePasswordForm({ forced }: ChangePasswordFormProps) {
  const [currentPassword, setCurrentPassword] = useState('')
  const [newPassword, setNewPassword] = useState('')
  const [confirmPassword, setConfirmPassword] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [isSubmitting, setIsSubmitting] = useState(false)
  // null while unknown (or if the probe failed) — the server still refuses a short password with its own
  // sentence, so an unread floor costs a courtesy check rather than letting a weak password through.
  const minLength = usePasswordMinLength()

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault()
    setError(null)

    if (minLength !== null && newPassword.length < minLength) {
      setError(`Le nouveau mot de passe doit contenir au moins ${minLength} caractères.`)
      return
    }
    if (newPassword !== confirmPassword) {
      setError('Le nouveau mot de passe et sa confirmation ne correspondent pas.')
      return
    }
    if (newPassword === currentPassword) {
      setError('Le nouveau mot de passe doit être différent de l\'actuel.')
      return
    }

    setIsSubmitting(true)
    try {
      const res = await fetch('/bff/auth/change-password', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ currentPassword, newPassword }),
      })
      const data = await res.json().catch(() => null)
      if (!res.ok) {
        setError(data?.error || 'Échec du changement de mot de passe.')
        setIsSubmitting(false)
        return
      }
      // Full navigation, and to /login rather than /: `User.SetPassword` bumps `TokenVersion`, so the
      // session that submitted this change is revoked by the change itself (AC-5.2) and the BFF route has
      // just dropped both cookies. Sending the user to / would land them on a page whose first API call
      // 401s and bounces them here anyway, with nothing saying the password was actually saved.
      window.location.href = '/login?passwordChanged=1'
    } catch {
      setError('Impossible de joindre le serveur du cabinet. Veuillez réessayer.')
      setIsSubmitting(false)
    }
  }

  return (
    <div className="flex min-h-dvh items-center justify-center bg-background p-4">
      <Card className="w-full max-w-md">
        <CardHeader className="space-y-1">
          <CardTitle className="text-2xl font-bold">
            {forced ? 'Définir un nouveau mot de passe' : 'Changer le mot de passe'}
          </CardTitle>
          <CardDescription>
            {forced
              ? 'Votre mot de passe a été réinitialisé par un administrateur. Choisissez un nouveau mot de passe pour continuer.'
              : 'Saisissez votre mot de passe actuel et choisissez-en un nouveau.'}
          </CardDescription>
        </CardHeader>
        <CardContent>
          <form onSubmit={handleSubmit} className="space-y-4">
            {/* The shared banner rather than a hand-rolled div: it already renders on `--destructive-wash`
                and carries `role="alert"` + `aria-live`, which a failed password change — the one thing
                standing between the user and the app — has to announce. */}
            <FormErrorBanner message={error} />

            <div className="space-y-2">
              <Label htmlFor="current-password">
                {forced ? 'Mot de passe temporaire' : 'Mot de passe actuel'}
              </Label>
              <Input
                id="current-password"
                type="password"
                autoComplete="current-password"
                value={currentPassword}
                onChange={(e) => setCurrentPassword(e.target.value)}
                required
              />
            </div>
            <div className="space-y-2">
              <Label htmlFor="new-password">Nouveau mot de passe</Label>
              <Input
                id="new-password"
                type="password"
                autoComplete="new-password"
                value={newPassword}
                onChange={(e) => setNewPassword(e.target.value)}
                required
              />
            </div>
            <div className="space-y-2">
              <Label htmlFor="confirm-password">Confirmer le nouveau mot de passe</Label>
              <Input
                id="confirm-password"
                type="password"
                autoComplete="new-password"
                value={confirmPassword}
                onChange={(e) => setConfirmPassword(e.target.value)}
                required
              />
            </div>
            <Button type="submit" className="w-full" size="lg" disabled={isSubmitting}>
              {isSubmitting ? 'Enregistrement…' : 'Enregistrer le nouveau mot de passe'}
            </Button>
          </form>
        </CardContent>
      </Card>
    </div>
  )
}
