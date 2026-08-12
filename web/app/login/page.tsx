"use client"

import { useEffect, useState } from 'react'
import { useRouter } from 'next/navigation'
import { useSession } from '@/lib/auth/session'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { FormErrorBanner } from '@/components/ui/form-error-banner'
import { TotpCodeField } from '@/components/security/totp-code-field'
import { TotpEnrolmentStep } from '@/components/security/totp-enrolment-step'
import { RecoveryCodesPanel } from '@/components/security/recovery-codes-panel'
import { PRODUCT_NAME } from '@/lib/brand'

export default function LoginPage() {
  const { user, isLoading, mode } = useSession()
  const router = useRouter()

  useEffect(() => {
    if (user && !isLoading) {
      // Cloud: go to /setup (redirects onward if a clinic exists). Local: go to the app.
      router.push(mode === 'local' ? '/' : '/setup')
    }
  }, [user, isLoading, mode, router])

  if (isLoading) {
    return (
      <div className="flex h-dvh items-center justify-center">
        <p className="text-muted-foreground">Chargement…</p>
      </div>
    )
  }

  if (user) {
    return null
  }

  if (mode === 'local') {
    return <LocalLoginForm />
  }

  return (
    <LoginShell
      title={`Bienvenue sur ${PRODUCT_NAME}`}
      description="Connectez-vous pour accéder au système de gestion de la clinique"
    >
      <Button asChild className="min-h-11 w-full" size="lg">
        <a href="/auth/login">Se connecter</a>
      </Button>
    </LoginShell>
  )
}

/**
 * The full-screen frame every mode renders inside.
 *
 * ⚠️ **`items-start` + `overflow-y-auto` + `my-auto` on the child**, which is the pattern
 * `session-lock-gate.tsx` and `client-version-gate.tsx` already share. `items-center` inside a scroller pushes
 * overflow to *both* ends and the **top** is outside the scrollable region — so on a landscape phone the top of
 * a tall card (the enrolment step, the eight recovery codes) is unreachable by any means. An auto margin
 * centres when there is room and degrades to top-aligned when there is not.
 */
function LoginShell({
  title,
  description,
  children,
}: {
  title: string
  description: string
  children: React.ReactNode
}) {
  return (
    <div className="fixed inset-0 flex h-dvh items-start justify-center overflow-y-auto bg-background p-4">
      <Card className="my-auto w-full max-w-md">
        <CardHeader className="space-y-1">
          <CardTitle className="text-2xl font-bold">{title}</CardTitle>
          <CardDescription>{description}</CardDescription>
        </CardHeader>
        <CardContent>{children}</CardContent>
      </Card>
    </div>
  )
}

/** The four states this screen can be in (`hosted-security-hardening` FR-1.2/FR-1.3/FR-1.4). */
type Mode = 'login' | 'enrol' | 'recovery' | 'codes'

interface EnrolmentMaterial {
  secretUri: string | null
  secret: string | null
  qrPng: string | null
}

/**
 * Sign in, enrol, recover — one component, four modes.
 *
 * ⚠️ **One component because the address and password must survive the transition.** An account told « enrol
 * your factor first » has to arrive at the enrolment form with what it already typed intact; re-typing them is
 * not friction here, it is the moment somebody gives up. `console/app/login/sign-in-form.tsx` is the working
 * reference and states the same reason.
 *
 * ⚠️ **Every transition is driven by the refusal's `code`, never by its French sentence.** Matching prose is
 * what this repository deleted elsewhere, and it would break the first time a message is reworded.
 */
