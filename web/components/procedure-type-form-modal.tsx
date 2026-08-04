"use client"

import type React from "react"
import { useState, useEffect } from "react"
import {
  Dialog,
  DialogContent,
  DialogHeader,
  DialogTitle,
  DialogFooter,
  DialogDescription,
} from "@/components/ui/dialog"
import { Button } from "@/components/ui/button"
import { Input } from "@/components/ui/input"
import { Label } from "@/components/ui/label"
import { Textarea } from "@/components/ui/textarea"
import { Badge } from "@/components/ui/badge"
import { FormErrorBanner } from "@/components/ui/form-error-banner"
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select"
import { Command, CommandGroup, CommandInput, CommandItem, CommandList } from "@/components/ui/command"
import { formatAmount, parseAmountInput } from "@/lib/format"
import { Popover, PopoverContent, PopoverTrigger } from "@/components/ui/popover"
import { Check, ChevronsUpDown, Loader2, Plus } from "lucide-react"
import { cn } from "@/lib/utils"
import { procedureTypesApi } from "@/lib/api/procedure-types"
import type { ProcedureTypeDto } from "@/lib/api/types"
import { ApiError } from "@/lib/api/client"
import { CONDITION_ORDER, conditionStyle } from "@/components/odontogram-conditions"

// Sentinel for the "no resulting condition" option (Radix Select forbids an empty-string value).
const NO_CONDITION = "__none__"

/**
 * French labels for the palette hexes. **Not** the palette itself: which colours exist is
 * `GET /api/procedure-types/colors` (AC-P2.36), because the backend `ColorHex` value object rejects anything
 * off its own list. This map only names them — the endpoint returns bare hex strings with no names (A-14), so
 * the labels have to live somewhere, and a hex with no entry here renders with the hex as its label (never
 * blank, never dropped).
 *
 * This replaces a hardcoded array carried under a "must match backend" comment, which is the drift the endpoint
 * exists to prevent: a colour added server-side never appeared, and one retired server-side was still offered
 * and then refused on save.
 */
const COLOR_LABELS_FR: Record<string, string> = {
  "#4F83CC": "Bleu doux",
  "#2A9D8F": "Sarcelle",
  "#6BAA75": "Vert doux",
  "#9B8EDC": "Lavande",
  "#E9A23B": "Ambre chaud",
  "#E76F51": "Corail",
  "#6C757D": "Ardoise",
  "#60A5FA": "Bleu ciel",
  "#5EEAD4": "Menthe",
  "#FB7185": "Rose",
}

const colorLabel = (hex: string) => COLOR_LABELS_FR[hex.toUpperCase()] ?? hex

interface ProcedureTypeFormModalProps {
  open: boolean
  onOpenChange: (open: boolean) => void
  editingProcedure?: ProcedureTypeDto | null
  onSuccess?: () => void
}

