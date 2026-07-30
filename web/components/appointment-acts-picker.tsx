"use client"

import { useMemo, useState } from "react"
import { Badge } from "@/components/ui/badge"
import { Button } from "@/components/ui/button"
import { Input } from "@/components/ui/input"
import { Label } from "@/components/ui/label"
import {
  Command, CommandEmpty, CommandGroup, CommandInput, CommandItem, CommandList,
} from "@/components/ui/command"
import { Popover, PopoverContent, PopoverTrigger } from "@/components/ui/popover"
import { Check, ChevronsUpDown, Clock, Plus, Stethoscope, X } from "lucide-react"
import { cn } from "@/lib/utils"
import { procedureTypesApi } from "@/lib/api/procedure-types"
import { ApiError } from "@/lib/api/client"
import type { ProcedureTypeDto } from "@/lib/api/types"

/**
 * One act chosen for a séance. `treatmentPlanItemId` is what makes grouping meaningful: a devis act booked
 * alongside others keeps its own link, so the plan reports each of them as planned rather than only the first.
 */
export interface SelectedAct {
  /**
   * The catalog act. **Null** for a link-only row: a hand-typed devis line has no `ProcedureType` behind it, and
   * it still belongs in the séance — dropping it would book the visit and leave that step reading « À planifier ».
   */
  procedureTypeId: string | null
  treatmentPlanItemId?: string | null
  /** Why this act is in the list, when it came from a devis — shown as a « devis » chip. */
  planLabel?: string
  /** Name for a link-only row, since there is no catalog entry to read one from. */
  fallbackName?: string
}

/**
 * Seed colours rotated by catalog size for an act typed on the fly. A subset of the palette the backend
 * `ColorHex` value object accepts (`GET /procedure-types/colors` is the authority) — a rotation seed, not a
 * picker, so it does not fetch: the user is booking a visit, not designing a catalogue.
 */
const CUSTOM_PROCEDURE_COLORS = ["#4F83CC", "#2A9D8F", "#6BAA75", "#9B8EDC", "#E9A23B", "#E76F51"]

/** Mirrors the server's own cap (`AppointmentProcedureSelection.MaxProceduresPerAppointment`). */
const MAX_ACTS = 12

interface AppointmentActsPickerProps {
  /** Active catalog. The picker never fetches it — both dialogs already load it for other reasons. */
  procedureTypes: ProcedureTypeDto[]
  loading?: boolean
  /** Why the catalog is empty, if it failed to load — an empty list and a failed call are different facts. */
  error?: string | null
  onRetry?: () => void
  value: SelectedAct[]
  onChange: (acts: SelectedAct[]) => void
  disabled?: boolean
  /** Called when an act is created on the fly, so the parent can fold it into its catalog state. */
  onProcedureCreated?: (created: ProcedureTypeDto) => void
  /** Fallback duration for a created act with no typical duration of its own. */
  fallbackDurationMinutes?: number
  /** Acts that came from a devis and must stay in the séance (removing one is « ne pas le planifier »). */
  idPrefix?: string
}

/**
 * « Actes du rendez-vous » — the séance's act list.
 *
 * <p>Replaces the single « Type d'acte » Select that both booking dialogs carried. A visit is routinely several
 * acts, and with one dropdown the second and third could only be typed into the notes: invisible to the duration,
 * to the colour, to the fiche de soins proposal and to the devis.</p>
 *
 * <p>Shared by create and edit rather than duplicated, so the inline « acte personnalisé » path — which only the
 * create dialog had — now exists on both, and the total-duration rule has one implementation.</p>
 */
