"use client"

import { createContext, useCallback, useContext, useEffect, useMemo, useRef, useState, type ReactNode } from "react"
import { usePathname } from "next/navigation"
import Link from "next/link"
import { format } from "date-fns"
import { toast } from "sonner"
import * as DialogPrimitive from "@radix-ui/react-dialog"
import { CalendarDays, Fingerprint, LogOut, RotateCcw } from "lucide-react"

import { Button } from "@/components/ui/button"
import { forgetBorneProbe } from "@/components/team/borne-link"
import { PunchFlow } from "@/components/team/punch-flow"
import { SchemaSetter } from "@/components/team/schema-setter"
import { onClockInRequired } from "@/lib/api/client"
import { quoteFr } from "@/lib/format"
import { pointageApi, type ClockStatusDto } from "@/lib/api/pointage"
import { useSession } from "@/lib/auth/session"
import { useUserStatus } from "@/lib/hooks/use-user-status"
import { isChromeLessPath } from "@/lib/nav"
import { RealtimeResource } from "@/lib/realtime/clinic-hub"
import { useClinicRealtime } from "@/lib/realtime/use-clinic-realtime"

/** Opening time arrives with no event to say so: a signed-in screen re-reads this often while it is visible. */
const REFRESH_MS = 60_000

/** Where the schéma card lives — the lock stands aside there while she has none. */
const SCHEMA_SETUP_PATHS = ["/mon-profil", "/mes-presences"]

/** « Mes congés » stays open while she is locked (AC-3): ill at home, she can still ask for today off. */
const LEAVE_PATH = "/conges"

interface ClockStatusState {
  /** Null until read, and while the pointage is off for this cabinet. */
  status: ClockStatusDto | null
  /** The lock is on screen — chat notices and the other popups stand down beneath it. */
  covering: boolean
  refresh: () => Promise<void>
}

const ClockStatusContext = createContext<ClockStatusState>({ status: null, covering: false, refresh: async () => {} })

/** Her clock status — « Pointer votre départ ? » at sign-out reads the same answer the lock does. */
export function useClockStatus(): ClockStatusState {
  return useContext(ClockStatusContext)
}

/**
 * « Pointez votre arrivée » (`pointage-obligatoire` AC-1, AC-2): reads the server's one lock rule and, while it holds,
 * covers every screen of her account but « Mes congés ». It lifts by itself on the `team` broadcast her punch sends —
 * no reload, no button — and on `clinics` (the lock switch, the opening hours). Also re-reads on a 423 (a save refused
 * before her arrival), on focus, and every minute (opening time).
 */
export function ClockStatusProvider({ children }: { children: ReactNode }) {
  const { user, locked: sessionLocked } = useSession()
  const { data: userStatus } = useUserStatus()
  const pathname = usePathname()
  const enabled = Boolean(user) && userStatus?.clinic?.attendanceEnabled === true
  const [status, setStatus] = useState<ClockStatusDto | null>(null)
  const current = useRef<Promise<void> | null>(null)
  const again = useRef(false)
  const enabledRef = useRef(enabled)

  useEffect(() => {
    enabledRef.current = enabled
  }, [enabled])

  const refresh = useCallback((): Promise<void> => {
    if (!enabled) return Promise.resolve()
    if (current.current) {
      // The read under way may predate her punch: one more once it lands, never a dropped one.
      again.current = true
      return current.current
    }
    const run = (async () => {
      try {
        do {
          again.current = false
          try {
            const next = await pointageApi.clockStatus()
            // An answer landing after the pointage went off must not put the lock back.
            if (enabledRef.current) setStatus(next)
            // An admin chose this device as a borne: pick it up, then read again so « cet appareil » holds.
            if (next.borneWaiting && (await claimWaitingBorne(next.borneWaiting))) again.current = true
          } catch {
            // A failed read keeps the last answer: never lock on a network error, never unlock on one either.
          }
        } while (again.current && enabledRef.current)
      } finally {
        current.current = null
      }
    })()
    current.current = run
    return run
  }, [enabled])

  useEffect(() => {
    if (!enabled) {
      setStatus(null)
      return
    }
    void refresh()
    const id = window.setInterval(() => document.visibilityState === "visible" && void refresh(), REFRESH_MS)
    const onVisible = () => document.visibilityState === "visible" && void refresh()
    document.addEventListener("visibilitychange", onVisible)
    window.addEventListener("focus", onVisible)
    const off = onClockInRequired(() => void refresh())
    return () => {
      window.clearInterval(id)
      document.removeEventListener("visibilitychange", onVisible)
      window.removeEventListener("focus", onVisible)
      off()
    }
  }, [enabled, refresh])

  useClinicRealtime([RealtimeResource.Team, RealtimeResource.Clinics], () => void refresh())

  const covering =
    enabled
    && status?.locked === true
    && !sessionLocked
    && !isChromeLessPath(pathname)
    && !pathname?.startsWith(LEAVE_PATH)
    && !(status.hasSchema === false && SCHEMA_SETUP_PATHS.some((p) => pathname?.startsWith(p)))
  const value = useMemo(() => ({ status, covering, refresh }), [status, covering, refresh])

  return (
    <ClockStatusContext.Provider value={value}>
      {children}
      {covering && <ClockInGate status={status} onPunched={refresh} />}
    </ClockStatusContext.Provider>
  )
}

