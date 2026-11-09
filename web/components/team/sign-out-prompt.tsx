"use client"

import { useCallback, useEffect, useRef, useState } from "react"
import { CheckCircle2 } from "lucide-react"

import { Button } from "@/components/ui/button"
import {
  Dialog,
  DialogBody,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog"
import { PunchFlow } from "@/components/team/punch-flow"
import { atBornes, bornesPhrase, clockTime, useClockStatus, useFutureInstant } from "@/components/team/clock-in-gate"
import { useSession } from "@/lib/auth/session"

/** How long « Départ enregistré » stays readable before she is signed out, or the dialog closes. */
const CONFIRMATION_MS = 2000

/**
 * « Pointez votre départ » (`pointage-obligatoire` AC-7): « Se déconnecter » while she is present. On a borne the pad
 * opens right here, beside « Se déconnecter sans pointer »; elsewhere the title names the borne and « Se déconnecter »
 * is the way out — signing out is never held hostage to a punch.
 */
export function SignOutPrompt({ open, onOpenChange }: { open: boolean; onOpenChange: (open: boolean) => void }) {
  return <DepartureDialog open={open} onOpenChange={onOpenChange} signOut />
}

/**
 * Her pointage from the header chip or a « Départ non pointé » row: the departure pad on a borne while she is present,
 * otherwise where to point, by name.
 */
export function ClockDialog({ open, onOpenChange }: { open: boolean; onOpenChange: (open: boolean) => void }) {
  return <DepartureDialog open={open} onOpenChange={onOpenChange} signOut={false} />
}

function DepartureDialog({
  open,
  onOpenChange,
  signOut,
}: {
  open: boolean
  onOpenChange: (open: boolean) => void
  /** Opened by « Se déconnecter »: every way out of it signs her out. */
  signOut: boolean
}) {
  const { logout } = useSession()
  const { status, refresh } = useClockStatus()
  const blockedUntil = useFutureInstant(status?.schemaLockedUntil)
  const present = Boolean(status?.presentSince)
  const onBorne = status?.canClockOutHere === true && status.hasSchema
  const canPunchHere = onBorne && present && !blockedUntil
  // The confirmation lives in the dialog: sign-out reloads the page, so a toast would die before it is read.
  const [departedAt, setDepartedAt] = useState<string | null>(null)
  // A departure in flight holds both footer buttons: « Annuler » would drop its answer after it was saved.
  const [punching, setPunching] = useState(false)
  const signingOut = useRef(false)

  const endSession = useCallback(() => {
    if (signingOut.current) return
    signingOut.current = true
    logout()
  }, [logout])

  const finish = useCallback(() => {
    if (signOut) {
      endSession()
      return
    }
    onOpenChange(false)
    void refresh()
  }, [signOut, endSession, onOpenChange, refresh])

  // A fresh opening starts from her current status, never from the last confirmation (kept through the close fade).
  useEffect(() => {
    if (open) setDepartedAt(null)
  }, [open])

  useEffect(() => {
    if (!departedAt) return
    const id = window.setTimeout(finish, CONFIRMATION_MS)
    return () => window.clearTimeout(id)
  }, [departedAt, finish])

  const where = status ? atBornes(status.bornes) : null
  const title = departedAt
    ? "Départ enregistré"
    : !present && status?.leftAt
      ? `Départ pointé à ${status.leftAt}`
      : onBorne || !where
        ? "Pointez votre départ"
        : `Pointez votre départ ${where}`
  const detail = departedAt
    ? `À ${departedAt}.`
    : present
      ? `Arrivée pointée à ${status?.presentSince}.`
      : status
        ? bornesPhrase(status.bornes)
        : ""

  return (
    <Dialog
      open={open}
      onOpenChange={(next) => {
        if (!next && punching) return
        // Closing during the confirmation finishes what she asked for.
        if (!next && departedAt) finish()
        else onOpenChange(next)
      }}
    >
      <DialogContent className="md:max-w-md">
        <p role="status" className="sr-only">
          {departedAt ? `Départ enregistré à ${departedAt}.` : ""}
        </p>
        <DialogHeader>
          <DialogTitle>{title}</DialogTitle>
          <DialogDescription>
            {detail}
            {!departedAt && present && !where && !canPunchHere && status ? ` ${bornesPhrase(status.bornes)}` : ""}
          </DialogDescription>
        </DialogHeader>
        {departedAt ? (
          <DialogBody>
            <div className="py-6">
              <CheckCircle2 className="mx-auto block size-12 text-warning-ink" aria-hidden="true" />
            </div>
          </DialogBody>
        ) : canPunchHere && status ? (
          <DialogBody>
            <PunchFlow
              userId={status.userId}
              name={status.name}
              action="departure"
              onDone={(result) => setDepartedAt(result.at)}
              onBusyChange={setPunching}
              showHeading={false}
              padClassName="max-w-[min(18rem,40dvh)]"
            />
          </DialogBody>
        ) : onBorne && present && blockedUntil ? (
          <p role="status" className="text-sm font-medium text-destructive">
            Schéma bloqué jusqu&apos;à {clockTime(blockedUntil)}
          </p>
        ) : null}
        {!departedAt && (
          <DialogFooter>
            <Button variant="outline" disabled={punching} onClick={() => onOpenChange(false)}>
              {signOut ? "Annuler" : "Fermer"}
            </Button>
            {signOut && (
              <Button variant={onBorne ? "ghost" : "default"} disabled={punching} onClick={endSession}>
                {onBorne ? "Se déconnecter sans pointer" : "Se déconnecter"}
              </Button>
            )}
          </DialogFooter>
        )}
      </DialogContent>
    </Dialog>
  )
}
