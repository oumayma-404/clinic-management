"use client"

import { useEffect, useRef, useState } from "react"
import Link from "next/link"
import { AlertCircle, CheckCircle2 } from "lucide-react"

import { Button } from "@/components/ui/button"
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card"
import { Input } from "@/components/ui/input"
import { Label } from "@/components/ui/label"
import { FormErrorBanner } from "@/components/ui/form-error-banner"
import { authApi } from "@/lib/api/auth"
import { usePasswordMinLength } from "@/lib/hooks/use-password-policy"
import { getErrorMessage, isNetworkError } from "@/lib/errors"

/** The single French refusal the server sends for expired / spent / unknown / account-no-longer-resettable. */
const FALLBACK_REFUSAL =
  "Ce lien de réinitialisation n'est plus valable. Demandez-en un nouveau depuis l'écran de connexion."

/**
 * The token arrives in the URL **fragment**, not the query string: a fragment is never sent to the server, so the
 * live single-use credential stays out of the reverse proxy's access log and every intermediate hop — all of which
 * outlive by a long way the hour the token is bounded by. Read once on mount, then erased from the address bar so
 * it does not survive in history or session restore either. `/signup/verifier` does exactly this.
 */
function takeTokenFromFragment(): string | null {
  if (typeof window === "undefined") return null

  const hash = window.location.hash.startsWith("#") ? window.location.hash.slice(1) : window.location.hash
  const token = new URLSearchParams(hash).get("token")
  if (token) {
    window.history.replaceState(null, "", window.location.pathname)
  }

  return token
}

/**
 * Choose a new password behind a mailed single-use link.
 *
 * ⚠️ **Unlike `/signup/verifier`, nothing is spent on mount.** That page consumes its token immediately because
 * verifying *is* the whole action; here the token is only spent once the person has typed a password, so the effect
 * captures it and waits. A link opened by a mail client's link-preview fetch must not burn itself.
 *
 * ⚠️ **No session is issued on success** (the server sends no token and sets no cookie), so this ends at a link to
 * the login screen rather than a redirect into the app. That is the design and not an omission: holding the e-mail
 * is not holding the second factor, and the six-digit code is still required at the sign-in that follows.
 */
