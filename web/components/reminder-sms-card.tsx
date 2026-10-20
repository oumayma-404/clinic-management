"use client"

import { useEffect, useRef, useState } from "react"
import { toast } from "sonner"
import { ChevronDown, Loader2, MessageSquareText } from "lucide-react"

import { Badge } from "@/components/ui/badge"
import { Button } from "@/components/ui/button"
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card"
import { FormErrorBanner } from "@/components/ui/form-error-banner"
import { Label } from "@/components/ui/label"
import { Switch } from "@/components/ui/switch"
import { Textarea } from "@/components/ui/textarea"
import {
  reminderSettingsApi,
  updateRequestFrom,
  type ReminderPreviewDto,
  type ReminderSettingsDto,
} from "@/lib/api/reminder-settings"
import { useConflict } from "@/lib/hooks/use-conflict"
import { cn } from "@/lib/utils"

interface ReminderSmsCardProps {
  /** The settings page's own icon-chip classes, so this card reads as one of its sections. */
  chipClassName: string
}

/** The anchor « Rappels » links to, which also opens the card. */
export const REMINDER_SMS_ANCHOR = "rappels-sms"

const LEAD_TIME_LABELS: Record<number, string> = {
  48: "2 jours avant",
  24: "La veille",
  12: "12 h avant",
  6: "6 h avant",
  3: "3 h avant",
  2: "2 h avant",
}

const VARIABLE_LABELS: Record<string, string> = {
  "{patient}": "Patient",
  "{date}": "Date et heure",
  "{acte}": "Acte",
  "{clinic}": "Cabinet",
}

/** A new opt-in starts on one SMS the day before. */
const DEFAULT_LEAD_TIMES = [24]

/**
 * « Rappels SMS » — whether the cabinet sends its patients an SMS before each appointment, when, and in which words.
 *
 * <p>The text is counted by the server, with the same renderer that sends it, so « 112 / 157 » is what the patient
 * is billed for: one SMS is 157 characters and the save refuses a wording that could exceed it once a long name and
 * the acts are filled in. Switching SMS on here is the opt-in; consent per patient lives on the patient's file.</p>
 *
 * <p>⚠️ The PUT replaces every field it is given, so a save starts from `updateRequestFrom(settings)` and overrides
 * only the three fields this card owns — the channels sheet on « Rappels » does the mirror image.</p>
 */