/**
 * « Ajouter une borne » chose this device: claim it (the server mints the secret and sets the cookie here) and say so.
 * False when nothing was claimed — another tab got there first, or it was retired meanwhile; the next read asks again.
 */
async function claimWaitingBorne(name: string): Promise<boolean> {
  try {
    const terminal = await pointageApi.claimBorne()
    forgetBorneProbe()
    toast.success(`Cet appareil est maintenant la borne ${quoteFr(terminal.name || name)}.`, {
      action: { label: "Ouvrir", onClick: () => window.location.assign("/pointage") },
    })
    return true
  } catch {
    return false
  }
}

/** « Borne : « Accueil » » / « Bornes : « Accueil », « Salle 2 » » — where to go, by name. */
export function bornesPhrase(bornes: string[]): string {
  if (bornes.length === 0) return "Aucune borne active : demandez à l'administrateur."
  return bornes.length === 1 ? `Borne : ${quoteFr(bornes[0])}` : `Bornes : ${bornes.map(quoteFr).join(", ")}`
}

/** « à la borne « Accueil » » / « à la borne « Accueil » ou « Salle 2 » »; null with no live borne. */
export function atBornes(bornes: string[]): string | null {
  if (bornes.length === 0) return null
  const names = bornes.map(quoteFr)
  const list = names.length === 1 ? names[0] : `${names.slice(0, -1).join(", ")} ou ${names[names.length - 1]}`
  return `à la borne ${list}`
}

/** « 08:47 » from a UTC instant, on this device's clock. */
export function clockTime(instant: Date): string {
  return format(instant, "HH:mm")
}

/** The instant while it is still ahead, null once passed — re-rendering (and calling `onPassed`) the moment it passes. */
export function useFutureInstant(iso: string | null | undefined, onPassed?: () => void): Date | null {
  const [, setTick] = useState(0)
  const at = iso ? new Date(iso).getTime() : Number.NaN
  const passedRef = useRef(onPassed)
  useEffect(() => {
    passedRef.current = onPassed
  }, [onPassed])

  useEffect(() => {
    if (!Number.isFinite(at)) return
    const wait = at - Date.now()
    if (wait <= 0) return
    const id = window.setTimeout(() => {
      setTick((n) => n + 1)
      passedRef.current?.()
    }, wait + 500)
    return () => window.clearTimeout(id)
  }, [at])

  return Number.isFinite(at) && at > Date.now() ? new Date(at) : null
}

/**
 * The lock itself. A Radix dialog rather than a plain layer, so a form left open beneath it keeps its typing and stays
 * inert: the newest modal layer owns focus and outside presses. Nothing dismisses it — only her punch does. On a borne
 * the pad is inside (AC-2), under her name so a colleague at a shared PC sees whose it is; elsewhere it names the
 * bornes. A missing or forgotten schéma is drawn right here, and « Se déconnecter » is always offered (AC-10).
 */
