"use client"

import { useCallback, useEffect, useRef, useState } from "react"
import { toast } from "sonner"
import {
  Archive, CheckCircle2, CircleSlash, Clock, CloudDownload, HardDrive, OctagonPause, PowerOff, RefreshCw, Server,
  TriangleAlert, XCircle, type LucideIcon,
} from "lucide-react"

import { Button } from "@/components/ui/button"
import { Card, CardContent } from "@/components/ui/card"
import {
  AlertDialog, AlertDialogCancel, AlertDialogContent, AlertDialogDescription, AlertDialogFooter, AlertDialogHeader,
  AlertDialogTitle,
} from "@/components/ui/alert-dialog"
import { LoadFailureNotice } from "@/components/ui/load-failure"
import { STATUS_TONE_CLASS, type StatusTone } from "@/components/ui/status-tone"
import { ApiError } from "@/lib/api/client"
import { RELAY_CARD_ID, relayApi, type RelayStateKey, type RelayStatusDto } from "@/lib/api/relay"
import { showErrorToast } from "@/lib/errors"
import { formatDate, formatFileSize, quoteFr } from "@/lib/format"
import { ZONES, zoneChipClass } from "@/lib/zones"

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
}

/** The sentence ages by the second (« il y a 3 s »), so the card re-reads while it is on screen. */
const REFRESH_MS = 15_000

type Load =
  | { kind: "loading" }
  | { kind: "absent" }
  | { kind: "failed" }
  | { kind: "loaded"; status: RelayStatusDto }

/**
 * « Paramètres → PC de secours » (`clinic-pc-copy` AC-2.1, AC-2.4, AC-8.1) — the cabinet's copy on one of its own PCs:
 * its name, its one state with its sentence, and « Retirer ce PC ». Mounted for an admin only.
 *
 * <p>⚠️ <b>Only a 404 means « not on this deployment »</b> and renders nothing — the subscription screen's rule: any
 * other failure is « Impossible de lire l'état du PC de secours » with « Réessayer », never « Aucun PC de secours »,
 * which would tell an admin the cabinet is unprotected when the truth is that nobody could look (AC-2.4).</p>
 *
 * <p>⚠️ <b>There is no setup button here, deliberately</b>: the spec puts the offer in the Windows app and never in a
 * browser (AC-1.12) — a browser cannot install a server on the PC it runs on.</p>
 */
export function RelayCard() {
  const [load, setLoad] = useState<Load>({ kind: "loading" })
  const [confirmOpen, setConfirmOpen] = useState(false)
  const [retiring, setRetiring] = useState(false)

  const read = useCallback(async (initial: boolean) => {
    try {
      const status = await relayApi.status()
      setLoad({ kind: "loaded", status })
    } catch (err) {
      if (err instanceof ApiError && err.status === 404) {
        setLoad({ kind: "absent" })
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
            retiring={retiring}
          />
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
    </Card>
  )
}

function RelayState({
  status, onRetire, retiring,
}: { status: RelayStatusDto; onRetire: () => void; retiring: boolean }) {
  const look = STATE_LOOK[status.state] ?? STATE_LOOK.none
  const Icon = look.icon

  if (!status.exists) {
    return (
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

        {live && (
          <Button
            variant="outline"
            size="sm"
            className="w-full coarse:min-h-11 sm:w-auto"
            disabled={retiring}
            onClick={onRetire}
          >
            Retirer ce PC
          </Button>
        )}
      </div>

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
