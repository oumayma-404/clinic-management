"use client"

import { useEffect, useId, useState } from "react"

import { Button } from "@/components/ui/button"
import { Label } from "@/components/ui/label"
import { PasswordInput } from "@/components/ui/password-input"
import { SchemaPad } from "@/components/team/schema-pad"
import { ApiError } from "@/lib/api/client"
import { PointageErrorCode, SCHEMA_MIN_DOTS, pointageApi, type AttendancePersonSettingDto } from "@/lib/api/pointage"
import { getErrorMessage } from "@/lib/errors"
import { cn } from "@/lib/utils"

type Step = "password" | "first" | "confirm"

/** `?schema=1` opens « Créer votre schéma » on Mon profil — where the « Créez votre schéma » notification lands. */
export const SCHEMA_PARAM = "schema"
export const SCHEMA_DEEP_LINK = `/mon-profil?${SCHEMA_PARAM}=1`
/** The same, for a click while Mon profil is already open: a same-route push does not remount the card. */
export const OPEN_SCHEMA_EVENT = "clinic:open-schema"

/**
 * « Créer votre schéma » / « Changer votre schéma »: the account password first when a schéma already exists (the
 * server checks it — a colleague at a shared PC must not replace someone else's), then the schéma drawn twice.
 * Shared by Mon profil's dialog and the arrival lock, so both ask the same things in the same order.
 */
export function SchemaSetter({
  hasSchema,
  onSaved,
  onBusyChange,
  onDirtyChange,
  hideTrail = false,
  padClassName,
  className,
}: {
  /** She already has one: the password is asked before the pad. */
  hasSchema: boolean
  onSaved: (saved: AttendancePersonSettingDto) => void
  /** A save in flight — the host holds its own way out meanwhile. */
  onBusyChange?: (busy: boolean) => void
  /** Something typed or drawn that a dismiss would lose. */
  onDirtyChange?: (dirty: boolean) => void
  /** At a shared desk: no trail once drawn, as the borne does. */
  hideTrail?: boolean
  padClassName?: string
  className?: string
}) {
  const [askPassword, setAskPassword] = useState(hasSchema)
  const [step, setStep] = useState<Step>(hasSchema ? "password" : "first")
  const [password, setPassword] = useState("")
  const [first, setFirst] = useState<number[] | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [saving, setSaving] = useState(false)
  const passwordId = useId()
  const errorId = useId()

  useEffect(() => {
    onBusyChange?.(saving)
  }, [saving, onBusyChange])

  const dirty = password !== "" || first !== null
  useEffect(() => {
    onDirtyChange?.(dirty)
  }, [dirty, onDirtyChange])

  const save = async (pattern: number[]) => {
    setSaving(true)
    try {
      const saved = await pointageApi.setMySchema(pattern, askPassword ? password : undefined)
      onSaved(saved)
    } catch (err) {
      const code = err instanceof ApiError ? err.code : undefined
      setFirst(null)
      setError(getErrorMessage(err, "Le schéma n'a pas pu être enregistré."))
      if (code === PointageErrorCode.SchemaInvalid) {
        setStep("first")
      } else if (code === PointageErrorCode.SchemaPasswordWrong || code === PointageErrorCode.SchemaPasswordRequired) {
        setAskPassword(true)
        setPassword("")
        setStep("password")
      } else {
        setStep(askPassword ? "password" : "first")
      }
    } finally {
      setSaving(false)
    }
  }

  const drawn = (pattern: number[]) => {
    if (saving) return
    if (step === "first") {
      if (pattern.length < SCHEMA_MIN_DOTS) {
        setError(`Reliez au moins ${SCHEMA_MIN_DOTS} points.`)
        return
      }
      setError(null)
      setFirst(pattern)
      setStep("confirm")
      return
    }
    if (!first || pattern.join("-") !== first.join("-")) {
      setFirst(null)
      setStep("first")
      setError("Les deux schémas ne correspondent pas. Recommencez.")
      return
    }
    void save(pattern)
  }

  if (step === "password") {
    return (
      <form
        className={cn("flex w-full max-w-72 flex-col gap-3 text-start", className)}
        onSubmit={(e) => {
          e.preventDefault()
          if (!password) return
          setError(null)
          setStep("first")
        }}
      >
        <div className="space-y-1.5">
          <Label htmlFor={passwordId}>Mot de passe de votre compte</Label>
          <PasswordInput
            id={passwordId}
            autoComplete="current-password"
            value={password}
            onChange={(e) => setPassword(e.target.value)}
            required
            aria-invalid={error ? true : undefined}
            aria-describedby={error ? errorId : undefined}
          />
        </div>
        <p id={errorId} role="alert" className="min-h-5 text-sm text-destructive">
          {error}
        </p>
        <Button type="submit" size="lg" disabled={!password}>
          Continuer
        </Button>
      </form>
    )
  }

  return (
    <div className={cn("flex w-full flex-col items-center gap-3", className)}>
      <p className="text-base font-medium" aria-live="polite">
        {step === "first" ? "Tracez votre schéma" : "Tracez-le à nouveau"}
      </p>
      <SchemaPad
        key={step}
        label="Votre schéma de pointage"
        onComplete={drawn}
        disabled={saving}
        error={error !== null}
        hideTrail={hideTrail}
        autoFocus={step === "confirm"}
        className={padClassName}
      />
      <p role="alert" className="min-h-5 text-center text-sm text-destructive">
        {saving ? "" : error}
      </p>
    </div>
  )
}
