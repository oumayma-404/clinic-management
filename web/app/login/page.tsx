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

  /*
   * ⚠️ **Two flows only this screen can serve, and a SIGNED-IN account legitimately needs both** — so being
   * signed in must not bounce you away from them.
   *
   * `?replace=1` (« Sécurité » → replace a factor bound to a lost phone) and `?enrol=1` (a live session that
   * met `totp_enrolment_required`) both run through the **anonymous** enrolment endpoint, which takes an address
   * and a password rather than a session. Without this exemption the effect below sent an authenticated user
   * straight back to `/`, so « Remplacer maintenant » navigated here and returned home having done nothing —
   * a dead affordance that type-checks and builds perfectly, and was only visible by clicking it.
   *
   * Computed in a lazy initialiser rather than an effect on purpose: an effect would leave one commit in which
   * the flag is still false, and the redirect below would win that race.
   */
  const [factorFlow] = useState(() => {
    if (typeof window === 'undefined') return false
    const params = new URLSearchParams(window.location.search)
    return params.get('replace') === '1' || params.get('enrol') === '1'
  })

  useEffect(() => {
    if (user && !isLoading && !factorFlow) {
      // Cloud: go to /setup (redirects onward if a clinic exists). Local: go to the app.
      router.push(mode === 'local' ? '/' : '/setup')
    }
  }, [user, isLoading, mode, router, factorFlow])

  if (isLoading) {
    return (
      <div className="flex h-dvh items-center justify-center">
        <p className="text-muted-foreground">Chargement…</p>
      </div>
    )
  }

  // The redirect above is running — render nothing rather than a form that is about to disappear. Exempt for
  // the two factor flows, which are exactly the case where a signed-in account still needs this screen.
  if (user && !factorFlow) {
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

/**
 * The five states this screen can be in (`hosted-security-hardening` FR-1.2/FR-1.3/FR-1.4).
 *
 * ⚠️ **`totp` is a screen of its own, not a field appended to `login`.** The server already answers in two rounds
 * — password first, `totp_required` second — and the code used to arrive as a third input *below* a still-editable
 * address and password, under the same « Se connecter ». That presented one decision as two: the fields that were
 * already accepted stayed live, so a mistyped code invited re-checking the password, and the button gave no sign
 * that the first half had succeeded. A separate step states what is being asked and for which account.
 */
type Mode = 'login' | 'totp' | 'enrol' | 'recovery' | 'codes' | 'replace'

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

    // « Sécurité » routes here with `?replace=1` while the window a redeemed code opened is still open. Same
    // reason as the branch above: the offer has to have a destination, or the sentence on that page points at a
    // password form and reads as having lost the session.
    if (params.get('replace') === '1') {
      setMode('replace')
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

  /**
   * `e` is optional so the six-digit field can submit itself the moment the code is complete — the behaviour every
   * authenticator flow has, and the reason nobody looks for the button. The `isSubmitting` guard is what makes that
   * safe: `onComplete` fires again if the user pastes over a full field, and the click is still reachable.
   */
  const handleLogin = async (e?: React.FormEvent) => {
    e?.preventDefault()
    if (isSubmitting) return
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
          // Advance to the second step, keeping everything typed. Not an error state: nobody has got anything
          // wrong, and the password just succeeded — so no banner, and the button label changes rather than a
          // field appearing under one that already worked.
          setMode('totp')
          setError(null)
          setTotpCode('')
          setIsSubmitting(false)
          return
        }

        setError(data?.error || 'Identifiants invalides.')
        // A refused code is refused *here*, on the step that asked for it. Clearing it is what makes the field
        // ready for the next 30-second window without the user selecting six stale digits first — and the
        // password is deliberately left alone, since the server already accepted it.
        if (mode === 'totp') setTotpCode('')
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

      // ⚠️ Signed in, but still bound to the authenticator they no longer have — and the server has just opened
      // a short window in which that can be fixed. Going straight home would spend the code and change nothing:
      // for a cabinet with a single administrator that is the whole difference between recovering and running
      // out of codes one sign-in at a time.
      if (data?.mayReplaceSecondFactor) {
        setRecoveryCode('')
        setMode('replace')
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
            setMode('totp')
          }}
        />
      </LoginShell>
    )
  }

  // ── Step two: the authenticator code ──────────────────────────────────────────────────────────────────
  //
  // Reached only when the server said `totp_required`, i.e. the address and password have already been accepted.
  // It therefore shows them as a settled fact rather than as fields: re-editing them here would silently start a
  // different sign-in, and the one thing still owed is the six digits.
  if (mode === 'totp') {
    return (
      <LoginShell
        title="Vérification en deux étapes"
        description="Votre mot de passe est accepté. Saisissez le code à 6 chiffres de votre application d'authentification."
      >
        <form onSubmit={handleLogin} className="space-y-4">
          <FormErrorBanner message={error} />

          {/* Which account is being signed in. Without it this screen is a code box with no subject — and on a
              shared reception machine that is exactly the moment somebody enters the wrong colleague's code. */}
          <p className="rounded-lg border border-border bg-muted/40 p-3 text-sm">
            <span className="text-muted-foreground">Compte : </span>
            <span className="font-medium [overflow-wrap:anywhere]">{email}</span>
          </p>

          <TotpCodeField
            value={totpCode}
            onChange={setTotpCode}
            onComplete={handleLogin}
            autoFocus
            disabled={isSubmitting}
            hint="Le code change toutes les 30 secondes."
          />

          <Button type="submit" className="min-h-11 w-full" disabled={isSubmitting}>
            {isSubmitting ? 'Vérification…' : 'Vérifier'}
          </Button>

          <Button
            type="button"
            variant="ghost"
            className="min-h-11 w-full"
            disabled={isSubmitting}
            onClick={() => {
              setMode('recovery')
              setError(null)
              setTotpCode('')
            }}
          >
            Je n&apos;ai plus accès à mon application
          </Button>

          {/* The way back. The password step no longer shows its own fields here, so without this an address
              typed by mistake is a dead end — § 0: never remove a capability, name the way out. */}
          <Button
            type="button"
            variant="ghost"
            className="min-h-11 w-full"
            disabled={isSubmitting}
            onClick={() => {
              setMode('login')
              setError(null)
              setTotpCode('')
              setPassword('')
            }}
          >
            Utiliser un autre compte
          </Button>
        </form>
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

  // ── After a recovery code: offer to move the factor to the new phone ──────────────────────────────────
  //
  // ⚠️ An OFFER, not a wall. A recovery code is also what somebody uses whose phone is at home rather than
  // gone, and forcing a re-scan would cost them a working enrolment for nothing. « Plus tard » is therefore a
  // real way out — the window simply lapses, and « Sécurité » says so while it lasts.
  if (mode === 'replace') {
    return (
      <LoginShell
        title="Remplacer votre second facteur"
        description="Vous êtes connecté. Votre second facteur est encore lié à votre ancien appareil."
      >
        {/* A form with both fields rather than a bare button, because this screen has TWO entry points: straight
            off the recovery sign-in (where both are already in state, so it is pre-filled and one click) and
            « Sécurité »'s `?replace=1` link (where the page has just loaded and holds neither). One code path
            serves both, which is the same reason this whole flow is one component. */}
        <form
          className="space-y-4"
          onSubmit={(e) => {
            e.preventDefault()
            void requestEnrolmentMaterial()
          }}
        >
          <FormErrorBanner message={error} />
          <p className="text-sm text-muted-foreground">
            Si vous avez perdu ce téléphone, remplacez-le maintenant : vous obtiendrez un nouveau QR code à
            scanner et une nouvelle série de codes de récupération. Vos anciens codes seront annulés.
          </p>
          <p className="text-sm text-muted-foreground">
            Cette possibilité n’est ouverte que quelques minutes après l’utilisation d’un code de récupération.
          </p>
          <EmailField value={email} onChange={setEmail} disabled={isSubmitting} />
          <PasswordField value={password} onChange={setPassword} disabled={isSubmitting} />
          <Button type="submit" className="min-h-11 w-full" disabled={isSubmitting}>
            {isSubmitting ? 'Préparation…' : 'Remplacer maintenant'}
          </Button>
          <Button
            type="button"
            variant="ghost"
            className="min-h-11 w-full"
            disabled={isSubmitting}
            onClick={goHome}
          >
            Plus tard
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
              // Back to the step this was reached from, never to the password form: the password is already
              // accepted at that point, and « Revenir à la connexion » would read as having lost it.
              setMode('totp')
              setError(null)
              setRecoveryCode('')
            }}
          >
            Revenir au code de vérification
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

        {/* No code field here any more: an account that owes one is sent to the `totp` step above, which is
            reached only once the server has accepted this password. « Je n'ai plus accès à mon application »
            moved with it — offered before we know a second factor is even required, it advertised a recovery
            path to every user who has no factor to recover. */}
        <Button type="submit" className="min-h-11 w-full" disabled={isSubmitting}>
          {isSubmitting ? 'Connexion…' : 'Se connecter'}
        </Button>

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