function LocalLoginForm() {
  const [mode, setMode] = useState<Mode>('login')
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [totpCode, setTotpCode] = useState('')
  const [recoveryCode, setRecoveryCode] = useState('')
  // Whether the code field is on screen. Driven by the server's own `totp_required`, so the client never
  // decides for itself whether an account has a second factor.
  const [needsCode, setNeedsCode] = useState(false)
  const [material, setMaterial] = useState<EnrolmentMaterial | null>(null)
  const [codes, setCodes] = useState<string[] | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [isSubmitting, setIsSubmitting] = useState(false)
  // A successful password change revokes its own session (SetPassword bumps TokenVersion), so
  // /change-password lands here rather than in the app. Without this the user meets a bare login form
  // and cannot tell whether the change was saved — the one fact they came here to establish.
  const [passwordChanged, setPasswordChanged] = useState(false)
  // The one-time sign-out on deploy (FR-1.7). A bare form after a working session is indistinguishable from a
  // bug, so it is said out loud rather than left for the user to work out.
  const [sessionsEnded, setSessionsEnded] = useState(false)

  useEffect(() => {
    const params = new URLSearchParams(window.location.search)
    setPasswordChanged(params.get('passwordChanged') === '1')
    setSessionsEnded(params.get('sessionsEnded') === '1')
    // The client routes here with `?enrol=1&email=…` when a live session meets `totp_enrolment_required`
    // (A.4, step 28): the refusal has to have a destination, or the app looks usable and is dead.
    if (params.get('enrol') === '1') {
      setMode('enrol')
      const address = params.get('email')
      if (address) setEmail(address)
    }
  }, [])

  const goHome = () => {
    // Full navigation so the session cookie is picked up by middleware + providers. Only same-origin absolute
    // paths — reject protocol-relative ("//host") and backslash ("/\\host") forms browsers treat as off-site.
    const returnTo = new URLSearchParams(window.location.search).get('returnTo')
    const isSafeReturnTo =
      !!returnTo && returnTo.startsWith('/') && !returnTo.startsWith('//') && !returnTo.startsWith('/\\')
    window.location.href = isSafeReturnTo ? returnTo! : '/'
  }

  /** Step one of enrolment: ask the server for a secret to scan. Also the QR's « Réessayer ». */
  const requestEnrolmentMaterial = async () => {
    setError(null)
    setIsSubmitting(true)
    try {
      const res = await fetch('/bff/auth/totp-enrol', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ email, password }),
      })
      const data = await res.json().catch(() => null)
      if (!res.ok) {
        setError(data?.error || "Impossible de préparer l'enrôlement.")
        return
      }
      setMaterial({
        secretUri: data?.secretUri ?? null,
        secret: data?.secret ?? null,
        qrPng: data?.secretQrPng ?? null,
      })
      setMode('enrol')
    } catch {
      setError('Impossible de joindre le serveur de la clinique. Veuillez réessayer.')
    } finally {
      setIsSubmitting(false)
    }
  }

  const handleLogin = async (e: React.FormEvent) => {
    e.preventDefault()
    setError(null)
    setIsSubmitting(true)
    try {
      const res = await fetch('/bff/auth/local-login', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        // Sent only once the field exists, so « pas encore demandé » stays distinct from « demandé et faux ».
        body: JSON.stringify({ email, password, ...(totpCode ? { totpCode } : {}) }),
      })
      const data = await res.json().catch(() => null)

      if (!res.ok) {
        const code: string | undefined = data?.code

        // The password was right and something else is owed — never a « mot de passe invalide ».
        if (code === 'totp_enrolment_required') {
          setIsSubmitting(false)
          await requestEnrolmentMaterial()
          return
        }

        if (code === 'totp_required') {
          // Ask for the code, keeping everything typed. Not an error state: nobody has got anything wrong.
          setMode('login')
          setError(null)
          setTotpCode('')
          setNeedsCode(true)
          setIsSubmitting(false)
          return
        }

        setError(data?.error || 'Identifiants invalides.')
        setIsSubmitting(false)
        return
      }

      goHome()
    } catch {
      setError('Impossible de joindre le serveur de la clinique. Veuillez réessayer.')
      setIsSubmitting(false)
    }
  }

  const handleConfirmEnrolment = async (e: React.FormEvent) => {
    e.preventDefault()
    setError(null)
    setIsSubmitting(true)
    try {
      const res = await fetch('/bff/auth/totp-enrol', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ email, password, totpCode }),
      })
      const data = await res.json().catch(() => null)
      if (!res.ok) {
        setError(data?.error || 'Code de vérification invalide.')
        setIsSubmitting(false)
        return
      }
      setCodes(data?.recoveryCodes ?? [])
      setTotpCode('')
      setMode('codes')
    } catch {
      setError('Impossible de joindre le serveur de la clinique. Veuillez réessayer.')
    } finally {
      setIsSubmitting(false)
    }
  }

  const handleRecovery = async (e: React.FormEvent) => {
    e.preventDefault()
    setError(null)
    setIsSubmitting(true)
    try {
      const res = await fetch('/bff/auth/recovery', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ email, password, recoveryCode }),
      })
      const data = await res.json().catch(() => null)
      if (!res.ok) {
        setError(data?.error || 'Code de récupération invalide.')
        setIsSubmitting(false)
        return
      }
      goHome()
    } catch {
      setError('Impossible de joindre le serveur de la clinique. Veuillez réessayer.')
      setIsSubmitting(false)
    }
  }

  // ── The codes screen: enrolment STOPS here rather than signing in ──────────────────────────────────────
  if (mode === 'codes' && codes) {
    return (
      <LoginShell
        title="Second facteur activé"
        description="Conservez ces codes de récupération avant de continuer."
      >
        <RecoveryCodesPanel
          codes={codes}
          confirmLabel="Continuer vers la connexion"
          onConfirm={() => {
            // Deliberately back to the sign-in rather than into the app: enrolling is not signing in, and
            // signing in now proves the authenticator works before anybody depends on it.
            setCodes(null)
            setMaterial(null)
            setMode('login')
            setNeedsCode(true)
          }}
        />
      </LoginShell>
    )
  }

  // ── Enrolment ─────────────────────────────────────────────────────────────────────────────────────────
  if (mode === 'enrol') {
    return (
      <LoginShell
        title="Activer le second facteur"
        description="Ce compte doit être protégé par un code à usage unique."
      >
        <form onSubmit={handleConfirmEnrolment} className="space-y-4">
          <FormErrorBanner message={error} />
          <TotpEnrolmentStep
            qrPng={material?.qrPng ?? null}
            secretUri={material?.secretUri ?? null}
            secret={material?.secret ?? null}
            onRetry={requestEnrolmentMaterial}
            busy={isSubmitting}
          />
          <TotpCodeField
            value={totpCode}
            onChange={setTotpCode}
            autoFocus
            disabled={isSubmitting}
            hint="Saisissez le code affiché par votre application pour confirmer."
          />
          <Button type="submit" className="min-h-11 w-full" disabled={isSubmitting}>
            {isSubmitting ? 'Vérification…' : 'Confirmer'}
          </Button>
          <Button
            type="button"
            variant="ghost"
            className="min-h-11 w-full"
            onClick={() => {
              setMode('login')
              setError(null)
              setTotpCode('')
            }}
          >
            Revenir à la connexion
          </Button>
        </form>
      </LoginShell>
    )
  }

  // ── Recovery code ─────────────────────────────────────────────────────────────────────────────────────
  if (mode === 'recovery') {
    return (
      <LoginShell
        title="Utiliser un code de récupération"
        description="Saisissez un de vos codes de récupération à usage unique."
      >
        <form onSubmit={handleRecovery} className="space-y-4">
          <FormErrorBanner message={error} />
          <EmailField value={email} onChange={setEmail} disabled={isSubmitting} />
          <PasswordField value={password} onChange={setPassword} disabled={isSubmitting} />
          <div className="space-y-2">
            <Label htmlFor="recovery-code">Code de récupération</Label>
            {/* Deliberately NOT type="password": it is being copied off paper, and the whole difficulty is
                reading it correctly. */}
            <Input
              id="recovery-code"
              name="recovery-code"
              type="text"
              autoComplete="off"
              required
              value={recoveryCode}
              onChange={(e) => setRecoveryCode(e.target.value)}
            />
          </div>
          <Button type="submit" className="min-h-11 w-full" disabled={isSubmitting}>
            {isSubmitting ? 'Vérification…' : 'Se connecter'}
          </Button>
          <Button
            type="button"
            variant="ghost"
            className="min-h-11 w-full"
            onClick={() => {
              setMode('login')
              setError(null)
              setRecoveryCode('')
            }}
          >
            Revenir à la connexion
          </Button>
        </form>
      </LoginShell>
    )
  }

  // ── Sign in ───────────────────────────────────────────────────────────────────────────────────────────
  return (
    <LoginShell title="Connexion" description="Saisissez les identifiants de votre compte clinique">
      <form onSubmit={handleLogin} className="space-y-4">
        {passwordChanged && !error && (
          <div
            role="status"
            className="rounded-lg border border-success/20 bg-success-wash p-3 text-sm text-success"
          >
            Mot de passe enregistré. Connectez-vous avec votre nouveau mot de passe.
          </div>
        )}
        {sessionsEnded && !error && (
          <div role="status" className="rounded-lg border border-border bg-muted/40 p-3 text-sm">
            Pour renforcer la sécurité, toutes les sessions ont été fermées. Connectez-vous à nouveau.
          </div>
        )}

        {/* The shared banner rather than a hand-rolled div: it carries `role="alert"` + `aria-live`, which the
            one thing standing between the user and the app has to announce. The old block had no role at all. */}
        <FormErrorBanner message={error} />

        <EmailField value={email} onChange={setEmail} disabled={isSubmitting} />
        <PasswordField value={password} onChange={setPassword} disabled={isSubmitting} />

        {needsCode && (
          <TotpCodeField
            value={totpCode}
            onChange={setTotpCode}
            autoFocus
            disabled={isSubmitting}
            hint="Ouvrez votre application d'authentification et saisissez le code affiché."
          />
        )}

        <Button type="submit" className="min-h-11 w-full" disabled={isSubmitting}>
          {isSubmitting ? 'Connexion…' : 'Se connecter'}
        </Button>

        {needsCode && (
          <Button
            type="button"
            variant="ghost"
            className="min-h-11 w-full"
            onClick={() => {
              setMode('recovery')
              setError(null)
            }}
          >
            Je n&apos;ai plus accès à mon application
          </Button>
        )}

        {/* /signup states « Inscription non disponible ici » itself where the capability is off, so this
            link needs no probe of its own — unlike the retired /join one, whose code path is closed. */}
        <p className="text-center text-sm text-muted-foreground">
          Vous n&apos;avez pas encore de cabinet ?{' '}
          <a href="/signup" className="text-primary hover:underline">
            Créer mon cabinet
          </a>
        </p>
      </form>
    </LoginShell>
  )
}

function EmailField({
  value,
  onChange,
  disabled,
}: {
  value: string
  onChange: (v: string) => void
  disabled?: boolean
}) {
  return (
    <div className="space-y-2">
      <Label htmlFor="email">Email</Label>
      <Input
        id="email"
        type="email"
        autoComplete="username"
        value={value}
        disabled={disabled}
        onChange={(e) => onChange(e.target.value)}
        required
      />
    </div>
  )
}

function PasswordField({
  value,
  onChange,
  disabled,
}: {
  value: string
  onChange: (v: string) => void
  disabled?: boolean
}) {
  return (
    <div className="space-y-2">
      <Label htmlFor="password">Mot de passe</Label>
      <Input
        id="password"
        type="password"
        autoComplete="current-password"
        value={value}
        disabled={disabled}
        onChange={(e) => onChange(e.target.value)}
        required
      />
    </div>
  )
}
