"use client"

import Link from "next/link"
import { useCallback, useEffect, useRef, useState } from "react"
import { Fingerprint } from "lucide-react"
import { Button } from "@/components/ui/button"
import { Dialog, DialogBody, DialogContent, DialogDescription, DialogHeader, DialogTitle } from "@/components/ui/dialog"
import { BorneBoard, useBorneStatus, type BorneStage } from "@/components/team/borne-board"
import { useClockStatus } from "@/components/team/clock-in-gate"
import { pointageApi } from "@/lib/api/pointage"
import { cn } from "@/lib/utils"

/**
 * One read per page load: the header remounts on every navigation, and whether this device is a borne does not
 * change between two clicks. A failed read means « not a borne » — the entry is a convenience, never the only door.
 */
let borneProbe: Promise<boolean> | null = null
const isThisDeviceABorne = () =>
  (borneProbe ??= pointageApi
    .status()
    .then((status) => status.isBorne && status.enabled)
    .catch(() => false))

/** Forgets the cached answer — after « Ajouter une borne », a claimed borne, or « Retirer ». */
export function forgetBorneProbe() {
  borneProbe = null
}

/**
 * « Pointage » (`pointage-borne` AC-1): on /login and in the top bar, only on a device that is a borne. In the top bar
 * someone is signed in and working, so the tiles open over her page instead of taking the tab away from it.
 */
export function BorneLink({ variant }: { variant: "login" | "header" }) {
  const [isBorne, setIsBorne] = useState(false)
  const [open, setOpen] = useState(false)

  useEffect(() => {
    let cancelled = false
    void isThisDeviceABorne().then((answer) => !cancelled && setIsBorne(answer))
    return () => {
      cancelled = true
    }
  }, [])

  if (!isBorne) return null

  if (variant === "login") {
    return (
      <Button asChild variant="outline" className="min-h-11 w-full gap-2">
        <Link href="/pointage">
          <Fingerprint className="size-4" aria-hidden="true" />
          Pointage
        </Link>
      </Button>
    )
  }

  return (
    <>
      <Button
        variant="ghost"
        size="sm"
        className={cn("touch-target gap-1.5 print:hidden")}
        aria-label="Pointage"
        aria-haspopup="dialog"
        onClick={() => setOpen(true)}
      >
        <Fingerprint className="size-4" aria-hidden="true" />
        <span className="hidden lg:inline">Pointage</span>
      </Button>
      <BorneDialog open={open} onOpenChange={setOpen} />
    </>
  )
}

/** The borne's tiles and pad over the current page; it closes itself once the confirmation has been read. */
function BorneDialog({ open, onOpenChange }: { open: boolean; onOpenChange: (open: boolean) => void }) {
  const { status, failed, load } = useBorneStatus(open)
  const { refresh: refreshClock } = useClockStatus()
  const stage = useRef<BorneStage>("tiles")
  const hold = useRef(false)

  const onStageChange = useCallback((next: BorneStage) => {
    stage.current = next
  }, [])
  const onHoldChange = useCallback((next: boolean) => {
    hold.current = next
  }, [])
  const onFinished = useCallback(() => {
    hold.current = false
    onOpenChange(false)
    // The punch may be her own: the header chip and the lock follow it at once.
    void refreshClock()
  }, [onOpenChange, refreshClock])

  return (
    <Dialog
      open={open}
      onOpenChange={(next) => {
        if (!next && hold.current) return
        onOpenChange(next)
      }}
    >
      <DialogContent
        className="md:max-w-3xl"
        // On the pad, Escape goes back to the tiles (the board's own listener); only the tiles close the dialog.
        onEscapeKeyDown={(e) => stage.current !== "tiles" && e.preventDefault()}
        onInteractOutside={(e) => stage.current !== "tiles" && e.preventDefault()}
      >
        <DialogHeader>
          <DialogTitle>Pointage</DialogTitle>
          <DialogDescription>{status?.isBorne && status.name ? `Borne ${status.name}` : "La borne du cabinet"}</DialogDescription>
        </DialogHeader>
        <DialogBody className="flex flex-col">
          <BorneBoard
            status={status}
            failed={failed}
            reload={load}
            onStageChange={onStageChange}
            onHoldChange={onHoldChange}
            padClassName="max-w-[min(18rem,40dvh)]"
            onFinished={onFinished}
          />
        </DialogBody>
      </DialogContent>
    </Dialog>
  )
}
