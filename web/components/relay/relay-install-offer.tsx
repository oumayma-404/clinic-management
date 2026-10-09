"use client"

import { useCallback, useEffect, useRef, useState } from "react"
import { toast } from "sonner"
import { CheckCircle2, Loader2, Server } from "lucide-react"

import { Button } from "@/components/ui/button"
import {
  Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle,
} from "@/components/ui/dialog"
import { FormErrorBanner } from "@/components/ui/form-error-banner"
import { StepUpDialog } from "@/components/security/step-up-dialog"
import { authApi } from "@/lib/api/auth"
import { RELAY_PAIRING_STEP_UP, relayApi, type RelayPairingCodeDto } from "@/lib/api/relay"
import { useSession } from "@/lib/auth/session"
import { getErrorMessage } from "@/lib/errors"
import { ZONES, zoneChipClass } from "@/lib/zones"
import {
  alreadyConsideredThisRun, offerSetAside, relayInstallShell, rememberLater, rememberNever, roomRefusal,
} from "./relay-install"

type Phase =
  | { kind: "idle"; error?: string }
  | { kind: "installing" }
  | { kind: "installed"; sentence: string }
  | { kind: "failed"; sentence: string }

/**
 * One setup, from a code the cloud issued to the Windows app's outcome (AC-1.4, AC-1.8, AC-1.11). Shared by the
 * offer (an admin signed in) and « Installer le PC de secours ici… » (an admin's email, password and code), so the two
 * doors cannot handle a refusal differently.
 *
 * ⚠️ **Any outcome but « installed » gives the code back**: Windows' prompt refused, too little room, a failed download
 * — the code was never presented, and without the release the clinic's one place stays taken for an hour, so the
 * offer would vanish from every device (AC-1.10) exactly when AC-1.11 says it stays available.
 *
 * ⚠️ **The outcome becomes a toast only when nothing on screen shows it** (`onScreen`): the install runs for minutes and
 * the person may close the dialog meanwhile, but a banner and a toast saying one thing at once is noise.
 */
export function useRelayInstall(onScreen: () => boolean = () => false) {
  const [phase, setPhase] = useState<Phase>({ kind: "idle" })

  const install = useCallback(async (issue: () => Promise<RelayPairingCodeDto>, freeBytes: number | null) => {
    const shell = relayInstallShell()
    if (!shell) return

    setPhase({ kind: "installing" })
    let dto: RelayPairingCodeDto
    try {
      dto = await issue()
    } catch (err) {
      setPhase({ kind: "idle", error: getErrorMessage(err, "L'installation du PC de secours n'a pas pu démarrer.") })
      return
    }

    const room = roomRefusal(freeBytes, dto.needBytes)
    const outcome: ShellRelayInstallOutcome = room
      ? { outcome: "refused", sentence: room }
      : await shell.installRelay({ code: dto.code, needBytes: dto.needBytes })

    if (outcome.outcome === "installed") {
      setPhase({ kind: "installed", sentence: outcome.sentence })
      if (!onScreen()) toast.success(outcome.sentence)
      return
    }

    void relayApi.releasePairingCode(dto.code).catch(() => undefined)
    setPhase({ kind: "failed", sentence: outcome.sentence })
    if (!onScreen()) toast.error(outcome.sentence)
  }, [onScreen])

  const reset = useCallback(() => setPhase({ kind: "idle" }), [])
  const refuse = useCallback((error: string) => setPhase({ kind: "idle", error }), [])

  return { phase, install, reset, refuse }
}

/** The running and finished states, shared by the dialog and the page. */
export function RelayInstallProgress({ phase }: { phase: Phase }) {
  if (phase.kind === "installing") {
    return (
      <p role="status" className="flex items-start gap-2 text-sm">
        <Loader2 aria-hidden="true" className="mt-0.5 size-4 shrink-0 motion-safe:animate-spin" />
        <span className="min-w-0">
          L&apos;installation se poursuit en arrière-plan. Windows vous demandera une autorisation dans quelques
          minutes.
        </span>
      </p>
    )
  }

  if (phase.kind === "installed") {
    return (
      <p role="status" className="flex items-start gap-2 text-sm font-medium text-success">
        <CheckCircle2 aria-hidden="true" className="mt-0.5 size-4 shrink-0" />
        <span className="min-w-0">{phase.sentence}</span>
      </p>
    )
  }

  return null
}

const BACKGROUND_NOTICE_MS = 4000

interface RelayOfferDialogProps {
  open: boolean
  onOpenChange: (open: boolean) => void
  facts: ShellRelayHostFacts
  /** The room the cloud says a copy needs (status `needBytes`); checked before the code is even asked for. */
  needBytes: number
  /** The start-up offer's two set-asides (AC-1.3). Absent where the person came to install on purpose. */
  onLater?: () => void
  onNever?: () => void
}

/**
 * « Garder une copie du cabinet sur ce PC ? » — an admin signed in on this Windows PC (AC-1.1, AC-1.4). « Oui » asks
 * for the authenticator code, then the Windows app shows its one permission prompt and does the rest.
 */
