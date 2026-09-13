"use client"

import { useCallback, useEffect, useRef, useState } from "react"
import { Fingerprint, Lock } from "lucide-react"
import { Button } from "@/components/ui/button"
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card"

/** AC-57. A dismissal counts too — it is the only bound on how long a live cookie may sit behind this overlay. */
const MAX_ATTEMPTS = 3

/**
 * Does this runtime have a shell that can confirm the device owner?
 *
 * A feature detection, never an assumption (`mobile/shared/bridge.md`): `false` in every browser and in a
 * Phase 1 shell, which is what keeps AC-58 — the inactivity path unchanged where the bridge is absent — true by
 * construction rather than by branch.
 */
export function canConfirmIdentityInShell(): boolean {
  return typeof window !== "undefined" && typeof window.__clinicShell?.confirmIdentity === "function"
}

interface SessionLockGateProps {
  /** The OS confirmed the owner: re-arm the inactivity timer and uncover the app. The cookie is never cleared. */
  onConfirmed: () => void
  /** Out of attempts: the ordinary logout, with the user's place remembered. */
  onFallBackToPassword: () => void
  /**
   * Whether « Rester connecté sur cet appareil » was ticked for this session.
   *
   * ⚠️ It decides what a device that **cannot ask** does, and nothing else. On a trusted device the gate stays
   * up as a plain cover — one click resumes, no password and no code, which is what the login screen promised —
   * instead of ending a thirty-day session because the OS has no Windows Hello credential to offer. See
   * `idleExpiryEnding`.
   */
  trusted: boolean
}

/**
 * What a native shell shows instead of logging out at the inactivity limit (AC-57…AC-60).
 *
 * <p><b>The app stays mounted behind it.</b> That is the whole value: a dentist who put the phone down mid-fiche
 * gets the fiche back, not a login screen and a lost hour. The session cookie is <b>not</b> cleared on the
 * success path — AC-57 says so explicitly, because a passing banner and a destroyed session look identical from
 * the outside.</p>
 *
 * <p><b>And it is opaque, which is not decoration.</b> The limit exists so a phone left on a counter stops
 * showing a patient's record; awaiting the OS prompt over a visible page would keep the record on screen for
 * anyone who dismisses it. Covering the app is what makes the pause mean anything.</p>
 *
 * <p><b>Three attempts, then the password screen.</b> A refusal and a dismissal both count — one rule, and the
 * only thing bounding how long a still-valid cookie can sit behind a client-side overlay. A device with no
 * enrolled biometric and no PIN answers <code>unavailable</code> and falls through immediately: no dead control
 * and no error, which is AC-60.</p>
 *
 * <p><b>Nothing here stores a password (AC-59).</b> The shell asks the OS a yes/no question about the person
 * holding the phone; the session resumed is the one already in the WebView's cookie store.</p>
 */