export function AppointmentActsPicker({
  procedureTypes,
  loading = false,
  error = null,
  onRetry,
  value,
  onChange,
  disabled = false,
  onProcedureCreated,
  fallbackDurationMinutes = 30,
  idPrefix = "appt-acts",
}: AppointmentActsPickerProps) {
  const [pickerOpen, setPickerOpen] = useState(false)
  const [customMode, setCustomMode] = useState(false)
  const [customName, setCustomName] = useState("")
  const [customDuration, setCustomDuration] = useState("")
  const [customCost, setCustomCost] = useState("")
  const [creating, setCreating] = useState(false)
  const [customError, setCustomError] = useState<string | null>(null)

  const byId = useMemo(
    () => new Map(procedureTypes.map((p) => [p.id, p])),
    [procedureTypes],
  )

  /**
   * The chosen acts, resolved against the catalog. A row whose procedure is no longer in the active catalog is
   * **kept and marked**, never dropped: silently removing it would change what the user is about to save without
   * telling them, and on the edit dialog that means deleting an act from a booked visit.
   */
  const rows = useMemo(
    () =>
      value.map((act) => {
        // Link-only row: no catalogue entry, so no duration and no colour of its own. Not an error state — it is
        // a devis line the clinic never turned into a catalog act.
        if (!act.procedureTypeId) {
          return {
            act,
            name: act.fallbackName ?? "Acte du devis",
            durationMinutes: null,
            colorHex: "#6C757D",
            missing: false,
          }
        }
        const pt = byId.get(act.procedureTypeId)
        return {
          act,
          name: pt?.name ?? act.fallbackName ?? "Acte indisponible",
          durationMinutes: pt?.defaultDurationMinutes ?? null,
          colorHex: pt?.colorHex ?? "#6C757D",
          missing: !pt,
        }
      }),
    [value, byId],
  )

  const totalMinutes = rows.reduce((sum, r) => sum + (r.durationMinutes ?? 0), 0)
  const selectedIds = useMemo(
    () => new Set(value.map((a) => a.procedureTypeId).filter((id): id is string => id !== null)),
    [value],
  )
  const atCap = value.length >= MAX_ACTS

  const addAct = (procedureTypeId: string) => {
    // The server refuses a duplicate by name; refusing it here too keeps the list honest without a round trip.
    if (selectedIds.has(procedureTypeId)) return
    onChange([...value, { procedureTypeId }])
  }

  const removeAt = (index: number) => onChange(value.filter((_, i) => i !== index))

  const handleCreateCustom = async () => {
    setCustomError(null)
    const name = customName.trim()
    if (!name) {
      setCustomError("Le nom de l'acte est requis")
      return
    }
    // Unique per clinic server-side; catching the common case here avoids a round-trip 400.
    const existing = procedureTypes.find((pt) => pt.name.trim().toLowerCase() === name.toLowerCase())
    if (existing) {
      setCustomError(
        `Un acte nommé « ${existing.name} » existe déjà. Choisissez-le dans la liste ou utilisez un autre nom.`,
      )
      return
    }

    const typed = customDuration ? Number(customDuration) : NaN
    const inferred = Number.isFinite(typed) && typed > 0 ? Math.floor(typed) : fallbackDurationMinutes
    const durationMinutes = Math.min(479, Math.max(1, inferred || 30))
    const cost = customCost ? Number.parseFloat(customCost) : null
    if (cost !== null && (Number.isNaN(cost) || cost < 0)) {
      setCustomError("Le montant est invalide")
      return
    }

    setCreating(true)
    try {
      const colorHex = CUSTOM_PROCEDURE_COLORS[procedureTypes.length % CUSTOM_PROCEDURE_COLORS.length]
      const created = await procedureTypesApi.create({
        name,
        defaultDurationMinutes: durationMinutes,
        defaultCost: cost,
        colorHex,
      })
      onProcedureCreated?.(created)
      onChange([...value, { procedureTypeId: created.id }])
      setCustomMode(false)
      setCustomName("")
      setCustomDuration("")
      setCustomCost("")
    } catch (err) {
      const message = err instanceof ApiError ? err.message : ""
      if (/already exists|existe déjà/i.test(message)) {
        setCustomError(`Un acte nommé « ${name} » existe déjà. Choisissez-le dans la liste ou utilisez un autre nom.`)
      } else {
        setCustomError(message || "Échec de la création de l'acte")
      }
    } finally {
      setCreating(false)
    }
  }

  return (
    <div className="space-y-3">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <Label htmlFor={`${idPrefix}-add`} className="text-sm">
          Actes du rendez-vous
        </Label>
        {/* The count and the summed duration together, because the summed duration is the reason the count
            matters: it is what the visit will be booked for. */}
        {rows.length > 0 && (
          <Badge variant="secondary" className="gap-1">
            <Clock className="h-3 w-3" />
            {rows.length} acte{rows.length > 1 ? "s" : ""}
            {totalMinutes > 0 ? ` · ${totalMinutes} min` : ""}
          </Badge>
        )}
      </div>

      {rows.length > 0 && (
        <ul className="space-y-1.5">
          {rows.map((row, index) => (
            <li
              key={`${row.act.procedureTypeId ?? row.act.treatmentPlanItemId ?? "act"}-${index}`}
              className="flex items-center gap-2 rounded-md border bg-background px-3 py-2"
            >
              <span
                className="h-3 w-3 shrink-0 rounded-full"
                style={{ backgroundColor: row.colorHex }}
                aria-hidden
              />
              <span className={cn("min-w-0 flex-1 truncate text-sm", row.missing && "text-muted-foreground italic")}>
                {row.name}
              </span>
              {row.act.planLabel && (
                <Badge variant="outline" className="hidden shrink-0 gap-1 text-xs sm:inline-flex">
                  <Stethoscope className="h-3 w-3" />
                  {row.act.planLabel}
                </Badge>
              )}
              {row.durationMinutes != null && (
                <span className="shrink-0 text-xs tabular-nums text-muted-foreground">
                  {row.durationMinutes} min
                </span>
              )}
              <Button
                type="button"
                variant="ghost"
                size="icon"
                className="h-7 w-7 shrink-0"
                aria-label={`Retirer « ${row.name} » du rendez-vous`}
                disabled={disabled}
                onClick={() => removeAt(index)}
              >
                <X className="h-4 w-4" />
              </Button>
            </li>
          ))}
        </ul>
      )}

      {/*
        Searchable, because a clinic's catalogue is long and a Select that lists all of it is unscannable.
        `modal` on the Popover: the parent Dialog disables pointer events outside its content, so a non-modal
        Popover portalled to <body> inherits pointer-events:none and its items can only be keyboard-selected.

        **The first act is the chooser itself.** With no acts yet this renders as an ordinary select box — same
        height, same placeholder, same chevron as every other field in the dialog — because choosing the act is
        the expected next step, not an extra one. An empty list behind an « Ajouter un acte » button made the
        common single-act booking cost one more click than it used to. Only from the second act on does it become
        a compact « Ajouter un autre acte », which is where an explicit add really is the intent.
      */}
      <Popover open={pickerOpen} onOpenChange={setPickerOpen} modal>
        <PopoverTrigger asChild>
          <Button
            id={`${idPrefix}-add`}
            type="button"
            variant="outline"
            size={rows.length === 0 ? "default" : "sm"}
            className={cn(
              "w-full font-normal",
              rows.length === 0 ? "h-10 justify-between" : "h-9 justify-start gap-2",
            )}
            disabled={disabled || loading || atCap}
            aria-expanded={pickerOpen}
          >
            {rows.length === 0 ? (
              <>
                <span className={cn("truncate", !loading && "text-muted-foreground")}>
                  {loading ? "Chargement des actes…" : "Sélectionner un type d'acte"}
                </span>
                <ChevronsUpDown className="ml-2 h-4 w-4 shrink-0 opacity-50" />
              </>
            ) : (
              <>
                <Plus className="h-4 w-4" />
                {loading
                  ? "Chargement des actes…"
                  : atCap
                    ? `Maximum ${MAX_ACTS} actes atteint`
                    : "Ajouter un autre acte…"}
              </>
            )}
          </Button>
        </PopoverTrigger>
        <PopoverContent className="p-0" align="start" style={{ width: "var(--radix-popover-trigger-width)" }}>
          <Command>
            <CommandInput placeholder="Rechercher un acte…" />
            <CommandList>
              <CommandEmpty>Aucun acte trouvé.</CommandEmpty>
              <CommandGroup>
                {procedureTypes.map((pt) => {
                  const already = selectedIds.has(pt.id)
                  return (
                    <CommandItem
                      key={pt.id}
                      value={pt.name}
                      // Kept visible but ticked rather than filtered out: an act vanishing from the list the
                      // moment it is picked reads as "it failed", and the tick is what says it is already in.
                      onSelect={() => {
                        if (!already) addAct(pt.id)
                        setPickerOpen(false)
                      }}
                    >
                      <Check className={cn("mr-2 h-4 w-4", already ? "opacity-100" : "opacity-0")} />
                      <span
                        className="mr-2 h-3 w-3 rounded-full"
                        style={{ backgroundColor: pt.colorHex }}
                        aria-hidden
                      />
                      <span className="flex-1 truncate">{pt.name}</span>
                      <span className="ml-2 text-xs tabular-nums text-muted-foreground">
                        {pt.defaultDurationMinutes} min
                      </span>
                    </CommandItem>
                  )
                })}
                <CommandItem
                  value="__acte personnalisé nouveau__"
                  onSelect={() => {
                    setPickerOpen(false)
                    setCustomMode(true)
                    setCustomError(null)
                  }}
                >
                  <Plus className="mr-2 h-4 w-4" />
                  <span className="font-medium">Acte personnalisé…</span>
                </CommandItem>
              </CommandGroup>
            </CommandList>
          </Command>
        </PopoverContent>
      </Popover>

      {error && (
        <p role="status" className="flex flex-wrap items-center gap-2 text-xs text-destructive">
          <span>{error}</span>
          {onRetry && (
            <button
              type="button"
              onClick={onRetry}
              className="underline underline-offset-2 hover:no-underline"
            >
              Réessayer
            </button>
          )}
        </p>
      )}

      {customMode && (
        <div className="space-y-3 rounded-md border bg-background p-3">
          <p className="text-sm font-medium">Nouvel acte personnalisé</p>
          {customError && <p className="text-xs text-red-600 dark:text-red-400">{customError}</p>}
          <div className="grid gap-3 sm:grid-cols-[1fr_120px_140px]">
            <div className="space-y-1">
              <Label htmlFor={`${idPrefix}-custom-name`} className="text-xs text-muted-foreground">
                Nom *
              </Label>
              <Input
                id={`${idPrefix}-custom-name`}
                value={customName}
                onChange={(e) => setCustomName(e.target.value)}
                onKeyDown={(e) => {
                  if (e.key === "Enter") {
                    e.preventDefault()
                    void handleCreateCustom()
                  }
                }}
                placeholder="Nom de l'acte"
                className="h-9"
                disabled={creating}
                autoFocus
              />
            </div>
            <div className="space-y-1">
              <Label htmlFor={`${idPrefix}-custom-duration`} className="text-xs text-muted-foreground">
                Durée (min)
              </Label>
              <Input
                id={`${idPrefix}-custom-duration`}
                type="number"
                min="1"
                max="479"
                value={customDuration}
                onChange={(e) => setCustomDuration(e.target.value)}
                onKeyDown={(e) => {
                  if (e.key === "Enter") {
                    e.preventDefault()
                    void handleCreateCustom()
                  }
                }}
                placeholder="auto"
                className="h-9"
                disabled={creating}
              />
            </div>
            <div className="space-y-1">
              <Label htmlFor={`${idPrefix}-custom-cost`} className="text-xs text-muted-foreground">
                Montant
              </Label>
              <div className="relative">
                <span className="absolute left-2 top-1/2 -translate-y-1/2 text-xs text-muted-foreground">DT</span>
                <Input
                  id={`${idPrefix}-custom-cost`}
                  type="number"
                  min="0"
                  step="0.01"
                  value={customCost}
                  onChange={(e) => setCustomCost(e.target.value)}
                  onKeyDown={(e) => {
                    if (e.key === "Enter") {
                      e.preventDefault()
                      void handleCreateCustom()
                    }
                  }}
                  placeholder="0.00"
                  className="h-9 pl-8"
                  disabled={creating}
                />
              </div>
            </div>
          </div>
          <div className="flex items-center justify-between gap-3">
            <p className="text-[11px] text-muted-foreground">
              Durée et montant optionnels. Sans durée, {fallbackDurationMinutes} min est utilisé.
            </p>
            <div className="flex shrink-0 gap-2">
              <Button
                type="button"
                variant="ghost"
                size="sm"
                className="h-8"
                onClick={() => {
                  setCustomMode(false)
                  setCustomError(null)
                }}
                disabled={creating}
              >
                Annuler
              </Button>
              <Button type="button" size="sm" className="h-8" onClick={handleCreateCustom} disabled={creating}>
                {creating ? "Ajout…" : "Ajouter"}
              </Button>
            </div>
          </div>
        </div>
      )}
    </div>
  )
}

/**
 * Total booked duration of a séance, in minutes — the default length the dialogs pre-fill. Link-only acts
 * contribute nothing, because nothing anywhere knows how long a hand-typed devis line takes.
 */
export function totalActsDuration(acts: SelectedAct[], procedureTypes: ProcedureTypeDto[]): number {
  const byId = new Map(procedureTypes.map((p) => [p.id, p]))
  return acts.reduce(
    (sum, a) => sum + (a.procedureTypeId ? byId.get(a.procedureTypeId)?.defaultDurationMinutes ?? 0 : 0),
    0,
  )
}