export default function ResetPasswordPage() {
  const minLength = usePasswordMinLength()

  const [password, setPassword] = useState("")
  const [confirmation, setConfirmation] = useState("")
  const [error, setError] = useState<string | null>(null)
  const [isSubmitting, setIsSubmitting] = useState(false)
  const [done, setDone] = useState(false)

  // A ref, not state: the token must survive re-renders without causing one, and it is read inside the submit
  // handler rather than rendered anywhere.
  const token = useRef<string | null>(null)
  const [hasToken, setHasToken] = useState<boolean | null>(null)

  useEffect(() => {
    token.current = takeTokenFromFragment()
    setHasToken(token.current !== null)
  }, [])

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault()
    setError(null)

    if (password !== confirmation) {
      setError("Les deux mots de passe ne correspondent pas.")
      return
    }

    // ⚠️ The floor comes from the server (`usePasswordMinLength`), never a literal — four screens each carried
    // their own number once, and raising the constant left them refusing at one figure while the API refused at
    // another and quoted the stale one in French. `null` means « not known yet », in which case we do not
    // pre-check at all and let the server answer with its own sentence and its own number.
    if (minLength !== null && password.length < minLength) {
      setError(`Le mot de passe doit contenir au moins ${minLength} caractères.`)
      return
    }

    setIsSubmitting(true)
    try {
      await authApi.completePasswordReset(token.current ?? "", password)
      setDone(true)
    } catch (err) {
      setError(
        isNetworkError(err)
          ? "Impossible de joindre le serveur. Vérifiez votre connexion, puis réessayez — votre lien reste valable."
          : getErrorMessage(err, FALLBACK_REFUSAL),
      )
    } finally {
      // The form stays open with the typed values intact on any refusal (§ 13).
      setIsSubmitting(false)
    }
  }

  return (
    <div className="flex min-h-dvh justify-start overflow-y-auto bg-background p-4 sm:p-6">
      <div className="mx-auto my-auto w-full max-w-md">
        <Card>
          {done ? (
            <>
              <CardHeader className="space-y-4 text-center">
                <div className="mx-auto inline-flex size-16 items-center justify-center rounded-full bg-accent/20">
                  <CheckCircle2 className="size-8 text-primary" aria-hidden="true" />
                </div>
                <div>
                  <CardTitle className="text-2xl">Mot de passe enregistré</CardTitle>
                  <CardDescription className="mt-2" role="status">
                    Connectez-vous avec votre nouveau mot de passe.
                  </CardDescription>
                </div>
              </CardHeader>
              <CardContent className="space-y-3">
                {/* Said here because it is the next thing that will happen and the person is already mid-recovery:
                    arriving at a code prompt unwarned, having just proved their e-mail, reads as the reset not
                    having worked. */}
                <p className="text-sm text-muted-foreground">
                  Votre code de vérification à six chiffres reste demandé à la connexion : il n&apos;a pas été
                  modifié. Vos autres appareils ont été déconnectés.
                </p>
                <Button asChild className="min-h-11 w-full">
                  <Link href="/login">Se connecter</Link>
                </Button>
              </CardContent>
            </>
          ) : hasToken === false ? (
            <>
              <CardHeader className="space-y-4 text-center">
                <div className="mx-auto inline-flex size-16 items-center justify-center rounded-full bg-muted">
                  <AlertCircle className="size-8 text-destructive" aria-hidden="true" />
                </div>
                <div>
                  <CardTitle className="text-2xl">Lien incomplet</CardTitle>
                  <CardDescription className="mt-2" role="status">
                    Cette page s&apos;ouvre depuis le lien reçu par e-mail. Ouvrez-le à nouveau, ou demandez-en un
                    nouveau.
                  </CardDescription>
                </div>
              </CardHeader>
              <CardContent>
                <Button asChild className="min-h-11 w-full">
                  <Link href="/mot-de-passe-oublie">Demander un nouveau lien</Link>
                </Button>
              </CardContent>
            </>
          ) : (
            <>
              <CardHeader className="space-y-1">
                <CardTitle className="text-2xl font-bold">Nouveau mot de passe</CardTitle>
                <CardDescription>
                  Choisissez le mot de passe de votre compte. Le lien ne peut servir qu&apos;une seule fois.
                </CardDescription>
              </CardHeader>
              <CardContent>
                <form onSubmit={handleSubmit} className="space-y-4">
                  <FormErrorBanner message={error} />

                  <div className="space-y-2">
                    <Label htmlFor="new-password">Nouveau mot de passe</Label>
                    <Input
                      id="new-password"
                      type="password"
                      autoComplete="new-password"
                      value={password}
                      disabled={isSubmitting}
                      onChange={(e) => setPassword(e.target.value)}
                      required
                    />
                    {/* Interpolated, never a digit in the sentence: `PasswordFloorSingleSourceTests` scans `web/`
                        for « au moins N caractères » and fails the build on a literal. Rendered only once the
                        served floor is known, so no figure is ever asserted that the server does not enforce. */}
                    {minLength !== null && (
                      <p className="text-xs text-muted-foreground">
                        Au moins {minLength} caractères.
                      </p>
                    )}
                  </div>

                  <div className="space-y-2">
                    <Label htmlFor="confirm-password">Confirmer le mot de passe</Label>
                    <Input
                      id="confirm-password"
                      type="password"
                      autoComplete="new-password"
                      value={confirmation}
                      disabled={isSubmitting}
                      onChange={(e) => setConfirmation(e.target.value)}
                      required
                    />
                  </div>

                  <Button
                    type="submit"
                    className="min-h-11 w-full"
                    disabled={isSubmitting || hasToken === null}
                  >
                    {isSubmitting ? "Enregistrement…" : "Enregistrer le mot de passe"}
                  </Button>

                  <p className="text-center text-sm text-muted-foreground">
                    <Link href="/login" className="text-primary hover:underline">
                      Revenir à la connexion
                    </Link>
                  </p>
                </form>
              </CardContent>
            </>
          )}
        </Card>
      </div>
    </div>
  )
}