export function SessionLockGate({ onConfirmed, onFallBackToPassword, trusted }: SessionLockGateProps) {
  const [attemptsUsed, setAttemptsUsed] = useState(0)
  const [busy, setBusy] = useState(false)
  const [lastOutcome, setLastOutcome] = useState<ShellIdentityOutcome | null>(null)

  /*
   * The device cannot ask, and this session is trusted: the gate becomes a cover, not a checkpoint. « Reprendre »
   * is the only thing left to do here — there is nothing to verify against, so a control that pretends otherwise
   * would be the dead one AC-60 refuses.
   */
  const [coverOnly, setCoverOnly] = useState(false)

  // Refs, not state, because both are read inside an in-flight async call that must not re-run to see them.
  const inFlight = useRef(false)
  const attemptsUsedRef = useRef(0)

  /*
   * What « this device cannot confirm anybody » means, in one place.
   *
   * ⚠️ **Trusted ⇒ cover, never sign out** — the whole point of the fix this prop exists for. An untrusted
   * session keeps the old ending exactly: no verifier, no session.
   */
  const cannotAsk = useCallback(() => {
    if (trusted) {
      setCoverOnly(true)
      return
    }
    onFallBackToPassword()
  }, [trusted, onFallBackToPassword])

  const attempt = useCallback(async () => {
    if (inFlight.current) return
    const shell = typeof window !== "undefined" ? window.__clinicShell : undefined
    if (!shell?.confirmIdentity) {
      /*
       * No bridge at all — a plain browser, a shell older than the contract, or one deleted at runtime (AC-26).
       *
       * ⚠️ On an untrusted session this still fails closed, which is what AC-26 verifies. On a trusted one it
       * covers instead: the cookie behind this overlay is valid for a month either way, so ending the session
       * here would not be failing closed, it would be spending the promise to punish a missing API.
       */
      cannotAsk()
      return
    }

    inFlight.current = true
    setBusy(true)
    let outcome: ShellIdentityOutcome
    try {
      outcome = await shell.confirmIdentity()
    } catch {
      // The contract says it never rejects. If a shell breaks that, treat it as a device that cannot ask —
      // never as a confirmation.
      outcome = "unavailable"
    }
    inFlight.current = false
    setBusy(false)

    if (outcome === "confirmed") {
      onConfirmed()
      return
    }
    if (outcome === "unavailable") {
      // No enrolled biometric and no PIN, a reader that is gone, a policy that forbids it: none of those is a
      // person failing a check, so none of them may cost a trusted session (`idleExpiryEnding`).
      cannotAsk()
      return
    }

    /*
     * ⚠️ **A dismissal is not a failed identity check, and it used to cost the same as one.** `cancelled` and
     * `rejected` both consumed an attempt, so three stray clicks — or three Hello prompts that appeared before
     * the user was looking at them — ended the session and asked for the password and a six-digit code. Nobody
     * had refused anything; the OS had simply not been answered.
     *
     * So a dismissal re-shows this gate and costs nothing. The attempt count still exists and still bounds the
     * thing it was written to bound: `rejected` is the OS saying « that is not the owner », and three of those
     * still fall through to the password.
     *
     * ⚠️ That does leave a dismissal repeatable without limit, which is deliberate and is not the exposure it
     * sounds like: this overlay is opaque `bg-background` precisely so a paused session shows nothing, so an
     * indefinitely-dismissed lock is a blank screen over a covered app — the same state as a locked laptop.
     * And « Se déconnecter » below is a one-click exit, so nobody is trapped here either.
     */
    if (outcome === "cancelled") {
      setLastOutcome("cancelled")
      return
    }

    const used = attemptsUsedRef.current + 1
    attemptsUsedRef.current = used
    setAttemptsUsed(used)
    setLastOutcome(outcome)
    if (used >= MAX_ATTEMPTS) onFallBackToPassword()
  }, [onConfirmed, onFallBackToPassword, cannotAsk])

  // Ask straight away: the user unlocked their phone to get back to this, so a screen that waits for a second
  // tap is one tap too many. A re-entrant mount (React's dev double-invoke) is caught by `inFlight`.
  //
  // ⚠️ Not in cover mode: there is nothing to ask, and re-running would put the app back where it already is.
  useEffect(() => {
    if (coverOnly) return
    void attempt()
  }, [attempt, coverOnly])

  const attemptsLeft = MAX_ATTEMPTS - attemptsUsed

  return (
    /*
     * `h-dvh` and `my-auto`, for the two reasons `client-version-gate.tsx` documents at length: the *dynamic*
     * viewport is the only one that shrinks with a phone's chrome, and centring with `items-center` on the
     * scroller would push the card's top overflow outside the scrollable region on a landscape phone.
     *
     * `bg-background` and not a translucent scrim: the app behind this must not be readable.
     */
    <div
      role="alertdialog"
      aria-modal="true"
      aria-labelledby="session-lock-gate-title"
      className="fixed inset-0 z-50 flex h-dvh items-start justify-center overflow-y-auto bg-background p-4"
    >
      <Card className="my-auto w-full max-w-md">
        <CardHeader className="space-y-3 text-center">
          <div className="mx-auto flex size-14 items-center justify-center rounded-full bg-primary/10">
            <Lock className="size-7 text-primary" aria-hidden="true" />
          </div>
          <CardTitle id="session-lock-gate-title">
            {coverOnly ? "Session en pause" : "Session verrouillée"}
          </CardTitle>
          {/*
            ⚠️ **No number here.** This said « après 30 minutes d'inactivité », which stopped being true the
            moment the limit began to follow the device: it is 8 h on one the owner has vouched for and 30 min
            on a shared machine, decided by `idleLimitMinutes`. A sentence naming one of them is a second source
            of truth for a value this component is not told, and the version that is wrong is the one shown to
            the practitioner who is least likely to be interrupted — so it names none.
          */}
          <CardDescription>
            {coverOnly ? (
              <>
                Votre session a été mise en pause après une période d&apos;inactivité. Cet appareil est le vôtre :
                reprenez sans mot de passe ni code.
              </>
            ) : (
              <>
                Votre session a été mise en pause après une période d&apos;inactivité. Confirmez votre identité
                pour reprendre exactement où vous en étiez.
              </>
            )}
          </CardDescription>
        </CardHeader>
        <CardContent className="space-y-4">
          {lastOutcome && (
            // `role="status"`, not an error toast: this is the result of the action the user just took, and it
            // has to be readable beside the button that retries it.
            <p role="status" className="text-center text-sm text-destructive">
              {/*
                ⚠️ A cancellation no longer consumes an attempt, so it must not report a count either — saying
                « il vous reste 2 tentatives » after an action that cost nothing is the kind of small lie that
                makes someone stop trusting the rest of the screen.
              */}
              {lastOutcome === "cancelled" ? (
                "Vérification annulée. Réessayez lorsque vous êtes prêt."
              ) : (
                <>
                  La vérification a échoué.{" "}
                  {attemptsLeft === 1
                    ? "Il vous reste une tentative avant la saisie du mot de passe."
                    : `Il vous reste ${attemptsLeft} tentatives avant la saisie du mot de passe.`}
                </>
              )}
            </p>
          )}

          {coverOnly ? (
            // Nothing to verify against, so the button does the only honest thing: uncover the app. The cookie
            // was never cleared, which is what makes one click enough.
            <Button className="min-h-11 w-full" onClick={onConfirmed}>
              Reprendre la session
            </Button>
          ) : (
            <Button className="min-h-11 w-full gap-2" onClick={() => void attempt()} disabled={busy}>
              <Fingerprint className="size-4" aria-hidden="true" />
              {busy ? "Vérification en cours…" : "Déverrouiller"}
            </Button>
          )}

          {/* The way out that is not a failure. Without it the only exit is to fail twice more on purpose. */}
          <Button variant="outline" className="min-h-11 w-full" onClick={onFallBackToPassword} disabled={busy}>
            Se déconnecter
          </Button>
        </CardContent>
      </Card>
    </div>
  )
}