export function RelayOfferDialog({ open, onOpenChange, facts, needBytes, onLater, onNever }: RelayOfferDialogProps) {
  const openRef = useRef(open)
  useEffect(() => {
    openRef.current = open
  }, [open])
  const onScreen = useCallback(() => openRef.current, [])
  const { phase, install, reset, refuse } = useRelayInstall(onScreen)
  const [stepUpOpen, setStepUpOpen] = useState(false)

  useEffect(() => {
    if (!open && phase.kind !== "installing") reset()
  }, [open, phase.kind, reset])

  // The download and the install take minutes: once they start, say it happens in the background and close. The
  // outcome then arrives as a toast (`onScreen` is false once closed).
  useEffect(() => {
    if (!open || phase.kind !== "installing") return
    const timer = window.setTimeout(() => onOpenChange(false), BACKGROUND_NOTICE_MS)
    return () => window.clearTimeout(timer)
  }, [open, phase.kind, onOpenChange])

  const accept = () => {
    // AC-1.8 before the code: no code is issued, and no place taken, for a PC that cannot hold the copy.
    const room = roomRefusal(facts.freeBytes, needBytes)
    if (room) {
      refuse(room)
      return
    }
    setStepUpOpen(true)
  }

  const startOffer = !!onLater && !!onNever

  return (
    <>
      <Dialog open={open} onOpenChange={onOpenChange}>
        <DialogContent className="md:max-w-lg">
          <DialogHeader>
            <div className="flex items-center gap-2.5">
              <span
                aria-hidden="true"
                className={`flex size-8 shrink-0 items-center justify-center rounded-lg ${zoneChipClass(ZONES.config)}`}
              >
                <Server className="size-4" strokeWidth={1.75} />
              </span>
              <DialogTitle>Garder une copie du cabinet sur ce PC ?</DialogTitle>
            </div>
            <DialogDescription>
              Ce PC gardera une copie du cabinet pour que vous puissiez continuer à travailler quand internet est coupé.
            </DialogDescription>
          </DialogHeader>

          <div className="space-y-4">
            {phase.kind === "idle" && <FormErrorBanner message={phase.error ?? null} />}
            {phase.kind === "failed" && <FormErrorBanner message={phase.sentence} />}
            <RelayInstallProgress phase={phase} />
          </div>

          <DialogFooter>
            {phase.kind === "idle" && startOffer && (
              <>
                <Button variant="ghost" className="coarse:min-h-11" onClick={onNever}>Pas sur ce PC</Button>
                <Button variant="outline" className="coarse:min-h-11" onClick={onLater}>Plus tard</Button>
                <Button className="coarse:min-h-11" onClick={accept}>Oui</Button>
              </>
            )}
            {phase.kind === "idle" && !startOffer && (
              <>
                <Button variant="outline" className="coarse:min-h-11" onClick={() => onOpenChange(false)}>Annuler</Button>
                <Button className="coarse:min-h-11" onClick={accept}>Installer sur ce PC</Button>
              </>
            )}
            {phase.kind === "failed" && (
              <>
                <Button variant="outline" className="coarse:min-h-11" onClick={() => onOpenChange(false)}>Fermer</Button>
                <Button className="coarse:min-h-11" onClick={reset}>Réessayer</Button>
              </>
            )}
            {(phase.kind === "installing" || phase.kind === "installed") && (
              <Button variant="outline" className="coarse:min-h-11" onClick={() => onOpenChange(false)}>Fermer</Button>
            )}
          </DialogFooter>
        </DialogContent>
      </Dialog>

      <StepUpDialog
        open={stepUpOpen}
        onOpenChange={setStepUpOpen}
        action={RELAY_PAIRING_STEP_UP}
        purpose="Vous allez faire de ce PC le PC de secours du cabinet."
        hasTotp
        onConfirmed={(token) =>
          void install(() => relayApi.issuePairingCode(facts.machineName, token), facts.freeBytes)}
      />
    </>
  )
}

/**
 * The offer at the Windows app's start (AC-1.1–1.3, AC-1.10, AC-1.12), mounted in `AppShell`. It renders nothing —
 * not a node — unless every condition holds: the Windows app, an admin, the run's first page, this PC not set aside,
 * a server that offers a PC de secours, a clinic that has none, and this PC's facts readable.
 *
 * ⚠️ **Never over an open form (AC-1.2)**: the reads take a moment, so if anything opened a dialog meanwhile the run's
 * one chance is spent rather than spent on top of it.
 */
const OFFER_WAIT_MS = 120_000

export function RelayStartupOffer() {
  const { user } = useSession()
  const [offer, setOffer] = useState<{ facts: ShellRelayHostFacts; needBytes: number } | null>(null)
  const [open, setOpen] = useState(false)
  const considered = useRef(false)

  useEffect(() => {
    if (considered.current || !user) return
    const shell = relayInstallShell()
    if (!shell) return
    considered.current = true
    if (user.role !== "admin" || alreadyConsideredThisRun() || offerSetAside()) return

    let cancelled = false
    void (async () => {
      try {
        const mode = await authApi.getMode()
        if (mode.relayFeedEnabled !== true || mode.isClinicRelay === true) return
        const status = await relayApi.status()
        if (status.canInstall !== true) return
        const facts = await shell.relayHostFacts()
        if (!facts || cancelled) return
        // The post-visit prompt is often already up at start: wait for it to close rather than lose the run, and give
        // up if a dialog stays open — whatever it is, the offer is not shown over it.
        const giveUpAt = Date.now() + OFFER_WAIT_MS
        while (document.querySelector('[role="dialog"], [role="alertdialog"]')) {
          if (cancelled || Date.now() > giveUpAt) return
          await new Promise((resolve) => window.setTimeout(resolve, 1000))
        }
        if (cancelled) return
        setOffer({ facts, needBytes: status.needBytes ?? 0 })
        setOpen(true)
      } catch {
        // No offer this run: a failed read must not interrupt anybody.
      }
    })()

    return () => {
      cancelled = true
    }
  }, [user])

  if (!offer) return null

  return (
    <RelayOfferDialog
      open={open}
      onOpenChange={setOpen}
      facts={offer.facts}
      needBytes={offer.needBytes}
      onLater={() => {
        rememberLater()
        setOpen(false)
      }}
      onNever={() => {
        rememberNever()
        setOpen(false)
      }}
    />
  )
}
