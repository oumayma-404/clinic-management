"use client"

import { useEffect, useState } from "react"
import Link from "next/link"
import { Monitor, Server } from "lucide-react"

import { Button } from "@/components/ui/button"
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card"
import { Input } from "@/components/ui/input"
import { Label } from "@/components/ui/label"
import { PasswordInput } from "@/components/ui/password-input"
import { FormErrorBanner } from "@/components/ui/form-error-banner"
import { TotpCodeField } from "@/components/security/totp-code-field"
import { RelayInstallProgress, useRelayInstall } from "@/components/relay/relay-install-offer"
import { relayInstallShell } from "@/components/relay/relay-install"
import { authApi } from "@/lib/api/auth"
import { relayApi } from "@/lib/api/relay"
import { CAPABILITY_PROBE_TIMEOUT_MS, withTimeout } from "@/lib/capability-probe"

type Stage =
  | { kind: "checking" }
  | { kind: "no-shell" }
  | { kind: "unavailable" }
  | { kind: "no-facts" }
  | { kind: "ready"; facts: ShellRelayHostFacts }

/**
 * « Installer le PC de secours ici… » (`clinic-pc-copy` AC-1.5) — opened from the Windows app's menu on any PC of the
 * cabinet, so the always-on reception PC can be chosen while a secretary is signed in on it.
 *
 * ⚠️ **Public, and it signs nobody in or out.** The admin's email, password and code go to one endpoint that runs the
 * sign-in's own check and ends the session it opens before answering; whoever is signed in on this PC stays signed in.
 *
 * ⚠️ **In a browser or on Android it says where to go instead (AC-1.12)** — a page cannot install a server on the PC
 * it runs on, and the methods it would need are detected, not assumed.
 */
const alwaysOnScreen = () => true

export default function RelayInstallPage() {
  const [stage, setStage] = useState<Stage>({ kind: "checking" })
  const [email, setEmail] = useState("")
  const [password, setPassword] = useState("")
  const [totpCode, setTotpCode] = useState("")
  // The page never closes: its banner always says how the install ended, so no toast repeats it.
  const { phase, install } = useRelayInstall(alwaysOnScreen)

  useEffect(() => {
    const shell = relayInstallShell()
    if (!shell) {
      setStage({ kind: "no-shell" })
      return
    }

    let cancelled = false
    void (async () => {
      try {
        const mode = await withTimeout(authApi.getMode(), CAPABILITY_PROBE_TIMEOUT_MS)
        if (mode.relayFeedEnabled !== true || mode.isClinicRelay === true) {
          if (!cancelled) setStage({ kind: "unavailable" })
          return
        }
      } catch {
        // A failed probe falls through to the form: the endpoint answers 404 itself where there is no PC de secours.
      }
      const facts = await shell.relayHostFacts()
      if (!cancelled) setStage(facts ? { kind: "ready", facts } : { kind: "no-facts" })
    })()

    return () => {
      cancelled = true
    }
  }, [])

  // The code is spent by the sign-in whatever came of it; a retry needs the next one.
  useEffect(() => {
    if (phase.kind !== "installing") setTotpCode("")
  }, [phase.kind])

  const submit = (e: React.FormEvent) => {
    e.preventDefault()
    if (stage.kind !== "ready" || phase.kind === "installing") return
    const facts = stage.facts
    void install(
      () => relayApi.issuePairingCodeWithCredentials({ email: email.trim(), password, totpCode, label: facts.machineName }),
      facts.freeBytes,
    )
  }

  const busy = phase.kind === "installing"

  return (
    // `my-auto` on the child, never `items-center` on the scroller: the top of a tall card stays reachable.
    <div className="flex min-h-dvh justify-start overflow-y-auto bg-background p-4 sm:p-6">
      <div className="mx-auto my-auto w-full max-w-md">
        <Card>
          <CardHeader className="space-y-1">
            <div className="flex items-center gap-2.5">
              <span aria-hidden="true" className="flex size-8 shrink-0 items-center justify-center rounded-lg bg-primary/10 text-primary">
                {stage.kind === "no-shell" ? <Monitor className="size-4" /> : <Server className="size-4" />}
              </span>
              <CardTitle className="text-xl font-bold">Installer le PC de secours ici</CardTitle>
            </div>
            {stage.kind === "ready" && phase.kind !== "installed" && (
              <CardDescription>
                Ce PC gardera une copie du cabinet et prendra le relais si internet coupe. Un administrateur du cabinet
                confirme avec son e-mail, son mot de passe et son code.
              </CardDescription>
            )}
          </CardHeader>

          <CardContent className="space-y-4">
            {stage.kind === "checking" && <div aria-hidden="true" className="h-40 animate-pulse rounded-lg bg-muted" />}

            {stage.kind === "no-shell" && (
              <p className="text-sm text-muted-foreground" role="status">
                Installer le PC de secours depuis l&apos;application Windows d&apos;un PC du cabinet.
              </p>
            )}

            {stage.kind === "unavailable" && (
              <p className="text-sm text-muted-foreground" role="status">
                Ce serveur ne propose pas de PC de secours.
              </p>
            )}

            {stage.kind === "no-facts" && (
              <p className="text-sm text-muted-foreground" role="status">
                L&apos;application n&apos;a pas pu lire ce PC. Fermez-la, rouvrez-la, puis recommencez.
              </p>
            )}

            {stage.kind === "ready" && phase.kind !== "installed" && (
              <form onSubmit={submit} className="space-y-4">
                <FormErrorBanner
                  message={phase.kind === "failed" ? phase.sentence : phase.kind === "idle" ? phase.error ?? null : null}
                />
                <div className="space-y-2">
                  <Label htmlFor="relay-email">E-mail de l&apos;administrateur</Label>
                  <Input
                    id="relay-email"
                    type="email"
                    autoComplete="off"
                    value={email}
                    disabled={busy}
                    onChange={(e) => setEmail(e.target.value)}
                    required
                  />
                </div>
                <div className="space-y-2">
                  <Label htmlFor="relay-password">Mot de passe</Label>
                  <PasswordInput
                    id="relay-password"
                    autoComplete="off"
                    value={password}
                    disabled={busy}
                    onChange={(e) => setPassword(e.target.value)}
                    required
                  />
                </div>
                <TotpCodeField id="relay-totp" value={totpCode} onChange={setTotpCode} disabled={busy} />

                <RelayInstallProgress phase={phase} />

                <Button
                  type="submit"
                  className="min-h-11 w-full"
                  disabled={busy || !email.trim() || !password || totpCode.length < 6}
                >
                  {busy ? "Installation…" : "Installer sur ce PC"}
                </Button>
              </form>
            )}

            {phase.kind === "installed" && <RelayInstallProgress phase={phase} />}

            {stage.kind !== "checking" && (
              <Button asChild variant="outline" className="min-h-11 w-full">
                <Link href="/">Retour à l&apos;application</Link>
              </Button>
            )}
          </CardContent>
        </Card>
      </div>
    </div>
  )
}
