"use client"

import { useCallback, useEffect, useRef, useState } from "react"
import Link from "next/link"
import { toast } from "sonner"
import {
  Archive, CheckCircle2, CircleSlash, Clock, ClockAlert, CloudDownload, HardDrive, OctagonPause, PowerOff, RefreshCw, Server,
  TriangleAlert, XCircle, type LucideIcon,
} from "lucide-react"

import { Button } from "@/components/ui/button"
import { Card, CardContent } from "@/components/ui/card"
import {
  AlertDialog, AlertDialogCancel, AlertDialogContent, AlertDialogDescription, AlertDialogFooter, AlertDialogHeader,
  AlertDialogTitle,
} from "@/components/ui/alert-dialog"
import { LoadFailureNotice } from "@/components/ui/load-failure"
import { StepUpDialog } from "@/components/security/step-up-dialog"
import { useSession } from "@/lib/auth/session"
import { STATUS_TONE_CLASS, type StatusTone } from "@/components/ui/status-tone"
import { ApiError } from "@/lib/api/client"
import {
  RELAY_CARD_ID, RELAY_ERASE_STEP_UP, RELAY_LOST_STEP_UP, RELAY_RECLAIM_STEP_UP, relayApi, type RelayLocalStatusDto,
  type RelayStateKey, type RelayStatusDto,
} from "@/lib/api/relay"
import { showErrorToast } from "@/lib/errors"
import { formatDate, formatFileSize, quoteFr } from "@/lib/format"
import { ZONES, zoneChipClass } from "@/lib/zones"
import { relayInstallShell } from "./relay-install"
import { RelayOfferDialog } from "./relay-install-offer"

/** The state shows as words and an icon, never a colour alone (FR-2). Exhaustive, so a new state is a `tsc` error. */
const STATE_LOOK: Record<RelayStateKey, { icon: LucideIcon; tone: StatusTone }> = {
  none: { icon: Server, tone: "neutral" },
  installing: { icon: CloudDownload, tone: "pending" },
  "install-failed": { icon: XCircle, tone: "negative" },
  abandoned: { icon: CircleSlash, tone: "neutral" },
  ready: { icon: CheckCircle2, tone: "positive" },
  late: { icon: Clock, tone: "active" },
  off: { icon: PowerOff, tone: "negative" },
  updating: { icon: RefreshCw, tone: "pending" },
  "disk-nearly-full": { icon: HardDrive, tone: "active" },
  mismatch: { icon: TriangleAlert, tone: "negative" },
  retired: { icon: Archive, tone: "neutral" },
  stopped: { icon: OctagonPause, tone: "negative" },
  "clock-wrong": { icon: ClockAlert, tone: "negative" },
}

/** The sentence ages by the second (« il y a 3 s »), so the card re-reads while it is on screen. */
const REFRESH_MS = 15_000

type Load =
  | { kind: "loading" }
  | { kind: "absent" }
  | { kind: "failed" }
  | { kind: "loaded"; status: RelayStatusDto }
  /** Opened on the PC de secours itself (AC-8.1): the cloud's status does not exist there, its own does. */
  | { kind: "local"; local: RelayLocalStatusDto }

/**
 * « Paramètres → PC de secours » (`clinic-pc-copy` AC-2.1, AC-2.4, AC-8.1) — the cabinet's copy on one of its own PCs:
 * its name, its one state with its sentence, and « Retirer ce PC ». Mounted for an admin only.
 *
 * <p>⚠️ <b>Only a 404 means « not on this deployment »</b> and renders nothing — the subscription screen's rule: any
 * other failure is « Impossible de lire l'état du PC de secours » with « Réessayer », never « Aucun PC de secours »,
 * which would tell an admin the cabinet is unprotected when the truth is that nobody could look (AC-2.4).</p>
 *
 * <p>⚠️ <b>« Installer sur ce PC » appears in the Windows app only</b>, where the offer lives (AC-1.12) — a browser cannot
 * install a server on the PC it runs on, so there the card says where to go instead.</p>
 */
