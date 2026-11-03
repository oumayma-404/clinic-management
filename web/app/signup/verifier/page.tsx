"use client"

import { useCallback, useEffect, useRef, useState } from "react"
import Link from "next/link"
import { AlertCircle, CheckCircle2, Loader2, WifiOff } from "lucide-react"

import { VideoLink } from "@/components/onboarding/video-link"
import { Button } from "@/components/ui/button"
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card"
import { authApi } from "@/lib/api/auth"
import { PRODUCT_NAME } from "@/lib/brand"
import { getErrorMessage, isNetworkError } from "@/lib/errors"
import { replaceUrl } from "@/lib/history"
import { ONBOARDING_VIDEOS } from "@/lib/onboarding-videos"

type VerificationState =
  | { kind: "verifying" }
  | { kind: "done"; clinicName: string; message: string }
  | { kind: "refused"; message: string }
  | { kind: "offline"; message: string }

/** Where the authenticator the first sign-in asks for is installed from. */
const AUTHENTICATOR_STORES = [
  { label: "Google Play", href: "https://play.google.com/store/apps/details?id=com.google.android.apps.authenticator2" },
  { label: "App Store", href: "https://apps.apple.com/app/google-authenticator/id388497605" },
] as const

/** The single French refusal the server sends for expired / unknown / malformed / now-taken. */
const FALLBACK_REFUSAL =
  "Ce lien de vérification n'est plus valable. Recommencez l'inscription pour en recevoir un nouveau."

/**
 * The token arrives in the URL **fragment**, not the query string: a fragment is never sent to the server, so the
 * live single-use credential stays out of the reverse proxy's access log and every intermediate hop — all of which
 * outlive the 24 h the token is bounded by. Read once on mount, then erased from the address bar so it does not
 * survive in history or session restore either.
 */
function takeTokenFromFragment(): string | null {
  if (typeof window === "undefined") return null

  const hash = window.location.hash.startsWith("#") ? window.location.hash.slice(1) : window.location.hash
  const token = new URLSearchParams(hash).get("token")
  if (token) {
    replaceUrl(window.location.pathname)
  }

  return token
}

export default function SignUpVerifierPage() {
  const [state, setState] = useState<VerificationState>({ kind: "verifying" })

  // The token is single-use, and React 18's development StrictMode mounts every effect twice — without this the
  // second run would spend the token the first one just consumed and the page would render its own success as a
  // refusal. A ref rather than a state flag because it must be read synchronously, before the first await.
  const attempted = useRef(false)
  const token = useRef<string | null>(null)

  const verify = useCallback(async () => {
    const raw = token.current
    if (!raw) {
      setState({ kind: "refused", message: FALLBACK_REFUSAL })
      return
    }

    setState({ kind: "verifying" })
    try {
      const result = await authApi.verifySignUp(raw)
      setState({ kind: "done", clinicName: result.clinicName, message: result.message })
    } catch (err) {
      // A lost connection is not a refused link: collapsing the two told a visitor on a weak mobile signal that a
      // still-valid single-use link was dead. Only a real 4xx says « ce lien ne vaut plus rien ».
      if (isNetworkError(err)) {
        setState({
          kind: "offline",
          message: "Vérifiez votre connexion, puis réessayez. Votre lien reste valable.",
        })
        return
      }
      setState({ kind: "refused", message: getErrorMessage(err, FALLBACK_REFUSAL) })
    }
  }, [])

  useEffect(() => {
    if (attempted.current) return
    attempted.current = true

    token.current = takeTokenFromFragment()
    void verify()
  }, [verify])

  return (
    // One centred status panel at every width — there is nothing here to reflow. `my-auto` on the child rather
    // than `items-center` on the parent, so a landscape phone can still reach the top of the card.
    <div className="flex min-h-dvh justify-start overflow-y-auto bg-background p-4 sm:p-6">
      <div className="mx-auto my-auto w-full max-w-md">
        {state.kind === "verifying" && (
          <Panel
            icon={<Loader2 className="size-8 animate-spin text-primary" aria-hidden="true" />}
            title="Vérification en cours…"
            description="Nous créons votre cabinet. Ne fermez pas cette page."
          />
        )}

        {state.kind === "offline" && (
          <Panel
            icon={<WifiOff className="size-8 text-muted-foreground" aria-hidden="true" />}
            title="Connexion interrompue"
            description={state.message}
          >
            <Button className="w-full" onClick={() => void verify()}>
              Réessayer
            </Button>
          </Panel>
        )}

        {state.kind === "refused" && (
          <Panel
            icon={<AlertCircle className="size-8 text-destructive" aria-hidden="true" />}
            title="Lien non valable"
            description={state.message}
          >
            <Button asChild className="w-full">
              <Link href="/signup">Recommencer l&apos;inscription</Link>
            </Button>
          </Panel>
        )}

        {state.kind === "done" && (
          <Panel
            icon={<CheckCircle2 className="size-8 text-primary" aria-hidden="true" />}
            title={`${state.clinicName} est créé`}
            description={state.message}
          >
            <div className="space-y-3">
              <NextStepSecurityCode />
              {/* No session is issued here (the server sends no token and sets no cookie), so this is a link to the
                  login screen and not a redirect into the app — the visitor signs in with the password they chose. */}
              <Button asChild className="min-h-11 w-full">
                <Link href="/login">Se connecter</Link>
              </Button>
              <VideoLink video={ONBOARDING_VIDEOS.firstSignIn} className="w-full" />
            </div>
          </Panel>
        )}
      </div>
    </div>
  )
}

