"use client"

import Link from "next/link"
import { useCallback, useEffect, useState } from "react"
import { toast } from "sonner"
import { MonitorSmartphone, RotateCcw, Trash2 } from "lucide-react"
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
import { Switch } from "@/components/ui/switch"
import { StepUpDialog } from "@/components/security/step-up-dialog"
import {
  REGISTER_TERMINAL_STEP_UP_ACTION,
  SCHEMA_MAX_FAILURES,
  TERMINAL_NAME_MAX,
  pointageApi,
  type AttendancePersonSettingDto,
  type AttendanceSettingsDto,
  type AttendanceTerminalDto,
  type PointageStatusDto,
} from "@/lib/api/pointage"
import { securityApi } from "@/lib/api/security"
import { USER_ROLE_LABELS_FR, type UserRole } from "@/lib/api/users"
import { getErrorMessage, showErrorToast } from "@/lib/errors"
import { formatDate, formatDateTime, quoteFr } from "@/lib/format"
import { useDirtyGuard } from "@/lib/hooks/use-dirty-guard"
import { RealtimeResource } from "@/lib/realtime/clinic-hub"
import { useClinicRealtime } from "@/lib/realtime/use-clinic-realtime"
import { forgetBorneProbe } from "@/components/team/borne-link"

const DEFAULT_BORNE_NAME = "Accueil"

/**
 * Paramètres → Équipe → « Pointage » (`pointage-borne` AC-1, AC-2, AC-4, AC-5): the cabinet's bornes and who
 * pointe. Shown only while the pointage is on; the caller gates on that and on the admin role.
 */
export function AttendanceSettingsCard() {
  const [settings, setSettings] = useState<AttendanceSettingsDto | null>(null)
  const [failed, setFailed] = useState(false)
  const [device, setDevice] = useState<PointageStatusDto | null>(null)

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

  useEffect(() => {
    void load()
  }, [load])
  useClinicRealtime(RealtimeResource.Team, () => void load())

  if (failed && !settings) {
    return <LoadFailureNotice message="Les réglages du pointage n'ont pas pu être chargés." onRetry={() => void load()} />
  }
  if (!settings) {
    return <div className="h-32 animate-pulse rounded-lg bg-muted/50" aria-hidden="true" />
  }

  const terminals = settings.terminals.filter((t) => t.isActive)
  const people = settings.people.filter((p) => p.isActive)

  return (
    <div className="space-y-5">
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
                isThisDevice={device?.isBorne === true && device.terminalId === terminal.id}
                onRevoked={() => void load()}
              />
            ))}
          </ul>
        )}
        <div className="flex flex-wrap items-center gap-2">
          <RegisterTerminalButton
            onRegistered={() => void load()}
            replacingTerminalId={device?.isBorne ? device.terminalId : null}
          />
          {device?.isBorne && (
            <Button asChild variant="ghost" size="sm">
              <Link href="/pointage">Ouvrir la borne</Link>
            </Button>
          )}
        </div>
      </section>

      <section aria-labelledby="pointent-title" className="space-y-2">
        <h3 id="pointent-title" className="text-sm font-medium">
          Personnes qui pointent
        </h3>
        <ul className="divide-y rounded-md border">
          {people.map((person) => (
            <PersonRow key={person.userId} person={person} onChanged={() => void load()} />
          ))}
        </ul>
      </section>
    </div>
  )
}