export function RelayCard() {
  const [load, setLoad] = useState<Load>({ kind: "loading" })
  const [confirmOpen, setConfirmOpen] = useState(false)
  const [retiring, setRetiring] = useState(false)

  // Read after mount: the bridge is a window global, so the server render has to assume a browser.
  const [inWindowsApp, setInWindowsApp] = useState(false)
  useEffect(() => setInWindowsApp(relayInstallShell() !== null), [])
  const [installOffer, setInstallOffer] = useState<{ facts: ShellRelayHostFacts; needBytes: number } | null>(null)
  const [installOpen, setInstallOpen] = useState(false)
  const [readingPc, setReadingPc] = useState(false)

  const openInstall = async (needBytes: number) => {
    setReadingPc(true)
    const facts = await relayInstallShell()?.relayHostFacts()
    setReadingPc(false)
    if (!facts) {
      toast.error("L'application n'a pas pu lire ce PC. Fermez-la, rouvrez-la, puis recommencez.")
      return
    }
    setInstallOffer({ facts, needBytes })
    setInstallOpen(true)
  }

  const read = useCallback(async (initial: boolean) => {
    try {
      const status = await relayApi.status()
      setLoad({ kind: "loaded", status })
    } catch (err) {
      if (err instanceof ApiError && err.status === 404) {
        // No change feed here: either this IS a PC de secours (it has its own status), or there is none at all.
        try {
          setLoad({ kind: "local", local: await relayApi.local() })
        } catch (localErr) {
          if (localErr instanceof ApiError && localErr.status === 404) {
            setLoad({ kind: "absent" })
          } else if (initial) {
            setLoad({ kind: "failed" })
          }
        }
      } else if (initial) {
        setLoad({ kind: "failed" })
      }
      // A failed background re-read keeps the last state on screen: the next one, 15 s away, will say.
    }
  }, [])

  useEffect(() => {
    void read(true)
    const timer = window.setInterval(() => void read(false), REFRESH_MS)
    return () => window.clearInterval(timer)
  }, [read])

  // A bell row opens « /settings#pc-de-secours », but the page mounts this card only after its own read, so the
  // browser's hash scroll has already happened by then — scroll once ourselves when the card first has its content.
  const scrolled = useRef(false)
  useEffect(() => {
    if (scrolled.current || load.kind === "loading" || window.location.hash !== `#${RELAY_CARD_ID}`) return
    scrolled.current = true
    document.getElementById(RELAY_CARD_ID)?.scrollIntoView({ block: "start" })
  }, [load.kind])

  // « Déclarer perdu ou volé » (AC-8.4): explained first, then confirmed with an authenticator code.
  const { logout } = useSession()
  const [lostConfirmOpen, setLostConfirmOpen] = useState(false)
  const [lostStepUpOpen, setLostStepUpOpen] = useState(false)
  const [declaring, setDeclaring] = useState(false)

  const declareLost = async (stepUpToken: string) => {
    setDeclaring(true)
    try {
      await relayApi.declareLost(stepUpToken)
      // Every session of the cabinet ended with it, this one included — say so, then go to the sign-in screen.
      toast.success("PC déclaré perdu ou volé. Reconnectez-vous : un nouveau mot de passe vous sera demandé.")
      window.setTimeout(() => logout(), 2500)
    } catch (err) {
      showErrorToast(err, "Le PC n'a pas pu être déclaré perdu ou volé.")
      setDeclaring(false)
    }
  }

  // « Reprendre la main » (US-7): AC-7.1's warning first, then an authenticator code.
  const [reclaimConfirmOpen, setReclaimConfirmOpen] = useState(false)
  const [reclaimStepUpOpen, setReclaimStepUpOpen] = useState(false)
  const [reclaiming, setReclaiming] = useState(false)

  const reclaim = async (stepUpToken: string) => {
    setReclaiming(true)
    try {
      const status = await relayApi.reclaim(stepUpToken)
      setLoad({ kind: "loaded", status })
      toast.success("Le cloud a repris la main : le cabinet peut de nouveau enregistrer.")
    } catch (err) {
      showErrorToast(err, "Le cloud n'a pas pu reprendre la main.")
    } finally {
      setReclaiming(false)
    }
  }

  // « Effacer la copie » on a retired PC (AC-8.2): explained first, then confirmed with an authenticator code.
  const [eraseConfirmOpen, setEraseConfirmOpen] = useState(false)
  const [eraseStepUpOpen, setEraseStepUpOpen] = useState(false)
  const [erasing, setErasing] = useState(false)

  const eraseLocal = async (stepUpToken: string) => {
    setErasing(true)
    try {
      await relayApi.eraseLocal(stepUpToken)
      // The accounts went with the copy, this one included: say so, then the sign-in screen.
      toast.success("Copie effacée de ce PC. Le cabinet garde tout sur le cloud.")
      window.setTimeout(() => logout(), 2500)
    } catch (err) {
      showErrorToast(err, "La copie n'a pas pu être effacée.")
      setErasing(false)
    }
  }

  const retire = async () => {
    setRetiring(true)
    try {
      const status = await relayApi.retire()
      setLoad({ kind: "loaded", status })
      setConfirmOpen(false)
      toast.success("PC de secours retiré.")
    } catch (err) {
      showErrorToast(err, "Le PC de secours n'a pas pu être retiré.")
    } finally {
      setRetiring(false)
    }
  }

  if (load.kind === "absent") {
    return null
  }

  return (
    // The bell's « PC de secours » rows land here (`RELAY_CARD_ID`).
    <Card id={RELAY_CARD_ID} className="scroll-mt-4">
      <CardContent className="space-y-3 p-4">
        <div className="flex items-center gap-2.5">
          <span
            aria-hidden="true"
            className={`flex size-8 shrink-0 items-center justify-center rounded-lg ${zoneChipClass(ZONES.config)}`}
          >
            <Server className="size-4" strokeWidth={1.75} />
          </span>
          <p className="min-w-0 text-sm font-medium">PC de secours</p>
        </div>

        {load.kind === "loading" && (
          <div aria-hidden="true" className="h-14 animate-pulse rounded-lg bg-muted" />
        )}

        {load.kind === "failed" && (
          <LoadFailureNotice
            message="Impossible de lire l'état du PC de secours."
            onRetry={() => {
              setLoad({ kind: "loading" })
              void read(true)
            }}
          />
        )}

        {load.kind === "loaded" && (
          <RelayState
            status={load.status}
            onRetire={() => setConfirmOpen(true)}
            onDeclareLost={() => setLostConfirmOpen(true)}
            onReclaim={() => setReclaimConfirmOpen(true)}
            reclaiming={reclaiming}
            onInstall={inWindowsApp && load.status.canInstall ? () => void openInstall(load.status.needBytes ?? 0) : undefined}
            installing={readingPc}
            retiring={retiring || declaring}
          />
        )}

        {load.kind === "local" && (
          <div className="flex flex-wrap items-center justify-between gap-3">
            <p
              role="status"
              className={`inline-flex max-w-full items-center gap-1.5 rounded-md px-2 py-1 text-xs font-medium ${STATUS_TONE_CLASS[load.local.retired ? "neutral" : "positive"]}`}
            >
              {load.local.retired
                ? <Archive aria-hidden="true" className="size-3.5 shrink-0" />
                : <CheckCircle2 aria-hidden="true" className="size-3.5 shrink-0" />}
              <span className="min-w-0">{load.local.sentence}</span>
            </p>
            {/* Only once retired: before that, this copy is the cabinet's spare. */}
            {load.local.retired && (
              <Button
                variant="outline"
                size="sm"
                className="w-full text-destructive coarse:min-h-11 sm:w-auto"
                disabled={erasing}
                onClick={() => setEraseConfirmOpen(true)}
              >
                {erasing ? "Effacement…" : "Effacer la copie"}
              </Button>
            )}
          </div>
        )}
      </CardContent>

      <AlertDialog open={confirmOpen} onOpenChange={(open) => { if (!open && !retiring) setConfirmOpen(false) }}>
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>
              Retirer {load.kind === "loaded" && load.status.label ? quoteFr(load.status.label) : "le PC de secours"} ?
            </AlertDialogTitle>
            <AlertDialogDescription>
              La copie s&apos;arrête et le cabinet pourra installer un autre PC de secours. Ce qui est déjà copié reste
              sur ce PC.
            </AlertDialogDescription>
            {/* AC-8.6: pressed while the cabinet is locked, retiring also takes the cloud back. */}
            {load.kind === "loaded" && load.status.cloudLocked && load.status.reclaimWarning && (
              <p className="text-sm font-medium text-destructive">{load.status.reclaimWarning}</p>
            )}
          </AlertDialogHeader>
          <AlertDialogFooter>
            <AlertDialogCancel disabled={retiring} className="coarse:min-h-11">Annuler</AlertDialogCancel>
            {/* A plain Button, not AlertDialogAction: that one closes the dialog even when the call fails. */}
            <Button variant="destructive" disabled={retiring} className="coarse:min-h-11" onClick={() => void retire()}>
              {retiring ? "Retrait…" : "Retirer ce PC"}
            </Button>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>

      <AlertDialog open={lostConfirmOpen} onOpenChange={setLostConfirmOpen}>
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>
              Déclarer {load.kind === "loaded" && load.status.label ? quoteFr(load.status.label) : "le PC de secours"} perdu
              ou volé ?
            </AlertDialogTitle>
            <AlertDialogDescription>
              Le PC est retiré. Comme il contenait les comptes du cabinet, chaque compte devra choisir un nouveau mot de
              passe, et un nouvel authentificateur s&apos;il en avait un. Tout le monde, vous compris, sera déconnecté.
            </AlertDialogDescription>
            {load.kind === "loaded" && load.status.cloudLocked && load.status.reclaimWarning && (
              <p className="text-sm font-medium text-destructive">{load.status.reclaimWarning}</p>
            )}
          </AlertDialogHeader>
          <AlertDialogFooter>
            <AlertDialogCancel className="coarse:min-h-11">Annuler</AlertDialogCancel>
            <Button
              variant="destructive"
              className="coarse:min-h-11"
              onClick={() => {
                setLostConfirmOpen(false)
                setLostStepUpOpen(true)
              }}
            >
              Continuer
            </Button>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>

      <AlertDialog open={eraseConfirmOpen} onOpenChange={setEraseConfirmOpen}>
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>Effacer la copie du cabinet de ce PC ?</AlertDialogTitle>
            <AlertDialogDescription>
              Tous les dossiers, fiches, documents et fichiers du cabinet sont effacés de ce PC, ainsi que ses comptes :
              personne ne pourra plus s&apos;y connecter. Le cloud garde tout. Cette action est irréversible.
            </AlertDialogDescription>
          </AlertDialogHeader>
          <AlertDialogFooter>
            <AlertDialogCancel className="coarse:min-h-11">Annuler</AlertDialogCancel>
            <Button
              variant="destructive"
              className="coarse:min-h-11"
              onClick={() => {
                setEraseConfirmOpen(false)
                setEraseStepUpOpen(true)
              }}
            >
              Continuer
            </Button>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>

      {installOffer && (
        <RelayOfferDialog
          open={installOpen}
          onOpenChange={(open) => {
            setInstallOpen(open)
            if (!open) void read(false)
          }}
          facts={installOffer.facts}
          needBytes={installOffer.needBytes}
        />
      )}

      <AlertDialog open={reclaimConfirmOpen} onOpenChange={setReclaimConfirmOpen}>
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>Reprendre la main sur le cloud ?</AlertDialogTitle>
            <AlertDialogDescription>
              {load.kind === "loaded" && load.status.reclaimWarning}
            </AlertDialogDescription>
          </AlertDialogHeader>
          <AlertDialogFooter>
            <AlertDialogCancel className="coarse:min-h-11">Annuler</AlertDialogCancel>
            <Button
              variant="destructive"
              className="coarse:min-h-11"
              onClick={() => {
                setReclaimConfirmOpen(false)
                setReclaimStepUpOpen(true)
              }}
            >
              Continuer
            </Button>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>

      <StepUpDialog
        open={reclaimStepUpOpen}
        onOpenChange={setReclaimStepUpOpen}
        action={RELAY_RECLAIM_STEP_UP}
        purpose="Vous allez reprendre la main sur le cloud : ce que le cabinet a enregistré sur le PC de secours n'y partira pas."
        hasTotp
        onConfirmed={(token) => void reclaim(token)}
      />

      <StepUpDialog
        open={eraseStepUpOpen}
        onOpenChange={setEraseStepUpOpen}
        action={RELAY_ERASE_STEP_UP}
        purpose="Vous allez effacer de ce PC toute la copie du cabinet."
        hasTotp
        onConfirmed={(token) => void eraseLocal(token)}
      />

      <StepUpDialog
        open={lostStepUpOpen}
        onOpenChange={setLostStepUpOpen}
        action={RELAY_LOST_STEP_UP}
        purpose="Vous allez déclarer ce PC perdu ou volé : chaque compte du cabinet devra choisir un nouveau mot de passe."
        hasTotp
        onConfirmed={(token) => void declareLost(token)}
      />
    </Card>
  )
}