function ClockInGate({ status, onPunched }: { status: ClockStatusDto; onPunched: () => Promise<void> }) {
  const { logout } = useSession()
  const { data: userStatus } = useUserStatus()
  const leaveOn = userStatus?.clinic?.leaveRequestsEnabled === true
  const titleRef = useRef<HTMLHeadingElement>(null)
  // A refusal that ends the punch leaves the pad shut; « Réessayer » opens a fresh one.
  const [attempt, setAttempt] = useState(0)
  const [ended, setEnded] = useState(false)
  // Saved here before the status read catches up: the arrival pad follows at once.
  const [savedHere, setSavedHere] = useState(false)
  const [forgot, setForgot] = useState(false)
  const [setterBusy, setSetterBusy] = useState(false)
  const blockedUntil = useFutureInstant(status.schemaLockedUntil, () => {
    setEnded(false)
    setAttempt((n) => n + 1)
    void onPunched()
  })

  const onBorne = status.canClockOutHere
  const hasSchema = status.hasSchema || savedHere
  const drawing = !hasSchema || forgot
  const canPunchHere = onBorne && hasSchema && !forgot && !blockedUntil

  const schemaSaved = () => {
    toast.success("Schéma enregistré.")
    setSavedHere(true)
    setForgot(false)
    setEnded(false)
    setAttempt((n) => n + 1)
    void onPunched()
  }

  return (
    <DialogPrimitive.Root open modal>
      <DialogPrimitive.Portal>
        {/* The overlay is what locks the page's scroll and tells the other popups a modal is up. */}
        <DialogPrimitive.Overlay className="fixed inset-0 z-[55] bg-background print:hidden" />
        <DialogPrimitive.Content
          data-clock-in-gate=""
          aria-describedby="clock-in-gate-detail"
          onOpenAutoFocus={(e) => {
            // The title, never « Se déconnecter »: the lock often rises on a save's Enter, and a second one would sign her out.
            e.preventDefault()
            titleRef.current?.focus({ preventScroll: true })
          }}
          onEscapeKeyDown={(e) => e.preventDefault()}
          onPointerDownOutside={(e) => e.preventDefault()}
          onInteractOutside={(e) => e.preventDefault()}
          className="fixed inset-0 z-[55] flex h-dvh overflow-y-auto bg-background p-4 outline-none print:hidden"
        >
          <div className="m-auto flex w-full max-w-md flex-col items-center gap-4 py-2 text-center">
            <span aria-hidden="true" className="flex size-12 items-center justify-center rounded-full bg-primary/10 text-primary">
              <Fingerprint className="size-6" />
            </span>
            <div className="space-y-1.5">
              <DialogPrimitive.Title ref={titleRef} tabIndex={-1} className="text-2xl font-semibold text-balance outline-none">
                Pointez votre arrivée
              </DialogPrimitive.Title>
              <p id="clock-in-gate-detail" className="text-base text-muted-foreground">
                {onBorne ? "Cet appareil est une borne du cabinet." : bornesPhrase(status.bornes)}
              </p>
              {onBorne && <p className="text-lg font-semibold break-words">{status.name}</p>}
            </div>

            {drawing ? (
              <section aria-labelledby="clock-in-gate-schema" className="flex w-full flex-col items-center gap-3">
                <h2 id="clock-in-gate-schema" className="text-base font-semibold">
                  {forgot ? "Changer votre schéma" : "Créer votre schéma"}
                </h2>
                <SchemaSetter
                  key={forgot ? "change" : "create"}
                  hasSchema={forgot}
                  onSaved={schemaSaved}
                  onBusyChange={setSetterBusy}
                  hideTrail={onBorne}
                  padClassName="max-w-[min(18rem,40dvh)]"
                />
                {forgot && (
                  <Button
                    variant="outline"
                    size="lg"
                    className="w-full max-w-72"
                    disabled={setterBusy}
                    onClick={() => setForgot(false)}
                  >
                    Annuler
                  </Button>
                )}
              </section>
            ) : blockedUntil ? (
              <p
                role="status"
                className="rounded-lg border border-destructive/40 bg-destructive-wash px-4 py-3 text-base font-medium text-destructive"
              >
                Schéma bloqué jusqu&apos;à {clockTime(blockedUntil)}
              </p>
            ) : canPunchHere ? (
              <PunchFlow
                key={attempt}
                userId={status.userId}
                name={status.name}
                action="arrival"
                onDone={(result) => {
                  // The toast waits for the lock to lift: until then the page under it is hidden from a screen reader.
                  void onPunched().then(() => toast.success(`Arrivée enregistrée à ${result.at}.`))
                }}
                onEndingRefusal={() => {
                  setEnded(true)
                  void onPunched()
                }}
                onBack={() => logout()}
                backLabel="Se déconnecter"
                showHeading={false}
                padAutoFocus={false}
                padClassName="max-w-[min(18rem,40dvh)]"
              />
            ) : null}

            {canPunchHere && ended && (
              <Button
                variant="outline"
                size="lg"
                className="w-full max-w-72"
                onClick={() => {
                  setEnded(false)
                  setAttempt((n) => n + 1)
                }}
              >
                <RotateCcw aria-hidden="true" className="size-4" />
                Réessayer
              </Button>
            )}

            {!canPunchHere && (
              <Button variant="outline" size="lg" className="w-full max-w-72" disabled={setterBusy} onClick={() => logout()}>
                <LogOut aria-hidden="true" className="size-4" />
                Se déconnecter
              </Button>
            )}

            {((hasSchema && !forgot) || onBorne) && (
              <div className="flex flex-wrap items-center justify-center gap-x-2">
                {hasSchema && !forgot && (
                  <Button variant="link" className="coarse:min-h-11" onClick={() => setForgot(true)}>
                    Schéma oublié ?
                  </Button>
                )}
                {onBorne && (
                  <Button asChild variant="link" className="coarse:min-h-11">
                    <Link href="/pointage">Ce n&apos;est pas moi</Link>
                  </Button>
                )}
              </div>
            )}

            {leaveOn && (
              <Button asChild variant="ghost" size="lg" className="w-full max-w-72">
                <Link href={LEAVE_PATH}>
                  <CalendarDays aria-hidden="true" className="size-4" />
                  Demander un congé
                </Link>
              </Button>
            )}
          </div>
        </DialogPrimitive.Content>
      </DialogPrimitive.Portal>
    </DialogPrimitive.Root>
  )
}
