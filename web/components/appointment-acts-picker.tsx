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
import { groupProceduresByCategory } from "@/components/procedure-categories"
import { procedureTypesApi } from "@/lib/api/procedure-types"
import { ApiError } from "@/lib/api/client"
import { formatAmount, parseAmountInput, quoteFr } from "@/lib/format"
import type { AppointmentProcedurePayload } from "@/lib/api/appointments"
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
  /**
   * The price agreed for this act at this visit, **as typed** — a raw string, not a number, because « 90,500 »
   * is how this product prints money and `parseAmountInput` is what reads that back. A `type="number"` input
   * refuses the comma outright.
   *
   * <p>⚠️ `undefined` means **untouched**, and is not the same as `""`. Untouched shows the catalogue tarif in
   * the field and sends *nothing*, so the act stays at whatever its tarif is on the day the fiche is filled.
   * Prefilling the value into this field instead would freeze today's catalogue price onto every booking anyone
   * ever makes, and « personne n'a négocié » would become unsayable.</p>
   */
  agreedCost?: string
}

/**
 * The agreed price of one act as a number, or null when none was negotiated (or the field was cleared, which is
 * the same statement: leave it at the tarif).
 */
export function agreedCostOf(act: SelectedAct): number | null {
  if (act.agreedCost === undefined || act.agreedCost.trim() === "") return null
  const parsed = parseAmountInput(act.agreedCost)
  return Number.isFinite(parsed) ? parsed : null
}

/** True when a typed price cannot be read as money, or is negative — the server refuses both. */
export function hasInvalidAgreedCost(act: SelectedAct): boolean {
  if (act.agreedCost === undefined || act.agreedCost.trim() === "") return false
  const parsed = parseAmountInput(act.agreedCost)
  return !Number.isFinite(parsed) || parsed < 0
}

/**
 * What the séance costs at the prices typed into it, or **null** when nothing was negotiated.
 *
 * <p>Null when no act carries a price of its own, so the récapitulatif states a figure only when there is one to
 * verify. An act left at its tarif inside a séance where another was negotiated still contributes its tarif —
 * the total the patient was quoted is the whole séance, not the discounted part of it, which is why the
 * catalogue is a parameter here rather than something this module goes looking for.</p>
 */
export function negotiatedTotalOf(acts: SelectedAct[], procedureTypes: ProcedureTypeDto[]): number | null {
  if (!acts.some((a) => agreedCostOf(a) != null)) return null

  const byId = new Map(procedureTypes.map((p) => [p.id, p]))
  return acts.reduce((sum, act) => {
    const agreed = agreedCostOf(act)
    if (agreed != null) return sum + agreed
    const tariff = act.procedureTypeId ? byId.get(act.procedureTypeId)?.defaultCost : null
    return sum + (tariff ?? 0)
  }, 0)
}

/**
 * The acts as the API wants them. Exported and shared rather than built inline in each booking dialog, for the
 * reason `AppointmentProcedureMapping` is shared server-side: the two must agree. A dialog that assembled
 * `procedures` without `agreedCost` would silently restore every act of the visit to its catalogue tarif,
 * because the server replaces the whole list.
 */