function RelayState({
  status, onRetire, onDeclareLost, onReclaim, onInstall, installing, retiring, reclaiming,
}: {
  status: RelayStatusDto
  onRetire: () => void
  onDeclareLost: () => void
  /** US-7: offered only while the cabinet's saves are refused on the cloud. */
  onReclaim: () => void
  reclaiming: boolean
  /** Present in the Windows app while a PC de secours may be set up (AC-1.10); absent everywhere else. */
  onInstall?: () => void
  installing: boolean
  retiring: boolean
}) {
  const look = STATE_LOOK[status.state] ?? STATE_LOOK.none
  const Icon = look.icon

  const installButton = onInstall && (
    <Button size="sm" className="grow basis-36 coarse:min-h-11 sm:grow-0" disabled={installing} onClick={onInstall}>
      {installing ? "Lecture du PC…" : "Installer sur ce PC…"}
    </Button>
  )

  if (!status.exists) {
    return installButton ? (
      <div className="flex flex-wrap items-center justify-between gap-3">
        <p className="min-w-0 basis-48 grow text-sm text-muted-foreground">Aucun PC de secours.</p>
        <div className="flex w-full sm:w-auto">{installButton}</div>
      </div>
    ) : (
      <p className="text-sm text-muted-foreground">
        Aucun PC de secours. Il s&apos;installe depuis l&apos;application Windows, sur un PC du cabinet.
      </p>
    )
  }

  const live = status.state !== "retired" && status.state !== "abandoned"
  const facts: { label: string; value: string }[] = []
  if (live && status.filesTotal > 0) {
    facts.push({ label: "Fichiers copiés", value: `${status.filesCopied} sur ${status.filesTotal}` })
  }
  if (live && status.diskFreeBytes != null) {
    facts.push({ label: "Disque libre", value: formatFileSize(status.diskFreeBytes) })
  }
  if (live && status.lanAddresses.length > 0) {
    facts.push({ label: "Adresse", value: status.lanAddresses.join(", ") })
  }
  if (status.pairedAtUtc) {
    facts.push({ label: "Installé le", value: formatDate(status.pairedAtUtc) })
  }

  return (
    <div className="space-y-3">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div className="min-w-0 basis-48 grow space-y-1">
          <p className="truncate text-sm font-semibold" title={status.label ?? undefined}>
            {status.label ?? "PC de secours"}
          </p>
          <p
            role="status"
            className={`inline-flex max-w-full items-center gap-1.5 rounded-md px-2 py-1 text-xs font-medium ${STATUS_TONE_CLASS[look.tone]}`}
          >
            <Icon aria-hidden="true" className={`size-3.5 shrink-0 ${status.state === "updating" ? "motion-safe:animate-spin" : ""}`} />
            <span className="min-w-0">{status.sentence}</span>
          </p>
        </div>

        <div className="flex w-full flex-wrap gap-2 sm:w-auto">
          {/* A retired or abandoned PC leaves the place free: the next one can be set up from here. */}
          {installButton}
          {live && (
            <Button
              variant="outline"
              size="sm"
              className="grow basis-36 coarse:min-h-11 sm:grow-0"
              disabled={retiring}
              onClick={onRetire}
            >
              Retirer ce PC
            </Button>
          )}
          {/* A PC that was ever installed held the cabinet's accounts — retired already or not (EC-14). */}
          {status.pairedAtUtc && !status.lostOrStolen && (
            <Button
              variant="outline"
              size="sm"
              className="grow basis-36 text-destructive coarse:min-h-11 sm:grow-0"
              disabled={retiring}
              onClick={onDeclareLost}
            >
              Déclarer perdu ou volé
            </Button>
          )}
        </div>
      </div>

      {/* US-7, AC-6.4: why the cabinet cannot record on the cloud now, and the one way out an admin has. */}
      {status.cloudLocked && status.lockSentence && (
        <div className="flex flex-wrap items-center justify-between gap-3 rounded-lg border border-destructive/30 p-3">
          <p role="status" className="min-w-0 basis-56 grow text-sm font-medium text-destructive">
            {status.lockSentence}
          </p>
          <Button
            variant="destructive"
            size="sm"
            className="grow basis-36 coarse:min-h-11 sm:grow-0"
            disabled={reclaiming || retiring}
            onClick={onReclaim}
          >
            {reclaiming ? "Reprise…" : "Reprendre la main"}
          </Button>
        </div>
      )}

      {/* D18 / US-7: what a cut left behind — the cloud's changes to check, an overruled PC's records to enter again. */}
      {((status.reviewPending ?? 0) > 0 || (status.reEnterPending ?? 0) > 0) && (
        <div className="flex flex-wrap gap-2">
          {(status.reEnterPending ?? 0) > 0 && (
            <Button asChild variant="outline" size="sm" className="grow basis-44 coarse:min-h-11 sm:grow-0">
              <Link href="/settings/pc-de-secours?liste=a-reprendre">À reprendre · {status.reEnterPending}</Link>
            </Button>
          )}
          {(status.reviewPending ?? 0) > 0 && (
            <Button asChild variant="outline" size="sm" className="grow basis-44 coarse:min-h-11 sm:grow-0">
              <Link href="/settings/pc-de-secours">Modifications à vérifier · {status.reviewPending}</Link>
            </Button>
          )}
        </div>
      )}

      {status.state === "installing" && (
        <div
          role="progressbar"
          aria-label="Avancement de la première copie"
          aria-valuemin={0}
          aria-valuemax={100}
          aria-valuenow={status.seedPercent ?? 0}
          className="h-1.5 overflow-hidden rounded-full bg-muted"
        >
          <div className="h-full rounded-full bg-primary transition-[width]" style={{ width: `${status.seedPercent ?? 0}%` }} />
        </div>
      )}

      {facts.length > 0 && (
        <dl className="grid gap-x-4 gap-y-1 text-xs sm:grid-cols-2">
          {facts.map((f) => (
            <div key={f.label} className="flex min-w-0 gap-1.5">
              <dt className="shrink-0 text-muted-foreground">{f.label}</dt>
              <dd className="min-w-0 break-words font-medium">{f.value}</dd>
            </div>
          ))}
        </dl>
      )}
    </div>
  )
}
