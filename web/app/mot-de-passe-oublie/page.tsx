"use client"

import { useEffect, useState } from "react"
import Link from "next/link"
import { AlertCircle, MailCheck } from "lucide-react"

import { Button } from "@/components/ui/button"
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card"
import { Input } from "@/components/ui/input"
import { Label } from "@/components/ui/label"
import { FormErrorBanner } from "@/components/ui/form-error-banner"
import { authApi } from "@/lib/api/auth"
import { CAPABILITY_PROBE_TIMEOUT_MS, withTimeout } from "@/lib/capability-probe"
import { getErrorMessage, isNetworkError } from "@/lib/errors"

/**
 * « J'ai oublié mon mot de passe » — asks for a single-use link, and says the same thing either way.
 *
 * ⚠️ **The acknowledgement is the server's own sentence, rendered verbatim.** It is written to be true whether a
 * link was sent or nothing was, and re-phrasing it here — or adding « vérifiez votre boîte » only on some branch —
 * would rebuild the enumeration oracle the backend took care not to expose. There is deliberately no distinct
 * « adresse inconnue » state on this screen for the same reason.
 *
 * ⚠️ **The capability is probed, exactly as `/signup` probes `publicSignupEnabled`.** On a `SelfHostedLan` install
 * the endpoint is absent (404 before the mediator), because a surgery PC has no SMTP credentials — so this page
 * states who to ask instead of offering a form that cannot work. `!== true` rather than `=== false`: the field is
 * absent on an older API, and an undefined answer must land on the sentence that is true everywhere.
 */
type Stage =
  | { kind: "probing" }
  | { kind: "unavailable" }
  | { kind: "form" }
  | { kind: "sent"; message: string }

export default function ForgotPasswordPage() {
  const [stage, setStage] = useState<Stage>({ kind: "probing" })
  const [email, setEmail] = useState("")
  const [error, setError] = useState<string | null>(null)
  const [isSubmitting, setIsSubmitting] = useState(false)

  useEffect(() => {
    let cancelled = false

    void (async () => {
      try {
        const { passwordResetEnabled } = await withTimeout(authApi.getMode(), CAPABILITY_PROBE_TIMEOUT_MS)
        if (!cancelled) setStage(passwordResetEnabled === true ? { kind: "form" } : { kind: "unavailable" })
      } catch {
        // ⚠️ A failed probe lands on the FORM, not on « indisponible ». The endpoint is the authority and answers
        // 404 itself where the capability is off; treating an unreachable probe as « no self-service » would hide a
        // working recovery path from somebody on a weak signal — on the one screen they reached because they are
        // already locked out.
        if (!cancelled) setStage({ kind: "form" })
      }
    })()

    return () => {
      cancelled = true
    }
  }, [])

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault()
    setError(null)
    setIsSubmitting(true)

    try {
      const result = await authApi.requestPasswordReset(email)
      setStage({ kind: "sent", message: result.message })
    } catch (err) {
      // A 404 here means the deployment has no self-service reset at all — worth saying plainly rather than as a
      // generic failure, since the person's next step is entirely different.
      setError(
        isNetworkError(err)
          ? "Impossible de joindre le serveur. Vérifiez votre connexion, puis réessayez."
          : getErrorMessage(
              err,
              "La demande n'a pas pu aboutir. Réessayez, ou contactez l'administrateur de votre cabinet.",
            ),
      )
    } finally {
      // ⚠️ In `finally`, so a refusal leaves the form usable with the address still typed (§ 13). The success
      // branch has already replaced the whole card, so re-enabling a button nobody can see costs nothing.
      setIsSubmitting(false)
    }
  }

  return (
    // `my-auto` on the child rather than `items-center` on the scroller: centred content inside an `overflow-y-auto`
    // box pushes its top overflow outside the scrollable region, which on a landscape phone makes the top of a tall
    // card unreachable by any means.
    <div className="flex min-h-dvh justify-start overflow-y-auto bg-background p-4 sm:p-6">
      <div className="mx-auto my-auto w-full max-w-md">
        <Card>
          {stage.kind === "sent" ? (
            <>
              <CardHeader className="space-y-4 text-center">
                <div className="mx-auto inline-flex size-16 items-center justify-center rounded-full bg-accent/20">
                  <MailCheck className="size-8 text-primary" aria-hidden="true" />
                </div>
                <div>
                  <CardTitle className="text-2xl">Vérifiez votre boîte e-mail</CardTitle>
                  <CardDescription className="mt-2" role="status">
                    {stage.message}
                  </CardDescription>
                </div>
              </CardHeader>
              <CardContent className="space-y-3">
                <p className="text-sm text-muted-foreground">
                  Le lien ne peut servir qu&apos;une seule fois. Votre mot de passe actuel reste valable
                  jusqu&apos;à ce que vous en choisissiez un nouveau.
                </p>
                <Button asChild variant="outline" className="min-h-11 w-full">
                  <Link href="/login">Revenir à la connexion</Link>
                </Button>
              </CardContent>
            </>
          ) : stage.kind === "unavailable" ? (
            <>
              <CardHeader className="space-y-4 text-center">
                <div className="mx-auto inline-flex size-16 items-center justify-center rounded-full bg-muted">
                  <AlertCircle className="size-8 text-muted-foreground" aria-hidden="true" />
                </div>
                <div>
                  <CardTitle className="text-2xl">Réinitialisation par e-mail indisponible</CardTitle>
                  <CardDescription className="mt-2" role="status">
                    Cette installation n&apos;envoie pas d&apos;e-mails.
                  </CardDescription>
                </div>
              </CardHeader>
              <CardContent className="space-y-3">
                {/* Names both ways back that DO exist here, in the order the person should try them. Neither
                    mentions the « réseau local » — the same server is reached over Wi-Fi and over a mobile
                    network, so that wording points a dentist at something that is not there. */}
                <p className="text-sm text-muted-foreground">
                  Demandez à un administrateur de votre cabinet de réinitialiser votre mot de passe depuis
                  l&apos;écran « Utilisateurs » : il vous remettra un mot de passe temporaire.
                </p>
                <p className="text-sm text-muted-foreground">
                  Si vous êtes le seul administrateur, la personne qui gère l&apos;ordinateur du cabinet peut le
                  faire directement sur ce poste.
                </p>
                <Button asChild variant="outline" className="min-h-11 w-full">
                  <Link href="/login">Revenir à la connexion</Link>
                </Button>
              </CardContent>
            </>
          ) : (
            <>
              <CardHeader className="space-y-1">
                <CardTitle className="text-2xl font-bold">Mot de passe oublié</CardTitle>
                <CardDescription>
                  Saisissez l&apos;adresse e-mail de votre compte. Nous vous enverrons un lien pour choisir un
                  nouveau mot de passe.
                </CardDescription>
              </CardHeader>
              <CardContent>
                <form onSubmit={handleSubmit} className="space-y-4">
                  <FormErrorBanner message={error} />

                  <div className="space-y-2">
                    <Label htmlFor="reset-email">E-mail</Label>
                    <Input
                      id="reset-email"
                      type="email"
                      autoComplete="username"
                      value={email}
                      disabled={isSubmitting || stage.kind === "probing"}
                      onChange={(e) => setEmail(e.target.value)}
                      required
                    />
                  </div>

                  <Button
                    type="submit"
                    className="min-h-11 w-full"
                    disabled={isSubmitting || stage.kind === "probing"}
                  >
                    {isSubmitting ? "Envoi…" : "Envoyer le lien"}
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