export function toProcedurePayloads(acts: SelectedAct[]): AppointmentProcedurePayload[] {
  return acts.map((act) => ({
    procedureTypeId: act.procedureTypeId,
    treatmentPlanItemId: act.treatmentPlanItemId ?? null,
    agreedCost: agreedCostOf(act),
  }))
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
            // A hand-typed devis line has no catalogue tarif to fall back on, so there is nothing to prefill and
            // nothing to « remettre au tarif » — its price line starts empty.
            tariff: null,
          }
        }
        const pt = byId.get(act.procedureTypeId)
        return {
          act,
          name: pt?.name ?? act.fallbackName ?? "Acte indisponible",
          durationMinutes: pt?.defaultDurationMinutes ?? null,
          colorHex: pt?.colorHex ?? "#6C757D",
          missing: !pt,
          tariff: pt?.defaultCost ?? null,
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
  // Shared with the fiche's catalogue picker so both agree on which discipline an act belongs to and in what
  // order the disciplines appear.
  const procedureGroups = useMemo(() => groupProceduresByCategory(procedureTypes), [procedureTypes])

  const addAct = (procedureTypeId: string) => {
    // The server refuses a duplicate by name; refusing it here too keeps the list honest without a round trip.
    if (selectedIds.has(procedureTypeId)) return
    onChange([...value, { procedureTypeId }])
  }

  const removeAt = (index: number) => onChange(value.filter((_, i) => i !== index))

  /** `undefined` puts the row back to « rien de négocié » — the field shows the tarif again and sends nothing. */
  const setAgreedCost = (index: number, next: string | undefined) =>
    onChange(value.map((act, i) => (i === index ? { ...act, agreedCost: next } : act)))

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
        `Un acte nommé ${quoteFr(existing.name)} existe déjà. Choisissez-le dans la liste ou utilisez un autre nom.`,
      )
      return
    }

    const typed = customDuration ? Number(customDuration) : NaN
    const inferred = Number.isFinite(typed) && typed > 0 ? Math.floor(typed) : fallbackDurationMinutes
    const durationMinutes = Math.min(479, Math.max(1, inferred || 30))
    const cost = customCost.trim() ? parseAmountInput(customCost) : null
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
        setCustomError(`Un acte nommé ${quoteFr(name)} existe déjà. Choisissez-le dans la liste ou utilisez un autre nom.`)
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
              className="rounded-md border bg-background px-3 py-2"
            >
              <div className="flex items-center gap-2">
              <span
                className="h-3 w-3 shrink-0 rounded-full"
                style={{ backgroundColor: row.colorHex }}
                aria-hidden
              />
              {/* Wraps, never truncates. The row is `flex items-center gap-2 px-3 py-2` with a 12px dot, a
                  `shrink-0` « N min » span and an `h-7 w-7` remove button, leaving ~170px at 390px — so
                  « Obturation composite deux faces » clipped to « Obturation composi… » and nothing else in
                  the row says which act is about to be booked. The act's name IS the row's identity. */}
              <span
                className={cn(
                  "min-w-0 flex-1 text-sm [overflow-wrap:anywhere]",
                  row.missing && "text-muted-foreground italic",
                )}
              >
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
                aria-label={`Retirer ${quoteFr(row.name)} du rendez-vous`}
                disabled={disabled}
                onClick={() => removeAt(index)}
              >
                <X className="h-4 w-4" />
              </Button>
              </div>

              {/*
                ⚠️ Its own line, not another cell on the identity row. That row already wraps rather than
                truncates at 390 px — the act's name IS the row's identity — and squeezing a ~7rem money field
                beside it would take the name back below the width that made it readable.

                « Prix pour ce rendez-vous », never « Prix » alone: the panel below can also create a catalogue
                act with a price, and that one changes the tarif for every future visit. Two money fields a
                thumb's width apart, one local and one permanent, is a mistake nobody would notice making.
              */}
              <div className="mt-1.5 flex flex-wrap items-center gap-x-2 gap-y-1">
                <Label
                  htmlFor={`${idPrefix}-price-${index}`}
                  className="shrink-0 text-2xs font-normal text-muted-foreground"
                >
                  Prix pour ce rendez-vous
                </Label>
                <div className="relative">
                  <span className="pointer-events-none absolute left-2 top-1/2 -translate-y-1/2 text-2xs text-muted-foreground">
                    DT
                  </span>
                  <Input
                    id={`${idPrefix}-price-${index}`}
                    // `text` + `inputMode="decimal"`, matching the fiche's own tarif field: `type="number"`
                    // refuses the comma this product prints money with, so « 90,500 » could not be typed at all.
                    type="text"
                    inputMode="decimal"
                    className={cn(
                      "h-8 w-28 ps-7 text-xs tabular-nums",
                      hasInvalidAgreedCost(row.act) && "border-destructive",
                    )}
                    // Untouched shows the tarif without claiming it was agreed — see `SelectedAct.agreedCost`.
                    value={
                      row.act.agreedCost ?? (row.tariff != null ? formatAmount(row.tariff) : "")
                    }
                    onChange={(e) => setAgreedCost(index, e.target.value)}
                    disabled={disabled}
                    aria-invalid={hasInvalidAgreedCost(row.act)}
                    aria-label={`Prix convenu pour ${row.name} à ce rendez-vous`}
                    placeholder={row.tariff == null ? "Prix libre" : undefined}
                  />
                </div>
                {row.act.agreedCost !== undefined && row.tariff != null && (
                  <button
                    type="button"
                    onClick={() => setAgreedCost(index, undefined)}
                    disabled={disabled}
                    className="shrink-0 text-2xs text-muted-foreground underline decoration-dotted hover:text-foreground"
                  >
                    remettre au tarif ({formatAmount(row.tariff)} DT)
                  </button>
                )}
                {hasInvalidAgreedCost(row.act) && (
                  <span className="basis-full text-2xs text-destructive">
                    Montant invalide — par exemple 120,000.
                  </span>
                )}
              </div>
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
              <CommandEmpty>Aucun acte ne correspond.</CommandEmpty>
              {/*
                One CommandGroup per clinical discipline, in the order a course of treatment runs.
                A flat list of a clinic's whole catalogue is a wall of French with no landmarks; the headings turn
                it into something you scan rather than read. `cmdk` hides a group whose every item is filtered
                out, so typing collapses this back to a flat ranked list on its own — the same behaviour the
                fiche's picker gets by branching on `searching`, here for free.
              */}
              {procedureGroups.map(({ label, items }) => (
                <CommandGroup key={label} heading={label}>
                  {items.map((pt) => {
                    const already = selectedIds.has(pt.id)
                    return (
                      <CommandItem
                        key={pt.id}
                        // The discipline joins the searchable value, so « endo » finds « Traitement de canal ».
                        // cmdk matches on `value` alone, so leaving it out would make the group headings
                        // searchable to the eye but not to the keyboard.
                        value={pt.category ? `${pt.name} ${pt.category}` : pt.name}
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
                </CommandGroup>
              ))}
              {/* Its own group, deliberately: creating an act is not a member of any discipline, and putting it
                  inside the last one would file it under whatever that happens to be. */}
              <CommandGroup>
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
              // A ~16px inline target that is the only recovery from a failed catalogue load in both booking
              // dialogs. `touch-target` plus real padding; the negative margin keeps the line height unchanged.
              className="touch-target -my-1 rounded px-1.5 py-1 underline underline-offset-2 hover:no-underline"
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
              {/* « Tarif au catalogue », not « Montant »: this one is permanent and seeds every future visit,
                  while each act row above carries a price for this rendez-vous only. */}
              <Label htmlFor={`${idPrefix}-custom-cost`} className="text-xs text-muted-foreground">
                Tarif au catalogue
              </Label>
              <div className="relative">
                <span className="absolute left-2 top-1/2 -translate-y-1/2 text-xs text-muted-foreground">DT</span>
                {/* `text` + `inputMode="decimal"`, never `type="number"` (J8). This « Montant » creates a
                    ProcedureType's `defaultCost` — the same field as the catalogue form's, reached from the
                    booking dialog — so its `step="0.01"` made the millime unreachable on the value that seeds
                    every invoice line, and it refused the comma the app prints with. */}
                <Input
                  id={`${idPrefix}-custom-cost`}
                  type="text"
                  inputMode="decimal"
                  value={customCost}
                  onChange={(e) => setCustomCost(e.target.value)}
                  onKeyDown={(e) => {
                    if (e.key === "Enter") {
                      e.preventDefault()
                      void handleCreateCustom()
                    }
                  }}
                  placeholder="0,000"
                  className="h-9 pl-8"
                  disabled={creating}
                />
              </div>
            </div>
          </div>
          <div className="flex items-center justify-between gap-3">
            <p className="text-2xs text-muted-foreground">
              Durée et montant facultatifs. Sans durée, {fallbackDurationMinutes} min est utilisé.
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