function TerminalRow({
  terminal,
  isThisDevice,
  onRevoked,
}: {
  terminal: AttendanceTerminalDto
  isThisDevice: boolean
  onRevoked: () => void
}) {
  const [confirm, setConfirm] = useState(false)
  const [busy, setBusy] = useState(false)

  const revoke = async () => {
    setBusy(true)
    try {
      await pointageApi.revokeTerminal(terminal.id)
      forgetBorneProbe()
      toast.success(`Borne ${quoteFr(terminal.name)} retirée.`)
      setConfirm(false)
      onRevoked()
    } catch (err) {
      showErrorToast(err, "La borne n'a pas pu être retirée.")
    } finally {
      setBusy(false)
    }
  }

  return (
    <li className="flex items-center gap-3 px-3 py-2.5">
      <MonitorSmartphone className="size-4 shrink-0 text-muted-foreground" aria-hidden="true" />
      <div className="min-w-0 flex-1">
        <p className="truncate text-sm font-medium">
          {terminal.name}
          {isThisDevice && <span className="ms-2 text-xs font-normal text-muted-foreground">cet appareil</span>}
        </p>
        <p className="text-xs text-muted-foreground">
          {terminal.lastUsedAt ? `Utilisée le ${formatDate(terminal.lastUsedAt)}` : "Jamais utilisée"}
        </p>
      </div>
      <Button
        variant="ghost"
        size="sm"
        className="shrink-0 gap-1.5 text-destructive hover:text-destructive"
        onClick={() => setConfirm(true)}
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
              Cet appareil ne pourra plus pointer. Les pointages déjà enregistrés restent.
            </AlertDialogDescription>
          </AlertDialogHeader>
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

function RegisterTerminalButton({
  onRegistered,
  replacingTerminalId,
}: {
  onRegistered: () => void
  /** The borne this device already is: renaming it registers a new one and retires this one. */
  replacingTerminalId: string | null
}) {
  const alreadyBorne = replacingTerminalId !== null
  const [open, setOpen] = useState(false)
  const [name, setName] = useState(DEFAULT_BORNE_NAME)
  const [error, setError] = useState<string | null>(null)
  const [stepUp, setStepUp] = useState(false)
  const [hasTotp, setHasTotp] = useState(true)
  const [saving, setSaving] = useState(false)
  const guard = useDirtyGuard(open, setOpen)

  useEffect(() => {
    if (!open) return
    setName(DEFAULT_BORNE_NAME)
    setError(null)
    // Unknown means both proofs are offered; the server re-checks whichever arrives.
    securityApi
      .getTotpState()
      .then((state) => setHasTotp(state.enrolledAt !== null))
      .catch(() => setHasTotp(true))
  }, [open])

  const submit = (e: React.FormEvent) => {
    e.preventDefault()
    const trimmed = name.trim()
    if (!trimmed) {
      setError("Donnez un nom à la borne.")
      return
    }
    setError(null)
    setStepUp(true)
  }

  const register = async (token: string) => {
    setSaving(true)
    try {
      const terminal = await pointageApi.registerTerminal(name.trim(), token, replacingTerminalId)
      forgetBorneProbe()
      guard.markClean()
      setOpen(false)
      toast.success(`Cet appareil est maintenant la borne ${quoteFr(terminal.name)}.`, {
        action: { label: "Ouvrir", onClick: () => window.location.assign("/pointage") },
      })
      onRegistered()
    } catch (err) {
      setError(getErrorMessage(err, "La borne n'a pas pu être enregistrée."))
    } finally {
      setSaving(false)
    }
  }

  return (
    <>
      <Button variant="outline" size="sm" className="gap-1.5" onClick={() => setOpen(true)}>
        <MonitorSmartphone className="size-4" aria-hidden="true" />
        {alreadyBorne ? "Renommer cet appareil" : "Faire de cet appareil une borne"}
      </Button>
      <Dialog open={open} onOpenChange={guard.onOpenChange}>
        <DialogContent className="md:max-w-md">
          <DialogHeader>
            <DialogTitle>Faire de cet appareil une borne</DialogTitle>
            <DialogDescription>
              Les secrétaires pointeront ici, sans se connecter, avec leur schéma.
            </DialogDescription>
          </DialogHeader>
          <DialogBody>
            <form id="register-borne-form" onSubmit={submit} noValidate className="space-y-3">
              <div className="space-y-2">
                <Label htmlFor="borne-name">Nom de la borne</Label>
                <Input
                  id="borne-name"
                  value={name}
                  maxLength={TERMINAL_NAME_MAX}
                  onChange={(e) => setName(e.target.value)}
                  autoComplete="off"
                />
              </div>
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
        purpose="Confirmez votre identité pour faire de cet appareil une borne de pointage."
        hasTotp={hasTotp}
        onConfirmed={(token) => {
          setStepUp(false)
          void register(token)
        }}
      />
    </>
  )
}

function PersonRow({ person, onChanged }: { person: AttendancePersonSettingDto; onChanged: () => void }) {
  const [busy, setBusy] = useState(false)
  const [confirmReset, setConfirmReset] = useState(false)
  const locked = person.lockedUntil !== null

  const toggle = async (next: boolean) => {
    setBusy(true)
    try {
      // No version: one idempotent switch, where the last press is the intent (« Mode discret »'s shape).
      await pointageApi.setTracked(person.userId, next, 0)
      onChanged()
    } catch (err) {
      showErrorToast(err, "Le réglage n'a pas pu être enregistré.")
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
    : locked
      ? `Bloqué après ${SCHEMA_MAX_FAILURES} essais, jusqu'à ${formatDateTime(person.lockedUntil!)}`
      : person.hasSchema
        ? "Schéma créé"
        : "Schéma à créer dans « Mon profil »"

  return (
    <li className="flex flex-wrap items-center gap-x-3 gap-y-1.5 px-3 py-2.5">
      <div className="min-w-0 flex-1">
        <Label htmlFor={`pointe-${person.userId}`} className="block truncate font-medium">
          {person.fullName}
        </Label>
        <p className="text-xs text-muted-foreground">
          {USER_ROLE_LABELS_FR[person.role as UserRole] ?? person.role}
          {schemaLine && <span className={locked ? "text-destructive" : undefined}> · {schemaLine}</span>}
        </p>
      </div>
      {person.tracked && (person.hasSchema || locked) && (
        <Button
          variant="ghost"
          size="sm"
          className="shrink-0 gap-1.5"
          disabled={busy}
          onClick={() => setConfirmReset(true)}
          aria-label={`Réinitialiser le schéma de ${person.fullName}`}
        >
          <RotateCcw className="size-4" aria-hidden="true" />
          <span className="hidden sm:inline">Réinitialiser le schéma</span>
        </Button>
      )}
      <Switch
        id={`pointe-${person.userId}`}
        checked={person.tracked}
        disabled={busy}
        onCheckedChange={(next) => void toggle(next)}
        aria-label={`${person.fullName} pointe`}
      />
      <AlertDialog open={confirmReset} onOpenChange={(open) => !busy && setConfirmReset(open)}>
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>Réinitialiser le schéma de {person.fullName} ?</AlertDialogTitle>
            <AlertDialogDescription>
              Elle devra en créer un nouveau dans « Mon profil » avant de pointer.
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
    </li>
  )
}