export function ReminderSmsCard({ chipClassName }: ReminderSmsCardProps) {
  const [collapsed, setCollapsed] = useState(true)
  const [settings, setSettings] = useState<ReminderSettingsDto | null>(null)
  const [loadFailed, setLoadFailed] = useState(false)
  const [saving, setSaving] = useState(false)
  const conflict = useConflict()

  const [enabled, setEnabled] = useState(false)
  const [leadTimes, setLeadTimes] = useState<number[]>(DEFAULT_LEAD_TIMES)
  const [message, setMessage] = useState("")
  const [preview, setPreview] = useState<ReminderPreviewDto | null>(null)
  const [previewFailed, setPreviewFailed] = useState(false)
  const textareaRef = useRef<HTMLTextAreaElement>(null)

  const hydrate = (dto: ReminderSettingsDto) => {
    setSettings(dto)
    setEnabled(dto.smsEnabled === true)
    setLeadTimes(dto.leadTimeHours?.length ? dto.leadTimeHours : DEFAULT_LEAD_TIMES)
    setMessage(dto.messageTemplateBody ?? dto.defaultMessageTemplate)
  }

  const load = async () => {
    setLoadFailed(false)
    try {
      hydrate(await reminderSettingsApi.get())
      conflict.reset()
    } catch {
      setLoadFailed(true)
    }
  }

  useEffect(() => {
    if (window.location.hash === `#${REMINDER_SMS_ANCHOR}`) setCollapsed(false)
    void load()
    // eslint-disable-next-line react-hooks/exhaustive-deps -- load once; `load` is stable in intent
  }, [])

  // The count and the sample come from the server, debounced, so the editor restates none of the rules.
  useEffect(() => {
    if (!settings) return
    let cancelled = false
    const handle = window.setTimeout(async () => {
      try {
        const result = await reminderSettingsApi.preview(message)
        if (!cancelled) {
          setPreview(result)
          setPreviewFailed(false)
        }
      } catch {
        if (!cancelled) setPreviewFailed(true)
      }
    }, 300)
    return () => {
      cancelled = true
      window.clearTimeout(handle)
    }
  }, [message, settings])

  const toggleLeadTime = (hours: number) =>
    setLeadTimes((current) =>
      current.includes(hours) ? current.filter((h) => h !== hours) : [...current, hours].sort((a, b) => b - a),
    )

  // Inserted as a word: a space before it unless the caret follows a space or a line break, and one after it
  // unless what follows already separates it — « …en avance.{acte} » read « …en avance.Détartrage ».
  const insertVariable = (variable: string) => {
    const field = textareaRef.current
    const start = field?.selectionStart ?? message.length
    const end = field?.selectionEnd ?? message.length
    const before = message.slice(0, start)
    const after = message.slice(end)
    const lead = before.length > 0 && !/\s$/.test(before) ? " " : ""
    const trail = after.length > 0 && !/^[\s.,;:!?)]/.test(after) ? " " : ""
    const inserted = lead + variable + trail
    setMessage(before + inserted + after)
    const caret = start + lead.length + variable.length
    requestAnimationFrame(() => {
      field?.focus()
      field?.setSelectionRange(caret, caret)
    })
  }

  const save = async () => {
    if (!settings) return
    setSaving(true)
    conflict.clearMessage()
    try {
      const wording = message.trim()
      const updated = await reminderSettingsApi.update({
        ...updateRequestFrom(settings),
        version: settings.version,
        smsEnabled: enabled,
        leadTimeHours: leadTimes,
        // The default wording is stored as « nothing », so a later improvement of the default reaches this cabinet.
        messageTemplateBody: wording === settings.defaultMessageTemplate ? null : wording || null,
      })
      hydrate(updated)
      conflict.reset()
      toast.success(enabled ? "Rappels SMS activés" : "Rappels SMS enregistrés")
    } catch (error) {
      conflict.capture(error, "L'enregistrement a échoué.")
    } finally {
      setSaving(false)
    }
  }

  const max = settings?.smsMaxLength ?? 157
  const maxLeadTimes = settings?.maxLeadTimes ?? 2
  const overLimit = preview !== null && preview.usedLength > preview.maxLength
  const refusal = preview?.refusal ?? null
  const isDefaultWording = settings !== null && message.trim() === settings.defaultMessageTemplate

  return (
    <Card id={REMINDER_SMS_ANCHOR} className="scroll-mt-20">
      <CardHeader className="pb-3">
        <button
          type="button"
          onClick={() => setCollapsed((value) => !value)}
          aria-expanded={!collapsed}
          className="touch-target flex w-full flex-wrap items-center gap-2.5 py-2 text-left transition-opacity hover:opacity-70"
        >
          <CardTitle className="flex min-w-0 flex-1 items-center gap-2.5 text-base leading-snug">
            <span aria-hidden="true" className={chipClassName}>
              <MessageSquareText className="size-4" strokeWidth={1.75} />
            </span>
            Rappels SMS
          </CardTitle>
          {settings && (
            <Badge variant="outline" className="font-normal">
              {settings.smsEnabled === true ? "Activés" : "Désactivés"}
            </Badge>
          )}
          <ChevronDown
            className={cn("size-4 shrink-0 text-muted-foreground transition-transform", collapsed && "-rotate-90")}
          />
        </button>
      </CardHeader>

      {!collapsed && (
        <CardContent className="space-y-5">
          {loadFailed ? (
            <div className="flex flex-wrap items-center gap-3 text-sm" role="alert">
              <span>Les réglages des rappels n&apos;ont pas pu être chargés.</span>
              <Button variant="outline" size="sm" onClick={() => void load()}>
                Réessayer
              </Button>
            </div>
          ) : !settings ? (
            <Loader2 className="size-5 animate-spin text-muted-foreground" aria-label="Chargement" />
          ) : (
            <>
              <div className="flex items-center justify-between gap-4">
                <Label htmlFor="sms-reminders-enabled" className="text-sm font-medium leading-snug">
                  Envoyer un SMS aux patients avant leur rendez-vous
                </Label>
                <Switch
                  id="sms-reminders-enabled"
                  checked={enabled}
                  onCheckedChange={setEnabled}
                  disabled={saving}
                  className="coarse:scale-125"
                />
              </div>

              <fieldset className="space-y-2" disabled={saving}>
                <legend className="text-sm font-medium">
                  Quand l&apos;envoyer{" "}
                  <span className="font-normal text-muted-foreground">({maxLeadTimes} au plus)</span>
                </legend>
                <div className="flex flex-wrap gap-2">
                  {settings.allowedLeadTimeHours.map((hours) => {
                    const selected = leadTimes.includes(hours)
                    const atMax = !selected && leadTimes.length >= maxLeadTimes
                    return (
                      <Button
                        key={hours}
                        type="button"
                        size="sm"
                        variant={selected ? "default" : "outline"}
                        aria-pressed={selected}
                        disabled={atMax}
                        onClick={() => toggleLeadTime(hours)}
                        className="coarse:min-h-11"
                      >
                        {LEAD_TIME_LABELS[hours] ?? `${hours} h avant`}
                      </Button>
                    )
                  })}
                </div>
                {leadTimes.length === 0 && (
                  <p role="alert" className="text-xs text-destructive">
                    Choisissez au moins un moment d&apos;envoi.
                  </p>
                )}
              </fieldset>

              <div className="space-y-2">
                <div className="flex flex-wrap items-end justify-between gap-2">
                  <Label htmlFor="sms-reminder-message" className="text-sm font-medium">
                    Message
                  </Label>
                  {!isDefaultWording && (
                    <Button
                      type="button"
                      variant="ghost"
                      size="sm"
                      className="h-auto px-1.5 py-1 text-xs coarse:min-h-11"
                      onClick={() => setMessage(settings.defaultMessageTemplate)}
                      disabled={saving}
                    >
                      Message par défaut
                    </Button>
                  )}
                </div>

                <div className="flex flex-wrap gap-1.5" role="group" aria-label="Insérer une information">
                  {settings.messageVariables.map((variable) => (
                    <Button
                      key={variable}
                      type="button"
                      variant="secondary"
                      size="sm"
                      className="h-7 px-2 text-xs coarse:min-h-11"
                      onClick={() => insertVariable(variable)}
                      disabled={saving}
                      aria-label={`Insérer ${VARIABLE_LABELS[variable] ?? variable}`}
                    >
                      + {VARIABLE_LABELS[variable] ?? variable}
                    </Button>
                  ))}
                </div>

                <Textarea
                  id="sms-reminder-message"
                  ref={textareaRef}
                  value={message}
                  onChange={(event) => setMessage(event.target.value)}
                  maxLength={max}
                  rows={4}
                  disabled={saving}
                  aria-invalid={refusal !== null}
                  aria-describedby="sms-reminder-count"
                  className="md:text-sm"
                />

                <div className="flex flex-wrap items-start justify-between gap-x-4 gap-y-1">
                  <p
                    role="alert"
                    className={cn("min-w-0 flex-1 text-xs", refusal ? "text-destructive" : "sr-only")}
                  >
                    {refusal ?? ""}
                  </p>
                  <p
                    id="sms-reminder-count"
                    className={cn(
                      "shrink-0 font-mono text-xs tabular-nums",
                      overLimit ? "text-destructive" : "text-muted-foreground",
                    )}
                  >
                    {preview ? `${preview.usedLength} / ${preview.maxLength} caractères` : `… / ${max} caractères`}
                  </p>
                </div>
              </div>

              <div className="space-y-1.5">
                <p className="text-sm font-medium" id="sms-reminder-preview-label">
                  Aperçu
                </p>
                <div
                  role="status"
                  aria-labelledby="sms-reminder-preview-label"
                  className="max-w-sm rounded-2xl rounded-ss-sm bg-muted px-3.5 py-2.5 text-sm leading-relaxed whitespace-pre-line break-words"
                >
                  {previewFailed && !preview ? "Aperçu indisponible." : (preview?.text ?? "…")}
                </div>
              </div>

              <FormErrorBanner
                message={conflict.error}
                action={conflict.isConflict ? { label: "Recharger", onClick: () => void load() } : undefined}
              />

              <div className="flex justify-end">
                <Button
                  onClick={() => void save()}
                  disabled={saving || refusal !== null || leadTimes.length === 0}
                  className="coarse:min-h-11"
                >
                  {saving && <Loader2 className="size-4 animate-spin" aria-hidden="true" />}
                  Enregistrer
                </Button>
              </div>
            </>
          )}
        </CardContent>
      )}
    </Card>
  )
}
