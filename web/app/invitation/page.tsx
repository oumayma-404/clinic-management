"use client"

import { useCallback, useEffect, useRef, useState } from "react"
import Link from "next/link"
import { AlertCircle } from "lucide-react"

import { Button } from "@/components/ui/button"
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card"
import { Input } from "@/components/ui/input"
import { PasswordInput } from "@/components/ui/password-input"
import { Label } from "@/components/ui/label"
import { FormErrorBanner } from "@/components/ui/form-error-banner"
import { LoadFailureNotice } from "@/components/ui/load-failure"
import { authApi, type StaffInvitationPreviewDto } from "@/lib/api/auth"
import { ApiError } from "@/lib/api/client"
import { usePasswordMinLength } from "@/lib/hooks/use-password-policy"
import { getErrorMessage, isNetworkError } from "@/lib/errors"
import { replaceUrl } from "@/lib/history"

/** The server's own sentence for a used, expired, re-sent or unknown link — the fallback if none arrives. */
const UNUSABLE_LINK =
  "Ce lien d'invitation n'est plus valable. Demandez à l'administrateur du cabinet de vous le renvoyer."

const OFFLINE =
  "Impossible de joindre le serveur. Vérifiez votre connexion, puis réessayez — votre lien reste valable."

/** 410 is the server's « this link is spent »; a 404 means this server invites nobody by link. Same answer here. */
function isUnusableLink(err: unknown): boolean {
  return err instanceof ApiError && (err.status === 410 || err.status === 404)
}

/** The token travels in the fragment (`#t=`), never sent to a server or written to a log; read once, then erased. */
function takeTokenFromFragment(): string | null {
  const hash = window.location.hash.startsWith("#") ? window.location.hash.slice(1) : window.location.hash
  const token = new URLSearchParams(hash).get("t")
  if (token) replaceUrl(window.location.pathname)
  return token
}

type Stage =
  | { kind: "reading" }
  | { kind: "no-token" }
  | { kind: "unusable"; message: string }
  | { kind: "read-failed" }
  | { kind: "ready"; preview: StaffInvitationPreviewDto }

/**
 * A colleague invited by e-mail chooses her first password (`guided-first-days` C).
 *
 * ⚠️ **Nothing is spent on mount** — the preview only reads, so a mail client's link-preview fetch cannot burn the
 * link; it is spent by « Enregistrer », like the password reset. ⚠️ **No session is issued**: success is a full
 * navigation to `/login` with her address filled in, because only a full load lets that page read its query string.
 */