export function ProcedureTypeFormModal({ open, onOpenChange, editingProcedure, onSuccess }: ProcedureTypeFormModalProps) {
  const [name, setName] = useState("")
  const [duration, setDuration] = useState("")
  const [defaultCost, setDefaultCost] = useState("")
  const [description, setDescription] = useState("")
  /** The act's discipline. `""` = unfiled — the same value the server treats as "clear it". */
  const [category, setCategory] = useState("")
  const [categoryOpen, setCategoryOpen] = useState(false)
  /** The combobox's own search box, separate from {@link category}: typing must not change the saved value. */
  const [categoryQuery, setCategoryQuery] = useState("")
  /**
   * Suggestions from the server: the canonical disciplines plus the ones this clinic already uses.
   *
   * A failed fetch is not surfaced and not retried, deliberately — unlike the palette below, which *gates* the
   * form because `ColorHex` rejects anything off its list. The category is free text, so an empty suggestion list
   * degrades to a plain « Utiliser « … » » field: less convenient, still fully usable. Blocking the dialog over
   * an optional convenience would be the worse failure.
   */
  const [categorySuggestions, setCategorySuggestions] = useState<string[]>([])
  const [selectedColor, setSelectedColor] = useState("")
  const [resultingCondition, setResultingCondition] = useState<string | null>(null)
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)
  /**
   * The valid palette, from the server. Starts empty and fills on open; the swatch grid renders nothing until
   * it arrives rather than falling back to a local copy — a stale local list is exactly the drift this closes.
   */
  const [palette, setPalette] = useState<string[]>([])
  /**
   * The palette REQUEST's own state, tracked separately from `palette.length`.
   *
   * The defect it fixes: the grid rendered « Chargement de la palette… » whenever the array was empty, which is
   * one branch doing duty for three unrelated facts — still loading, loaded but empty, and *failed*. When the
   * request failed, the required « Couleur de l'agenda * » field said « Chargement… » forever, the form could
   * never be completed, and nothing on screen said why or offered a way forward.
   */
  const [paletteState, setPaletteState] = useState<"loading" | "ready" | "error">("loading")
  // Bumped by « Réessayer » to re-run the fetch without closing and reopening the dialog.
  const [paletteRetry, setPaletteRetry] = useState(0)

  useEffect(() => {
    if (!open) return
    let active = true
    setPaletteState("loading")
    procedureTypesApi
      .getColors()
      .then((colors) => {
        if (!active) return
        setPalette(colors)
        setPaletteState("ready")
      })
      // A failed palette fetch must not block editing a procedure's name or fee — the rest of the form stays
      // usable and the colour already on the record is kept — but it must SAY so rather than pretending to load.
      .catch(() => {
        if (!active) return
        setPalette([])
        setPaletteState("error")
      })
    return () => {
      active = false
    }
  }, [open, paletteRetry])

  // The category suggestions. Refetched on every open rather than once, because an act created in this session
  // may have introduced a category the list would otherwise not offer until a reload.
  useEffect(() => {
    if (!open) return
    let active = true
    procedureTypesApi
      .getCategories()
      .then((categories) => {
        if (active) setCategorySuggestions(categories)
      })
      .catch(() => {
        if (active) setCategorySuggestions([])
      })
    return () => {
      active = false
    }
  }, [open])

  // Populate form when editing
  useEffect(() => {
    if (editingProcedure) {
      setName(editingProcedure.name)
      setDuration(String(editingProcedure.defaultDurationMinutes))
      // `formatAmount`, never `String(...)` (J8): reopening an act showed « 70.5 » where the placeholder itself
      // promises « Ex. 70,000 » — and the field used to refuse the very comma it was advertising.
      setDefaultCost(editingProcedure.defaultCost ? formatAmount(editingProcedure.defaultCost) : "")
      setDescription(editingProcedure.description || "")
      setCategory(editingProcedure.category || "")
      setSelectedColor(editingProcedure.colorHex)
      setResultingCondition(editingProcedure.resultingCondition ?? null)
    } else {
      // Reset form for new procedure
      setName("")
      setDuration("")
      setDefaultCost("")
      setDescription("")
      setCategory("")
      // Left blank here; the palette effect below preselects the first server-supplied colour once it lands.
      setSelectedColor("")
      setResultingCondition(null)
    }
    // Both branches: a query left in the combobox from the last act edited would otherwise still be offering
    // « Utiliser « Endo » » over a different act's form.
    setCategoryQuery("")
    setError(null)
  }, [editingProcedure, open])

  // Preselect the first valid colour for a NEW procedure once the palette arrives. Kept separate from the
  // reset above because the palette is fetched asynchronously — the reset runs before it is known.
  useEffect(() => {
    if (!editingProcedure && !selectedColor && palette.length > 0) {
      setSelectedColor(palette[0])
    }
  }, [editingProcedure, selectedColor, palette])

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault()
    setError(null)
    setLoading(true)

    try {
      // Validate required fields
      if (!name.trim()) {
        setError("Le nom de l'acte est requis")
        setLoading(false)
        return
      }

      if (!duration || Number(duration) <= 0) {
        setError("La durée doit être supérieure à 0")
        setLoading(false)
        return
      }

      if (Number(duration) >= 480) {
        setError("La durée doit être inférieure à 480 minutes (8 heures)")
        setLoading(false)
        return
      }

      const durationMinutes = Number(duration)

      const defaultCostValue = defaultCost.trim() ? parseAmountInput(defaultCost) : null
      // NaN is checked explicitly, not folded into the negative test: the field is now `type="text"` (J8), so the
      // browser no longer refuses a malformed value on the user's behalf and « 7,,5 » would otherwise be sent as
      // NaN and land as a null cost — silently unpricing the act that seeds every invoice line.
      if (defaultCostValue !== null && !Number.isFinite(defaultCostValue)) {
        setError("Saisissez un coût valide, par exemple 70,000")
        setLoading(false)
        return
      }
      if (defaultCostValue !== null && defaultCostValue < 0) {
        setError("Le coût par défaut ne peut pas être négatif")
        setLoading(false)
        return
      }

      if (editingProcedure) {
        // Update existing procedure
        await procedureTypesApi.update(editingProcedure.id, {
          name: name.trim(),
          defaultDurationMinutes: durationMinutes,
          defaultCost: defaultCostValue,
          colorHex: selectedColor,
          description: description.trim() || undefined,
          // Sent even when empty, unlike `description` above: the update command reads null as "unchanged" and
          // `""` as "clear it", so `|| undefined` here would make unfiling an act silently do nothing.
          category: category.trim(),
          resultingCondition,
        })
      } else {
        // Create new procedure
        await procedureTypesApi.create({
          name: name.trim(),
          defaultDurationMinutes: durationMinutes,
          defaultCost: defaultCostValue,
          colorHex: selectedColor,
          description: description.trim() || undefined,
          category: category.trim() || undefined,
          resultingCondition,
        })
      }

      onSuccess?.()
      onOpenChange(false)
    } catch (err) {
      if (err instanceof ApiError) {
        setError(err.message)
      } else {
        setError("Échec de l'enregistrement du type d'acte. Veuillez réessayer.")
      }
      console.error("Error saving procedure type:", err)
    } finally {
      setLoading(false)
    }
  }

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      {/* No `max-h-[90dvh] overflow-y-auto`: `ui/dialog.tsx`'s base already declares both. Repeating them
          unprefixed meant this call site would silently override the primitive the day it changes. */}
      <DialogContent className="md:max-w-2xl">
        <DialogHeader>
          <DialogTitle>{editingProcedure ? "Modifier le type d'acte" : "Ajouter un type d'acte"}</DialogTitle>
          <DialogDescription>
            {editingProcedure
              ? "Mettez à jour les détails et la couleur du type d'acte"
              : "Définissez un nouvel acte avec sa durée et sa couleur d'agenda"}
          </DialogDescription>
        </DialogHeader>

        <form onSubmit={handleSubmit} className="space-y-4">
          {/* The shared refusal banner, on `--destructive-wash` / `--destructive`, replacing a hand-written
              `border-red-200 bg-red-50 … dark:` copy that maintained dark mode itself. */}
          <FormErrorBanner message={error} />

          <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
            <div className="space-y-1.5">
              <Label htmlFor="name" className="text-sm">
                Nom de l'acte <span className="text-destructive">*</span>
              </Label>
              <Input
                id="name"
                placeholder="ex. : consultation"
                value={name}
                onChange={(e) => setName(e.target.value)}
                required
                disabled={loading}
              />
            </div>

            <div className="space-y-1.5">
              <Label htmlFor="duration" className="text-sm">
                Durée (min) <span className="text-destructive">*</span>
              </Label>
              <Input
                id="duration"
                type="number"
                min="1"
                max="479"
                step="1"
                placeholder="30"
                value={duration}
                onChange={(e) => setDuration(e.target.value)}
                required
                disabled={loading}
              />
            </div>
          </div>

          <div className="space-y-1.5">
            <Label htmlFor="defaultCost" className="text-sm">
              Coût par défaut (optionnel)
            </Label>
            <div className="relative">
              <span className="absolute left-3 top-1/2 -translate-y-1/2 text-muted-foreground text-sm">DT</span>
              {/* `text` + `inputMode="decimal"`, never `type="number"` (J8). The worst offender of the ten: this
                  field seeds every invoice line, its `step="0.01"` made the **millime unreachable** on it, and its
                  own placeholder advertised « Ex. 70,000 » — a comma the input then refused. */}
              <Input
                id="defaultCost"
                type="text"
                inputMode="decimal"
                placeholder="Ex. 70,000"
                value={defaultCost}
                onChange={(e) => setDefaultCost(e.target.value)}
                className="pl-10"
                disabled={loading}
              />
            </div>
            <p className="text-xs text-muted-foreground">
              Coût habituel de cet acte. Utilisé pour préremplir le coût dans les dossiers dentaires.
            </p>
          </div>

          {/*
            Catégorie — a combobox, not a Select: the twelve disciplines are *suggestions*, and a practice may
            file work under one of its own. The list it offers already includes the clinic's own categories
            (`getCategories` unions them in), which is what makes an open field converge instead of drifting —
            the second admin to want « Occlusodontie » picks it rather than retyping it. Typing a variant of a
            known one is still safe: the server folds « endodontie » back onto « Endodontie » on save.
          */}
          <div className="space-y-1.5">
            <Label htmlFor="category" className="text-sm">
              Catégorie (facultatif)
            </Label>
            <Popover open={categoryOpen} onOpenChange={setCategoryOpen}>
              <PopoverTrigger asChild>
                <Button
                  id="category"
                  type="button"
                  variant="outline"
                  role="combobox"
                  aria-expanded={categoryOpen}
                  disabled={loading}
                  className="w-full justify-between font-normal"
                >
                  <span className={cn("truncate", !category && "text-muted-foreground")}>
                    {category || "Sans catégorie"}
                  </span>
                  <ChevronsUpDown className="ml-2 h-4 w-4 shrink-0 opacity-50" />
                </Button>
              </PopoverTrigger>
              <PopoverContent
                className="p-0"
                align="start"
                style={{ width: "var(--radix-popover-trigger-width)" }}
              >
                <Command>
                  <CommandInput
                    placeholder="Rechercher ou saisir…"
                    value={categoryQuery}
                    onValueChange={setCategoryQuery}
                  />
                  <CommandList>
                    {/* No CommandEmpty: with a query typed there is always the « Utiliser » row below, and with
                        none there is always « Sans catégorie » — so the list is never actually empty, and an
                        « Aucun résultat » would appear directly above a row that is a result. */}
                    <CommandGroup>
                      <CommandItem
                        value="__sans categorie__"
                        onSelect={() => {
                          setCategory("")
                          setCategoryQuery("")
                          setCategoryOpen(false)
                        }}
                      >
                        <Check className={cn("mr-2 h-4 w-4", category ? "opacity-0" : "opacity-100")} />
                        <span className="text-muted-foreground">Sans catégorie</span>
                      </CommandItem>
                      {categorySuggestions.map((suggestion) => (
                        <CommandItem
                          key={suggestion}
                          value={suggestion}
                          onSelect={() => {
                            setCategory(suggestion)
                            setCategoryQuery("")
                            setCategoryOpen(false)
                          }}
                        >
                          <Check
                            className={cn(
                              "mr-2 h-4 w-4",
                              category === suggestion ? "opacity-100" : "opacity-0",
                            )}
                          />
                          <span className="truncate">{suggestion}</span>
                        </CommandItem>
                      ))}
                    </CommandGroup>
                    {/* The escape hatch, and the reason this is a combobox at all. Hidden once the typed text
                        already names a suggestion, so « Endodontie » is never offered twice — once as itself and
                        once as "create it". */}
                    {categoryQuery.trim() &&
                      !categorySuggestions.some(
                        (s) => s.toLowerCase() === categoryQuery.trim().toLowerCase(),
                      ) && (
                        <CommandGroup>
                          <CommandItem
                            value={`__utiliser__${categoryQuery}`}
                            onSelect={() => {
                              setCategory(categoryQuery.trim())
                              setCategoryQuery("")
                              setCategoryOpen(false)
                            }}
                          >
                            <Plus className="mr-2 h-4 w-4" />
                            <span className="truncate">
                              Utiliser «&nbsp;<span className="font-medium">{categoryQuery.trim()}</span>&nbsp;»
                            </span>
                          </CommandItem>
                        </CommandGroup>
                      )}
                  </CommandList>
                </Command>
              </PopoverContent>
            </Popover>
            <p className="text-xs text-muted-foreground">
              Regroupe l&apos;acte dans le catalogue et dans les listes de sélection.
            </p>
          </div>

          <div className="space-y-1.5">
            <Label htmlFor="description" className="text-sm">
              Description (optionnel)
            </Label>
            <Textarea
              id="description"
              placeholder="Brève description…"
              value={description}
              onChange={(e) => setDescription(e.target.value)}
              rows={2}
              className="resize-none"
              disabled={loading}
            />
          </div>

          <div className="space-y-1.5">
            <Label htmlFor="resultingCondition" className="text-sm">
              État résultant sur l'odontogramme (facultatif)
            </Label>
            <Select
              value={resultingCondition ?? NO_CONDITION}
              onValueChange={(v) => setResultingCondition(v === NO_CONDITION ? null : v)}
              disabled={loading}
            >
              {/* `w-full`: `ui/select.tsx`'s trigger ships `w-fit`, so it rendered narrower than every other
                  field in this form. */}
              <SelectTrigger id="resultingCondition" className="w-full">
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                <SelectItem value={NO_CONDITION}>Aucun</SelectItem>
                {CONDITION_ORDER.map((c) => (
                  <SelectItem key={c} value={c}>
                    {conditionStyle(c).label}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
            <p className="text-xs text-muted-foreground">
              État appliqué automatiquement aux dents traitées par cet acte dans l'odontogramme.
            </p>
          </div>

          <div className="space-y-2">
            <Label className="text-sm">
              Couleur de l'agenda <span className="text-destructive">*</span>
            </Label>

            {paletteState === "loading" ? (
              <p role="status" className="flex items-center gap-2 text-xs text-muted-foreground">
                <Loader2 className="h-3.5 w-3.5 animate-spin" aria-hidden="true" />
                Chargement de la palette…
              </p>
            ) : paletteState === "error" ? (
              // Named, not silent: the field is required, so a user staring at « Chargement… » that never ends
              // has no way to know the form is unsubmittable or what to do about it.
              <div
                role="status"
                className="space-y-2 rounded-lg border border-destructive/25 bg-destructive-wash p-3 text-sm text-destructive"
              >
                <p>Les couleurs de l&apos;agenda n&apos;ont pas pu être chargées.</p>
                <Button
                  type="button"
                  size="sm"
                  variant="outline"
                  onClick={() => setPaletteRetry((t) => t + 1)}
                  disabled={loading}
                >
                  Réessayer
                </Button>
              </div>
            ) : palette.length === 0 ? (
              <p className="text-xs text-muted-foreground">Aucune couleur disponible.</p>
            ) : (
              // `grid-cols-3` at the base: five columns of a 390px dialog is ~62px per cell for a 24px swatch
              // plus a French label (« Ambre chaud »), which wraps to three lines and collides with its
              // neighbours. Five only from `sm:` up.
              <div className="grid grid-cols-3 gap-2 sm:grid-cols-5">
                {palette.map((hex) => (
                  <button
                    key={hex}
                    type="button"
                    onClick={() => setSelectedColor(hex)}
                    disabled={loading}
                    className={cn(
                      // Movement hover gated behind `hover-hover:` (AC-11).
                      "relative flex flex-col items-center gap-1 rounded-lg border-2 p-2 transition-all hover-hover:hover:scale-105",
                      selectedColor === hex
                        ? "border-primary bg-accent"
                        : "border-border bg-background hover:border-muted-foreground/50",
                      loading && "opacity-50 cursor-not-allowed"
                    )}
                    title={colorLabel(hex)}
                  >
                    <div
                      className="h-6 w-6 rounded-full border-2 border-background shadow-sm"
                      style={{ backgroundColor: hex }}
                    />
                    {selectedColor === hex && (
                      <div className="absolute -right-1 -top-1 flex h-4 w-4 items-center justify-center rounded-full bg-primary text-primary-foreground">
                        <Check className="h-2.5 w-2.5" />
                      </div>
                    )}
                    {/* AC-P2.37: a colour the server offers but this map does not name still renders — with the
                        hex as its label — rather than appearing as an unlabelled swatch. */}
                    <span className="text-2xs text-center leading-tight text-muted-foreground">
                      {colorLabel(hex)}
                    </span>
                  </button>
                ))}
              </div>
            )}
          </div>

          <div className="space-y-2 rounded-lg border border-border bg-muted/30 p-3">
            <Label className="text-xs font-medium">Aperçu</Label>
            <div className="grid grid-cols-1 gap-3 sm:grid-cols-2">
              <div className="space-y-1.5">
                <p className="text-2xs font-medium text-muted-foreground">Agenda</p>
                <div
                  className="rounded-md border-l-4 bg-card p-2 shadow-sm"
                  style={{
                    borderLeftColor: selectedColor,
                    backgroundColor: `${selectedColor}15`,
                  }}
                >
                  <p className="text-xs font-medium text-foreground">{name || "Nom de l'acte"}</p>
                  <p className="text-2xs text-muted-foreground">{duration ? `${duration} min` : "Durée"}</p>
                </div>
              </div>

              <div className="space-y-1.5">
                <p className="text-2xs font-medium text-muted-foreground">Badge</p>
                <div className="flex items-center gap-2">
                  <div
                    className="h-2.5 w-2.5 rounded-full border border-border"
                    style={{ backgroundColor: selectedColor }}
                  />
                  <Badge
                    variant="outline"
                    className="border-2 text-xs"
                    style={{
                      borderColor: selectedColor,
                      color: selectedColor,
                      backgroundColor: `${selectedColor}10`,
                    }}
                  >
                    {name || "Acte"}
                  </Badge>
                </div>
              </div>
            </div>
          </div>

          <DialogFooter className="gap-2">
            <Button type="button" variant="outline" onClick={() => onOpenChange(false)} disabled={loading}>
              Annuler
            </Button>
            <Button type="submit" disabled={loading}>
              {loading ? (editingProcedure ? "Mise à jour…" : "Création…") : (editingProcedure ? "Mettre à jour" : "Ajouter l'acte")}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  )
}


