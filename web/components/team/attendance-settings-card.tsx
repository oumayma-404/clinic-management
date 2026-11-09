"use client"

import Link from "next/link"
import { useCallback, useEffect, useState } from "react"
import { format, formatDistanceToNow, isToday, isTomorrow } from "date-fns"
import { fr } from "date-fns/locale"
import { toast } from "sonner"
import { Check, CircleAlert, LockOpen, Monitor, MonitorSmartphone, RotateCcw, Smartphone, Tablet, Trash2 } from "lucide-react"
import {
  AlertDialog,
  AlertDialogAction,
  AlertDialogCancel,
  AlertDialogContent,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogHeader,
  AlertDialogTitle,
} from "@/components/ui/alert-dialog"
import { Button } from "@/components/ui/button"
import { Dialog, DialogBody, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from "@/components/ui/dialog"
import { DiscardChangesDialog } from "@/components/ui/discard-changes-dialog"
import { FormErrorBanner } from "@/components/ui/form-error-banner"
import { Input } from "@/components/ui/input"
import { Label } from "@/components/ui/label"
import { LoadFailureNotice } from "@/components/ui/load-failure"
import { RadioGroup, RadioGroupItem } from "@/components/ui/radio-group"
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select"
import { STATUS_TONE_CLASS, type StatusTone } from "@/components/ui/status-tone"
import { Switch } from "@/components/ui/switch"
import { useGuide } from "@/components/guide/guide-provider"
import { StepUpDialog } from "@/components/security/step-up-dialog"
import { forgetBorneProbe } from "@/components/team/borne-link"
import { isSchemaBlocked } from "@/components/team/team-today-list"
import { ApiError } from "@/lib/api/client"
import {
  PointageErrorCode,
  REGISTER_TERMINAL_STEP_UP_ACTION,
  SCHEMA_MAX_FAILURES,
  TERMINAL_NAME_MAX,
  pointageApi,
  type AttendancePersonSettingDto,
  type AttendanceSettingsDto,
  type AttendanceSetupDto,
  type AttendanceTerminalDto,
  type BorneDeviceDto,
  type PointageStatusDto,
} from "@/lib/api/pointage"
import { securityApi } from "@/lib/api/security"
import { USER_ROLE_LABELS_FR, type UserRole } from "@/lib/api/users"
import { getErrorMessage, showErrorToast } from "@/lib/errors"
import { formatDate, formatDateTime, quoteFr } from "@/lib/format"
import { useDirtyGuard } from "@/lib/hooks/use-dirty-guard"
import { RealtimeResource } from "@/lib/realtime/clinic-hub"
import { useClinicRealtime } from "@/lib/realtime/use-clinic-realtime"
import { cn } from "@/lib/utils"

const DEFAULT_BORNE_NAME = "Accueil"

/** The Horaires card's anchor on Paramètres. */
export const SETTINGS_HOURS_ANCHOR = "horaires"
/** Asks Paramètres to open « Horaires d'ouverture » and bring it into view. */
export const SHOW_SETTINGS_HOURS_EVENT = "settings:show-hours"
const showHours = () => window.dispatchEvent(new Event(SHOW_SETTINGS_HOURS_EVENT))

/** « Accueil », else « Accueil 2 », « Accueil 3 »… — the first name no live or waiting borne carries. */
function nextFreeName(terminals: AttendanceTerminalDto[]): string {
  const taken = new Set(terminals.filter((t) => t.isActive).map((t) => t.name.trim().toLowerCase()))
  if (!taken.has(DEFAULT_BORNE_NAME.toLowerCase())) return DEFAULT_BORNE_NAME
  for (let n = 2; ; n++) {
    const candidate = `${DEFAULT_BORNE_NAME} ${n}`
    if (!taken.has(candidate.toLowerCase())) return candidate
  }
}

/** « 14:02 » today, « 3 nov. 14:02 » otherwise. */
function sinceLabel(iso: string): string {
  const at = new Date(iso)
  return isToday(at) ? format(at, "HH:mm") : format(at, "d MMM HH:mm", { locale: fr })
}

/** What « Ajouter une borne » opens on: a fresh borne, or one that takes over `replacing`. */
interface BorneRequest {
  replacing: AttendanceTerminalDto | null
}

/**
 * Paramètres → Équipe → « Pointage » (`pointage-borne` AC-1, AC-2, AC-4, AC-5): where the setup stands, the cabinet's
 * bornes and who pointe. Shown only while the pointage is on; the caller gates on that and on the admin role, and hands
 * in the lock switch so its state is read beside it.
 */
export function AttendanceSettingsCard({ lockOn, lockSwitch }: { lockOn: boolean; lockSwitch: React.ReactNode }) {
  const [settings, setSettings] = useState<AttendanceSettingsDto | null>(null)
  const [failed, setFailed] = useState(false)
  const [device, setDevice] = useState<PointageStatusDto | null>(null)
  const [borneRequest, setBorneRequest] = useState<BorneRequest | null>(null)

  const load = useCallback(async () => {
    try {
      const [next, here] = await Promise.all([pointageApi.settings(), pointageApi.status().catch(() => null)])
      setSettings(next)
      setDevice(here)
      setFailed(false)
    } catch {
      setFailed(true)
    }
  }, [])

  // The lock switch writes the clinic row, not the team: its state is re-read here when it moves.
  useEffect(() => {
    void load()
  }, [load, lockOn])
  useClinicRealtime(RealtimeResource.Team, () => void load())
  // Saved hours move the checklist's first step and the lock's state.
  useClinicRealtime(RealtimeResource.Clinics, () => void load())

  if (!settings) {
    return (
      <div className="space-y-3">
        {failed ? (
          <LoadFailureNotice message="Les réglages du pointage n'ont pas pu être chargés." onRetry={() => void load()} />
        ) : (
          <div className="h-32 animate-pulse rounded-lg bg-muted/50" aria-hidden="true" />
        )}
        <div className="flex items-center justify-between gap-3">
          <Label htmlFor="team-attendance-lock" className="font-normal">
            Blocage de l&apos;app
          </Label>
          {lockSwitch}
        </div>
      </div>
    )
  }

  const terminals = settings.terminals.filter((t) => t.isActive)
  const live = terminals.filter((t) => !t.isPending)
  const people = settings.people.filter((p) => p.isActive)
  const thisBorneId = device?.isBorne ? device.terminalId : null

  return (
    <div className="space-y-5">
      <SetupChecklist
        setup={settings.setup}
        terminals={terminals}
        lockSwitch={lockSwitch}
        onAddBorne={(replacing) => setBorneRequest({ replacing })}
      />

      <section aria-labelledby="bornes-title" className="space-y-2">
        <h3 id="bornes-title" className="text-sm font-medium">
          Bornes
        </h3>
        {terminals.length === 0 ? (
          <p className="text-sm text-muted-foreground">Aucune borne pour l&apos;instant.</p>
        ) : (
          <ul className="divide-y rounded-md border">
            {terminals.map((terminal) => (
              <TerminalRow
                key={terminal.id}
                terminal={terminal}
                isThisDevice={thisBorneId === terminal.id}
                lastLiveWithLock={lockOn && !terminal.isPending && live.length === 1}
                onRevoked={() => void load()}
                onChooseOther={() => setBorneRequest({ replacing: terminal })}
              />
            ))}
          </ul>
        )}
        <div className="flex flex-wrap items-center gap-2">
          <Button
            variant="outline"
            size="sm"
            className="gap-1.5"
            onClick={() => setBorneRequest({ replacing: null })}
            data-guide="pointage-add-borne"
          >
            <MonitorSmartphone className="size-4" aria-hidden="true" />
            Ajouter une borne
          </Button>
          {device?.isBorne && (
            <Button asChild variant="ghost" size="sm">
              <Link href="/pointage">Ouvrir la borne</Link>
            </Button>
          )}
        </div>
      </section>

      <section aria-labelledby="pointent-title" className="space-y-2" data-guide="pointage-people">
        <h3 id="pointent-title" className="text-sm font-medium">
          Personnes qui pointent
        </h3>
        <ul className="divide-y rounded-md border">
          {people.map((person) => (
            <PointagePersonRow key={person.userId} person={person} lockOn={lockOn} onChanged={() => void load()} />
          ))}
        </ul>
      </section>

      <BorneDialog
        request={borneRequest}
        onClose={() => setBorneRequest(null)}
        terminals={terminals}
        thisBorneId={thisBorneId}
        onRegistered={() => void load()}
      />
    </div>
  )
}

type StepState = "done" | "todo" | "problem"

interface SetupStep {
  title: React.ReactNode
  state: StepState
  status: React.ReactNode
  /** The step's fix, as a button label and what it does. */
  action?: { label: string; run: () => void }
  /** A control in place of a button (the lock switch). */
  control?: React.ReactNode
}

/** The four setup steps, each a status with its fix; the first unfinished one carries the filled button. */
function SetupChecklist({
  setup,
  terminals,
  lockSwitch,
  onAddBorne,
}: {
  setup: AttendanceSetupDto
  terminals: AttendanceTerminalDto[]
  lockSwitch: React.ReactNode
  onAddBorne: (replacing: AttendanceTerminalDto | null) => void
}) {
  const { start } = useGuide()
  const live = terminals.filter((t) => !t.isPending)
  const pending = terminals.filter((t) => t.isPending)

  const borne: SetupStep = (() => {
    const title = "Borne"
    if (setup.borneLive && setup.noPunchSinceOpening) {
      return {
        title,
        state: "problem",
        status: "Aucun pointage depuis l'ouverture",
        action: { label: "Remplacer", run: () => onAddBorne(live.length === 1 ? live[0] : null) },
      }
    }
    if (setup.borneLive) {
      return {
        title,
        state: "done",
        status: live.map((t) => (t.deviceDescription ? `${t.name} · ${t.deviceDescription}` : t.name)).join(" ; "),
      }
    }
    const waiting = pending[0]
    if (waiting?.deviceSignedOut) {
      return {
        title,
        state: "problem",
        status: `${waiting.name} : appareil déconnecté avant d'avoir reçu la borne`,
        action: { label: "Choisir un autre appareil", run: () => onAddBorne(waiting) },
      }
    }
    if (waiting) {
      return {
        title,
        state: "todo",
        status: `En attente depuis ${sinceLabel(waiting.createdAt)}${waiting.deviceDescription ? ` · ${waiting.deviceDescription}` : ""}`,
      }
    }
    return { title, state: "todo", status: "Aucune borne", action: { label: "Ajouter une borne", run: () => onAddBorne(null) } }
  })()

  const created = setup.trackedCount - setup.schemasMissing.length
  const schemas: SetupStep =
    setup.trackedCount === 0
      ? { title: "Schémas", state: "todo", status: "Personne ne pointe" }
      : {
          title: "Schémas",
          state: setup.schemasMissing.length === 0 ? "done" : "todo",
          status: (
            <>
              {created} sur {setup.trackedCount} créé{created > 1 ? "s" : ""}
              {setup.schemasMissing.length > 0 && <> · à créer : {setup.schemasMissing.join(", ")}</>}
            </>
          ),
        }

  const lock = lockChip(setup)
  const steps: SetupStep[] = [
    setup.hoursSet
      ? { title: "Horaires du cabinet", state: "done", status: "Définis" }
      : {
          title: "Horaires du cabinet",
          state: "todo",
          status: "Non définis",
          action: { label: "Définir les horaires", run: showHours },
        },
    borne,
    schemas,
    {
      title: (
        <Label htmlFor="team-attendance-lock" className="text-sm font-medium">
          Blocage de l&apos;app
        </Label>
      ),
      state: setup.lockState === "inactive" ? "problem" : setup.lockState === "off" ? "todo" : "done",
      status: null,
      control: (
        <span className="ms-auto flex items-center gap-2">
          {lock.hoursLink ? (
            <button
              type="button"
              onClick={showHours}
              className={cn(
                "rounded px-1.5 py-0.5 text-xs font-medium underline underline-offset-2 coarse:min-h-11",
                STATUS_TONE_CLASS[lock.tone],
              )}
            >
              {lock.text}
            </button>
          ) : (
            <span role="status" className={cn("rounded px-1.5 py-0.5 text-xs font-medium", STATUS_TONE_CLASS[lock.tone])}>
              {lock.text}
            </span>
          )}
          {lockSwitch}
        </span>
      ),
    },
  ]
  const leadIndex = steps.findIndex((s) => s.state !== "done" && s.action)
  const incomplete =
    !setup.hoursSet ||
    !setup.borneLive ||
    setup.noPunchSinceOpening ||
    setup.trackedCount === 0 ||
    setup.schemasMissing.length > 0 ||
    setup.lockState === "inactive"

  return (
    <section aria-labelledby="pointage-setup-title" className="space-y-2" data-guide="pointage-setup">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <h3 id="pointage-setup-title" className="text-sm font-medium">
          Mise en place
        </h3>
        {incomplete && (
          <Button
            type="button"
            variant="ghost"
            size="sm"
            className="min-h-9 coarse:min-h-11"
            onClick={() => start("pointage")}
            aria-label="Me guider : pointage"
          >
            Me guider
          </Button>
        )}
      </div>
      <ol className="divide-y rounded-md border">
        {steps.map((step, index) => (
          <li key={index} className="flex flex-wrap items-center gap-x-3 gap-y-2 px-3 py-2.5">
            <StepMark index={index} state={step.state} />
            <div className="min-w-0 flex-[1_1_12rem]">
              <div className="text-sm font-medium">{step.title}</div>
              {step.status && (
                <p
                  className={cn(
                    "break-words text-xs",
                    step.state === "problem" ? "font-medium text-destructive" : "text-muted-foreground",
                  )}
                >
                  {step.status}
                </p>
              )}
            </div>
            {step.control}
            {step.action && (
              <Button
                type="button"
                size="sm"
                variant={index === leadIndex ? "default" : "outline"}
                className="ms-auto min-h-9 coarse:min-h-11"
                onClick={step.action.run}
              >
                {step.action.label}
              </Button>
            )}
          </li>
        ))}
      </ol>
    </section>
  )
}

function StepMark({ index, state }: { index: number; state: StepState }) {
  if (state === "done") {
    return (
      <span className="flex size-6 shrink-0 items-center justify-center rounded-full bg-success-wash text-success">
        <Check className="size-3.5" aria-hidden="true" />
        <span className="sr-only">Étape {index + 1}, faite</span>
      </span>
    )
  }
  if (state === "problem") {
    return (
      <span className="flex size-6 shrink-0 items-center justify-center rounded-full bg-destructive-wash text-destructive">
        <CircleAlert className="size-3.5" aria-hidden="true" />
        <span className="sr-only">Étape {index + 1}, à reprendre</span>
      </span>
    )
  }
  return (
    <span className="flex size-6 shrink-0 items-center justify-center rounded-full border text-xs font-semibold tabular-nums text-muted-foreground">
      {index + 1}
      <span className="sr-only">, à faire</span>
    </span>
  )
}

/** The lock's real state, beside its switch: on is not the same as locking. */
function lockChip(setup: AttendanceSetupDto): { text: string; tone: StatusTone; hoursLink?: boolean } {
  switch (setup.lockState) {
    case "active":
      return { text: "Actif", tone: "positive" }
    case "from":
      return { text: setup.lockActiveFrom ? lockFromLabel(setup.lockActiveFrom) : "Actif bientôt", tone: "accepted" }
    case "inactive":
      if (setup.lockInactiveReason === "hours_unset") {
        return { text: "Inactif — horaires non définis", tone: "active", hoursLink: true }
      }
      if (setup.lockInactiveReason === "borne_pending") return { text: "Inactif — borne en attente", tone: "active" }
      return { text: "Inactif — aucune borne active", tone: "active" }
    default:
      return { text: "Désactivé", tone: "neutral" }
  }
}

/** « Actif à partir de demain 08:00 ». */
function lockFromLabel(iso: string): string {
  const at = new Date(iso)
  const hm = format(at, "HH:mm")
  if (isToday(at)) return `Actif à partir de ${hm}`
  if (isTomorrow(at)) return `Actif à partir de demain ${hm}`
  return `Actif à partir du ${format(at, "EEEE d MMMM", { locale: fr })} ${hm}`
}

function TerminalRow({
  terminal,
  isThisDevice,
  lastLiveWithLock,
  onRevoked,
  onChooseOther,
}: {
  terminal: AttendanceTerminalDto
  isThisDevice: boolean
  /** Retiring it leaves no live borne while the lock is on: the app stops locking. */
  lastLiveWithLock: boolean
  onRevoked: () => void
  onChooseOther: () => void
}) {
  const [confirm, setConfirm] = useState(false)
  const [busy, setBusy] = useState(false)
  const [refusal, setRefusal] = useState<string | null>(null)

  const revoke = async () => {
    setBusy(true)
    setRefusal(null)
    try {
      await pointageApi.revokeTerminal(terminal.id)
      forgetBorneProbe()
      toast.success(`Borne ${quoteFr(terminal.name)} retirée.`)
      setConfirm(false)
      onRevoked()
    } catch (err) {
      if (err instanceof ApiError && err.code === PointageErrorCode.BorneLastWhilePresent) {
        setRefusal(getErrorMessage(err, "La borne n'a pas pu être retirée."))
      } else {
        showErrorToast(err, "La borne n'a pas pu être retirée.")
      }
    } finally {
      setBusy(false)
    }
  }

  const whose = isThisDevice
    ? "Cet appareil ne pourra plus pointer."
    : `L'appareil de la borne ${quoteFr(terminal.name)}${terminal.deviceDescription ? ` (${terminal.deviceDescription})` : ""} ne pourra plus pointer.`

  return (
    <li className="flex flex-wrap items-center gap-x-3 gap-y-1.5 px-3 py-2.5">
      <MonitorSmartphone className="size-4 shrink-0 text-muted-foreground" aria-hidden="true" />
      <div className="min-w-0 flex-[1_1_12rem]">
        <p className="break-words text-sm font-medium">
          {terminal.name}
          {isThisDevice && <span className="ms-2 text-xs font-normal text-muted-foreground">cet appareil</span>}
        </p>
        {terminal.isPending ? (
          <p className={cn("break-words text-xs", terminal.deviceSignedOut ? "font-medium text-destructive" : "text-muted-foreground")}>
            {terminal.deviceSignedOut
              ? "Appareil déconnecté avant d'avoir reçu la borne"
              : `En attente depuis ${sinceLabel(terminal.createdAt)}${terminal.deviceDescription ? ` · ${terminal.deviceDescription}` : ""}`}
          </p>
        ) : (
          <p className="break-words text-xs text-muted-foreground">
            {terminal.deviceDescription && <span>{terminal.deviceDescription} · </span>}
            {terminal.lastUsedAt ? `Utilisée le ${formatDate(terminal.lastUsedAt)}` : "Jamais utilisée"}
          </p>
        )}
      </div>
      {terminal.isPending && terminal.deviceSignedOut && (
        <Button variant="outline" size="sm" className="min-h-9 coarse:min-h-11" onClick={onChooseOther}>
          Choisir un autre appareil
        </Button>
      )}
      <Button
        variant="ghost"
        size="sm"
        className="shrink-0 gap-1.5 text-destructive hover:text-destructive"
        onClick={() => {
          setRefusal(null)
          setConfirm(true)
        }}
        aria-label={`Retirer la borne ${terminal.name}`}
      >
        <Trash2 className="size-4" aria-hidden="true" />
        <span className="hidden sm:inline">Retirer</span>
      </Button>
      <AlertDialog open={confirm} onOpenChange={(open) => !busy && setConfirm(open)}>
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>Retirer la borne {quoteFr(terminal.name)} ?</AlertDialogTitle>
            <AlertDialogDescription>
              {whose} Les pointages déjà enregistrés restent.
              {lastLiveWithLock && " L'app ne sera plus bloquée."}
            </AlertDialogDescription>
          </AlertDialogHeader>
          {refusal && (
            <p role="alert" className="text-sm font-medium text-destructive">
              {refusal}
            </p>
          )}
          <AlertDialogFooter>
            <AlertDialogCancel disabled={busy}>Retour</AlertDialogCancel>
            <AlertDialogAction
              variant="destructive"
              disabled={busy}
              onClick={(e) => {
                e.preventDefault()
                void revoke()
              }}
            >
              Retirer
            </AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>
    </li>
  )
}

/** « Cet appareil » in the device list — it takes the cookie at once instead of waiting to claim it. */
const THIS_DEVICE = "this"
/** The « Remplace » choice for a new borne. */
const NO_REPLACEMENT = "none"

const KIND_LABEL: Record<BorneDeviceDto["kind"], string | null> = {
  pc: "PC",
  tablet: "Tablette",
  phone: "Téléphone",
  unknown: null,
}
const KIND_ICON: Record<BorneDeviceDto["kind"], typeof Monitor> = {
  pc: Monitor,
  tablet: Tablet,
  phone: Smartphone,
  unknown: MonitorSmartphone,
}

/** « PC · Chrome sur Windows » — what the device is, before whose it is. */
function deviceTitle(d: BorneDeviceDto): string {
  const kind = KIND_LABEL[d.kind]
  if (kind && d.deviceLabel) return `${kind} · ${d.deviceLabel}`
  return kind ?? d.deviceLabel ?? "Appareil sans nom"
}

function BorneDialog({
  request,
  onClose,
  terminals,
  thisBorneId,
  onRegistered,
}: {
  request: BorneRequest | null
  onClose: () => void
  /** The live and waiting bornes: what a new one may replace, and the names already taken. */
  terminals: AttendanceTerminalDto[]
  /** The borne this device already is. */
  thisBorneId: string | null
  onRegistered: () => void
}) {
  const open = request !== null
  const [name, setName] = useState(DEFAULT_BORNE_NAME)
  const [nameTouched, setNameTouched] = useState(false)
  const [deviceKey, setDeviceKey] = useState<string>("")
  const [replaceId, setReplaceId] = useState<string>(NO_REPLACEMENT)
  const [devices, setDevices] = useState<BorneDeviceDto[] | null>(null)
  const [devicesFailed, setDevicesFailed] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [nameError, setNameError] = useState<string | null>(null)
  const [deviceError, setDeviceError] = useState<string | null>(null)
  const [stepUp, setStepUp] = useState(false)
  const [hasTotp, setHasTotp] = useState(true)
  const [saving, setSaving] = useState(false)
  const guard = useDirtyGuard(open, (next) => !next && onClose())

  const loadDevices = useCallback(async () => {
    setDevicesFailed(false)
    try {
      setDevices(await pointageApi.borneDevices())
    } catch {
      setDevicesFailed(true)
    }
  }, [])

  // Reset on open only: the terminal list is re-read on every team broadcast.
  const requestKey = request ? (request.replacing?.id ?? NO_REPLACEMENT) : null
  useEffect(() => {
    if (requestKey === null) return
    const replacing = terminals.find((t) => t.id === requestKey) ?? null
    setReplaceId(replacing?.id ?? NO_REPLACEMENT)
    setName(replacing?.name ?? nextFreeName(terminals))
    setNameTouched(false)
    setDeviceKey("")
    setDevices(null)
    setError(null)
    setNameError(null)
    setDeviceError(null)
    void loadDevices()
    // Unknown means both proofs are offered; the server re-checks whichever arrives.
    securityApi
      .getTotpState()
      .then((state) => setHasTotp(state.enrolledAt !== null))
      .catch(() => setHasTotp(true))
  }, [requestKey, loadDevices])

  const others = (devices ?? []).filter((d) => !d.isThisDevice)
  const thisDevice = devices?.find((d) => d.isThisDevice) ?? null
  const chosen = others.find((d) => d.sessionId === deviceKey) ?? null
  // This device already being a borne, choosing it again replaces that one.
  const replaceLocked = deviceKey === THIS_DEVICE && thisBorneId !== null
  const replacing = terminals.find((t) => t.id === replaceId) ?? null

  const chooseReplace = (id: string) => {
    setReplaceId(id)
    if (!nameTouched) setName(terminals.find((t) => t.id === id)?.name ?? nextFreeName(terminals))
  }

  const chooseDevice = (key: string) => {
    setDeviceKey(key)
    setDeviceError(null)
    if (key === THIS_DEVICE && thisBorneId) chooseReplace(thisBorneId)
  }

  const submit = (e: React.FormEvent) => {
    e.preventDefault()
    const trimmed = name.trim()
    setNameError(null)
    setDeviceError(null)
    if (!trimmed) {
      setNameError("Donnez un nom à la borne.")
      return
    }
    const clash = terminals.some(
      (t) => t.id !== replacing?.id && t.name.trim().toLowerCase() === trimmed.toLowerCase(),
    )
    if (clash) {
      setNameError(`Une borne s'appelle déjà ${quoteFr(trimmed)}.`)
      return
    }
    if (!deviceKey) {
      setDeviceError("Choisissez l'appareil qui sera la borne.")
      return
    }
    setError(null)
    setStepUp(true)
  }

  const register = async (token: string) => {
    setSaving(true)
    try {
      const replacingId = replacing?.id ?? null
      const terminal = chosen
        ? await pointageApi.registerTerminal(name.trim(), token, replacingId, chosen.sessionId)
        : await pointageApi.registerTerminal(name.trim(), token, replacingId)
      guard.markClean()
      onClose()
      if (chosen) {
        toast.success(`Borne ${quoteFr(terminal.name)} en attente de l'appareil de ${chosen.personName}.`)
      } else {
        forgetBorneProbe()
        toast.success(`Cet appareil est maintenant la borne ${quoteFr(terminal.name)}.`, {
          action: { label: "Ouvrir", onClick: () => window.location.assign("/pointage") },
        })
      }
      onRegistered()
    } catch (err) {
      const message = getErrorMessage(err, "La borne n'a pas pu être enregistrée.")
      const code = err instanceof ApiError ? err.code : undefined
      if (code === PointageErrorCode.BorneNameTaken) {
        setNameError(message)
      } else if (code === PointageErrorCode.BorneDevicePhone) {
        setDeviceError(message)
      } else {
        if (code === PointageErrorCode.DeviceGone) {
          setDeviceKey("")
          void loadDevices()
        }
        setError(message)
      }
    } finally {
      setSaving(false)
    }
  }

  const ThisIcon = KIND_ICON[thisDevice?.kind ?? "unknown"]

  return (
    <>
      <Dialog open={open} onOpenChange={guard.onOpenChange}>
        <DialogContent className="md:max-w-md" data-guide="pointage-borne-dialog">
          <DialogHeader>
            <DialogTitle>{replacing ? `Remplacer la borne ${quoteFr(replacing.name)}` : "Ajouter une borne"}</DialogTitle>
            <DialogDescription className="sr-only">L&apos;appareil de l&apos;accueil où chacun pointe.</DialogDescription>
          </DialogHeader>
          <DialogBody>
            <form id="register-borne-form" onSubmit={submit} noValidate className="space-y-4">
              <div className="space-y-2">
                <Label htmlFor="borne-name">Nom de la borne</Label>
                <Input
                  id="borne-name"
                  value={name}
                  maxLength={TERMINAL_NAME_MAX}
                  onChange={(e) => {
                    setName(e.target.value)
                    setNameTouched(true)
                    setNameError(null)
                  }}
                  autoComplete="off"
                  aria-invalid={nameError ? true : undefined}
                  aria-describedby={nameError ? "borne-name-error" : undefined}
                />
                {nameError && (
                  <p id="borne-name-error" role="alert" className="text-xs text-destructive">
                    {nameError}
                  </p>
                )}
              </div>
              {terminals.length > 0 && (
                <div className="space-y-2">
                  <Label htmlFor="borne-replace">Remplace</Label>
                  <Select value={replaceId} onValueChange={chooseReplace} disabled={replaceLocked}>
                    <SelectTrigger id="borne-replace" className="w-full">
                      <SelectValue />
                    </SelectTrigger>
                    <SelectContent>
                      <SelectItem value={NO_REPLACEMENT}>Aucune : nouvelle borne</SelectItem>
                      {terminals.map((t) => (
                        <SelectItem key={t.id} value={t.id}>
                          {t.isPending ? `${t.name} (en attente)` : t.name}
                        </SelectItem>
                      ))}
                    </SelectContent>
                  </Select>
                </div>
              )}
              <fieldset className="min-w-0 space-y-2" data-guide="pointage-devices">
                <legend className="text-sm font-medium">Appareil</legend>
                <RadioGroup
                  value={deviceKey}
                  onValueChange={chooseDevice}
                  className="gap-0 divide-y rounded-md border"
                  aria-invalid={deviceError ? true : undefined}
                  aria-describedby={deviceError ? "borne-device-error" : undefined}
                >
                  {others.map((d) => (
                    <DeviceOption
                      key={d.sessionId}
                      value={d.sessionId}
                      icon={KIND_ICON[d.kind]}
                      title={deviceTitle(d)}
                      detail={`${d.personName} · actif ${relativeFr(d.lastActiveAt)}`}
                    />
                  ))}
                  <DeviceOption
                    value={THIS_DEVICE}
                    icon={ThisIcon}
                    title={thisDevice ? `Cet appareil (${thisDevice.personName})` : "Cet appareil"}
                    detail={thisBorneId ? "Déjà une borne : elle sera remplacée" : thisDevice ? deviceTitle(thisDevice) : null}
                  />
                </RadioGroup>
                {deviceError && (
                  <p id="borne-device-error" role="alert" className="text-xs text-destructive">
                    {deviceError}
                  </p>
                )}
                {devicesFailed ? (
                  <LoadFailureNotice
                    message="Les autres appareils n'ont pas pu être chargés."
                    onRetry={() => void loadDevices()}
                  />
                ) : devices === null ? (
                  <div className="h-12 animate-pulse rounded-md bg-muted/50" aria-hidden="true" />
                ) : others.length === 0 ? (
                  <p className="text-xs text-muted-foreground" role="status">
                    Aucun autre appareil connecté : connectez-vous une fois sur le poste de l&apos;accueil.
                  </p>
                ) : null}
              </fieldset>
              <FormErrorBanner message={error} />
            </form>
          </DialogBody>
          <DialogFooter className="flex-col-reverse gap-2 md:flex-row">
            <Button type="button" variant="outline" onClick={() => guard.onOpenChange(false)} disabled={saving}>
              Retour
            </Button>
            <Button type="submit" form="register-borne-form" disabled={saving}>
              Enregistrer la borne
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
      <DiscardChangesDialog guard={guard} />
      <StepUpDialog
        open={stepUp}
        onOpenChange={setStepUp}
        action={REGISTER_TERMINAL_STEP_UP_ACTION}
        purpose="Confirmez votre identité pour ajouter une borne de pointage."
        hasTotp={hasTotp}
        onConfirmed={(token) => {
          setStepUp(false)
          void register(token)
        }}
      />
    </>
  )
}

/** One choosable device: the whole row is the target, so a finger never has to find the 16 px circle. */
function DeviceOption({
  value,
  icon: Icon,
  title,
  detail,
}: {
  value: string
  icon: typeof Monitor
  title: string
  detail: string | null
}) {
  const id = `borne-device-${value}`
  return (
    // min-w-0: the radio group is a grid, and a long line would otherwise widen its one track past the dialog.
    <label htmlFor={id} className="flex min-w-0 cursor-pointer items-center gap-3 px-3 py-2.5 coarse:py-3">
      <RadioGroupItem id={id} value={value} />
      <Icon className="size-4 shrink-0 text-muted-foreground" aria-hidden="true" />
      <span className="min-w-0 flex-1">
        <span className="block break-words text-sm font-medium">{title}</span>
        {detail && <span className="block break-words text-xs text-muted-foreground">{detail}</span>}
      </span>
    </label>
  )
}

/** « il y a 2 minutes » — how recently a device was used, so the admin can tell the reception PC from an old session. */
function relativeFr(iso: string): string {
  return formatDistanceToNow(new Date(iso), { addSuffix: true, locale: fr })
}

/**
 * One person's pointage: whether they pointe, their schéma, and the two fixes (« Débloquer », « Réinitialiser le
 * schéma »). A row of « Personnes qui pointent », and the header of a staff file's « Présences » (`as="div"`).
 */
export function PointagePersonRow({
  person,
  lockOn,
  onChanged,
  as: Root = "li",
  showName = true,
}: {
  person: AttendancePersonSettingDto
  /** With the lock on, tracking a non-secretary blocks their app until they point: asked first. */
  lockOn: boolean
  onChanged: () => void
  as?: "li" | "div"
  showName?: boolean
}) {
  const [busy, setBusy] = useState(false)
  const [confirmReset, setConfirmReset] = useState(false)
  const [confirmTrack, setConfirmTrack] = useState(false)
  const blocked = isSchemaBlocked(person.lockedUntil)
  const switchId = `pointe-${person.userId}`

  const save = async (next: boolean) => {
    setBusy(true)
    try {
      // No version: one idempotent switch, where the last press is the intent (« Mode discret »'s shape).
      await pointageApi.setTracked(person.userId, next, 0)
      toast.success(next ? `${person.fullName} pointe désormais.` : `${person.fullName} ne pointe plus.`)
      setConfirmTrack(false)
      onChanged()
    } catch (err) {
      showErrorToast(err, "Le réglage n'a pas pu être enregistré.")
    } finally {
      setBusy(false)
    }
  }

  const toggle = (next: boolean) => {
    const asksFirst = next && lockOn && person.role !== "secretary" && person.role !== "admin"
    if (asksFirst) setConfirmTrack(true)
    else void save(next)
  }

  const unlock = async () => {
    setBusy(true)
    try {
      await pointageApi.unlockSchema(person.userId)
      toast.success(`Schéma de ${person.fullName} débloqué.`)
      onChanged()
    } catch (err) {
      showErrorToast(err, "Le schéma n'a pas pu être débloqué.")
    } finally {
      setBusy(false)
    }
  }

  const reset = async () => {
    setBusy(true)
    try {
      await pointageApi.resetSchema(person.userId)
      toast.success(`Schéma de ${person.fullName} réinitialisé.`)
      setConfirmReset(false)
      onChanged()
    } catch (err) {
      showErrorToast(err, "Le schéma n'a pas pu être réinitialisé.")
    } finally {
      setBusy(false)
    }
  }

  const schemaLine = !person.tracked
    ? null
    : blocked
      ? `Bloqué après ${SCHEMA_MAX_FAILURES} essais, jusqu'à ${formatDateTime(person.lockedUntil!)}`
      : person.hasSchema
        ? "Schéma créé"
        : "Schéma à créer"
  const roleLabel = USER_ROLE_LABELS_FR[person.role as UserRole] ?? person.role

  return (
    <Root className={cn("flex flex-wrap items-center gap-x-3 gap-y-1.5", Root === "li" && "px-3 py-2.5")}>
      <div className="min-w-0 flex-[1_1_10rem]">
        <Label htmlFor={switchId} className="block break-words font-medium">
          {showName ? person.fullName : "Pointe"}
        </Label>
        {(showName || schemaLine) && (
          <p className="text-xs text-muted-foreground">
            {showName && roleLabel}
            {schemaLine && (
              <span className={blocked ? "text-destructive" : undefined}>
                {showName ? " · " : ""}
                {schemaLine}
              </span>
            )}
          </p>
        )}
      </div>
      {person.tracked && blocked && (
        <Button
          variant="outline"
          size="sm"
          className="min-h-9 shrink-0 gap-1.5 coarse:min-h-11"
          disabled={busy}
          onClick={() => void unlock()}
          aria-label={`Débloquer le schéma de ${person.fullName}`}
        >
          <LockOpen className="size-4" aria-hidden="true" />
          Débloquer
        </Button>
      )}
      {person.tracked && (person.hasSchema || blocked) && (
        <Button
          variant="ghost"
          size="sm"
          className="min-h-9 shrink-0 gap-1.5 coarse:min-h-11"
          disabled={busy}
          onClick={() => setConfirmReset(true)}
          aria-label={`Réinitialiser le schéma de ${person.fullName}`}
        >
          <RotateCcw className="size-4" aria-hidden="true" />
          <span className="hidden sm:inline">Réinitialiser le schéma</span>
        </Button>
      )}
      <Switch
        id={switchId}
        checked={person.tracked}
        disabled={busy}
        onCheckedChange={toggle}
        aria-label={`${person.fullName} pointe`}
      />
      <AlertDialog open={confirmTrack} onOpenChange={(open) => !busy && setConfirmTrack(open)}>
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>{person.fullName} pointe ?</AlertDialogTitle>
            <AlertDialogDescription>
              {person.fullName} devra pointer son arrivée avant de travailler.
            </AlertDialogDescription>
          </AlertDialogHeader>
          <AlertDialogFooter>
            <AlertDialogCancel disabled={busy}>Retour</AlertDialogCancel>
            <AlertDialogAction
              disabled={busy}
              onClick={(e) => {
                e.preventDefault()
                void save(true)
              }}
            >
              Confirmer
            </AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>
      <AlertDialog open={confirmReset} onOpenChange={(open) => !busy && setConfirmReset(open)}>
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>Réinitialiser le schéma de {person.fullName} ?</AlertDialogTitle>
            <AlertDialogDescription>
              {person.fullName} devra en créer un nouveau avant de pointer.
            </AlertDialogDescription>
          </AlertDialogHeader>
          <AlertDialogFooter>
            <AlertDialogCancel disabled={busy}>Retour</AlertDialogCancel>
            <AlertDialogAction
              variant="destructive"
              disabled={busy}
              onClick={(e) => {
                e.preventDefault()
                void reset()
              }}
            >
              Réinitialiser
            </AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>
    </Root>
  )
}