export default function InvitationPage() {
  const minLength = usePasswordMinLength()

  // Read once and kept in a ref: the fragment is erased on the first read, and a StrictMode re-run of the effect
  // would otherwise find nothing and report « lien incomplet » on a perfectly good link.
  const token = useRef<string | null | undefined>(undefined)
  const [stage, setStage] = useState<Stage>({ kind: "reading" })

  const [password, setPassword] = useState("")
  const [confirmation, setConfirmation] = useState("")
  const [error, setError] = useState<string | null>(null)
  const [isSubmitting, setIsSubmitting] = useState(false)

  const readPreview = useCallback(async () => {
    const value = token.current
    if (!value) {
      setStage({ kind: "no-token" })
      return
    }
    setStage({ kind: "reading" })
    try {
      setStage({ kind: "ready", preview: await authApi.previewInvitation(value) })
    } catch (err) {
      setStage(
        isUnusableLink(err)
          ? { kind: "unusable", message: getErrorMessage(err, UNUSABLE_LINK) }
          : { kind: "read-failed" },
      )
    }
  }, [])

  useEffect(() => {
    if (token.current === undefined) token.current = takeTokenFromFragment()
    void readPreview()
  }, [readPreview])

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault()
    if (stage.kind !== "ready") return
    setError(null)

    if (password !== confirmation) {
      setError("Les deux mots de passe ne correspondent pas.")
      return
    }
    // The floor is the server's (`usePasswordMinLength`); unknown → no pre-check, the server answers in French.
    if (minLength !== null && password.length < minLength) {
      setError(`Le mot de passe doit contenir au moins ${minLength} caractères.`)
      return
    }

    setIsSubmitting(true)
    try {
      await authApi.acceptInvitation(token.current ?? "", password)
      window.location.href = `/login?passwordChanged=1&email=${encodeURIComponent(stage.preview.email)}`
    } catch (err) {
      if (isUnusableLink(err)) {
        setStage({ kind: "unusable", message: getErrorMessage(err, UNUSABLE_LINK) })
      } else {
        // The form stays open with what she typed (§ 13).
        setError(isNetworkError(err) ? OFFLINE : getErrorMessage(err, UNUSABLE_LINK))
      }
      setIsSubmitting(false)
    }
  }

  return (
    <div className="flex min-h-dvh justify-start overflow-y-auto bg-background p-4 sm:p-6">
      <div className="mx-auto my-auto w-full max-w-md">
        <Card>
          {stage.kind === "ready" ? (
            <>
              <CardHeader className="space-y-1">
                <CardTitle className="text-2xl font-bold">Bienvenue, {stage.preview.firstName}</CardTitle>
                <CardDescription>
                  {stage.preview.clinicName} vous a créé un compte APEXA. Choisissez votre mot de passe.
                </CardDescription>
              </CardHeader>
              <CardContent>
                <form onSubmit={handleSubmit} className="space-y-4">
                  <FormErrorBanner message={error} />

                  {/* Read-only, and still a field: it is the identifiant she will type at /login, and a password
                      manager saves the new password against the `username` beside it. */}
                  <div className="space-y-2">
                    <Label htmlFor="invitation-email">Votre identifiant</Label>
                    <Input
                      id="invitation-email"
                      type="email"
                      autoComplete="username"
                      value={stage.preview.email}
                      readOnly
                    />
                  </div>

                  <div className="space-y-2">
                    {/* The floor is interpolated, never a digit in the sentence (`PasswordFloorSingleSourceTests`),
                        and stated only once the server's figure is known. */}
                    <Label htmlFor="invitation-password">
                      Mot de passe
                      {minLength !== null && (
                        <span className="font-normal text-muted-foreground"> · au moins {minLength} caractères</span>
                      )}
                    </Label>
                    <PasswordInput
                      id="invitation-password"
                      autoComplete="new-password"
                      value={password}
                      disabled={isSubmitting}
                      onChange={(e) => setPassword(e.target.value)}
                      required
                    />
                  </div>

                  <div className="space-y-2">
                    <Label htmlFor="invitation-confirm">Confirmer le mot de passe</Label>
                    <PasswordInput
                      id="invitation-confirm"
                      autoComplete="new-password"
                      value={confirmation}
                      disabled={isSubmitting}
                      onChange={(e) => setConfirmation(e.target.value)}
                      required
                    />
                  </div>

                  <Button type="submit" className="min-h-11 w-full" disabled={isSubmitting}>
                    {isSubmitting ? "Enregistrement…" : "Enregistrer mon mot de passe"}
                  </Button>
                </form>
              </CardContent>
            </>
          ) : stage.kind === "reading" ? (
            <CardContent className="py-10">
              <p role="status" className="text-center text-sm text-muted-foreground">
                Lecture de l&apos;invitation…
              </p>
            </CardContent>
          ) : stage.kind === "read-failed" ? (
            <CardContent className="py-6">
              <LoadFailureNotice
                message="L'invitation n'a pas pu être lue."
                detail="Vérifiez votre connexion, puis réessayez — votre lien reste valable."
                onRetry={() => void readPreview()}
              />
            </CardContent>
          ) : (
            <>
              <CardHeader className="space-y-4 text-center">
                <div className="mx-auto inline-flex size-16 items-center justify-center rounded-full bg-muted">
                  <AlertCircle className="size-8 text-destructive" aria-hidden="true" />
                </div>
                <div>
                  <CardTitle className="text-2xl">
                    {stage.kind === "no-token" ? "Lien incomplet" : "Lien expiré"}
                  </CardTitle>
                  <CardDescription className="mt-2" role="status">
                    {stage.kind === "no-token"
                      ? "Cette page s'ouvre depuis le lien reçu par e-mail. Ouvrez-le à nouveau depuis l'e-mail."
                      : stage.message}
                  </CardDescription>
                </div>
              </CardHeader>
              {/* Somebody who already chose her password lands here from the old e-mail — the way in is /login. */}
              <CardContent>
                <Button asChild variant="outline" className="min-h-11 w-full">
                  <Link href="/login">J&apos;ai déjà un mot de passe : se connecter</Link>
                </Button>
              </CardContent>
            </>
          )}
        </Card>
      </div>
    </div>
  )
}