/**
 * The code asked for at the first sign-in, announced BEFORE it is needed (guided-first-days A). Until now only the
 * welcome e-mail mentioned it, and that e-mail arrives after this page — so the first anybody heard of an
 * authenticator was a QR code on the sign-in screen, with no app on the phone to scan it.
 */
function NextStepSecurityCode() {
  return (
    <section aria-labelledby="next-step-title" className="space-y-3 rounded-lg border bg-muted/30 p-3 text-start">
      <h2 id="next-step-title" className="text-sm font-semibold">
        Prochaine étape : votre code de sécurité
      </h2>
      <ol className="space-y-3 text-sm">
        <li className="flex gap-3">
          <StepNumber n={1} />
          <div className="min-w-0 space-y-2">
            <p>Installez Google Authenticator sur votre téléphone.</p>
            <div className="flex flex-wrap gap-2">
              {AUTHENTICATOR_STORES.map((store) => (
                <Button key={store.label} asChild variant="outline" size="sm" className="min-h-11 bg-card">
                  <a href={store.href} target="_blank" rel="noopener noreferrer">
                    <span className="sr-only">Google Authenticator sur </span>
                    {store.label}
                  </a>
                </Button>
              ))}
            </div>
          </div>
        </li>
        <li className="flex gap-3">
          <StepNumber n={2} />
          <p className="min-w-0">Connectez-vous : {PRODUCT_NAME} affiche un QR code à scanner.</p>
        </li>
      </ol>
    </section>
  )
}

/** The visible number of an `<ol>` item — the list already announces its position, so this one is hidden. */
function StepNumber({ n }: { n: number }) {
  return (
    <span
      aria-hidden
      className="inline-flex size-6 shrink-0 items-center justify-center rounded-full bg-primary text-xs font-semibold text-primary-foreground"
    >
      {n}
    </span>
  )
}

function Panel({
  icon,
  title,
  description,
  children,
}: {
  icon: React.ReactNode
  title: string
  description: string
  children?: React.ReactNode
}) {
  return (
    <Card className="border-primary/20 shadow-lg">
      <CardHeader className="space-y-4 text-center">
        <div className="mx-auto inline-flex size-16 items-center justify-center rounded-full bg-accent/20">
          {icon}
        </div>
        <div>
          {/* `break-words`: the success title carries the clinic's own name, and one typed without spaces is an
              unbreakable run wider than the 240 px content box at 320 px — which nothing up the tree clips, so the
              document itself gains horizontal scroll. */}
          <CardTitle className="text-2xl break-words text-accent-foreground">{title}</CardTitle>
          <CardDescription className="mt-2" role="status">
            {description}
          </CardDescription>
        </div>
      </CardHeader>
      {children && <CardContent>{children}</CardContent>}
    </Card>
  )
}
