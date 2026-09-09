"use client"

import { useMemo, useState } from "react"
import { ChevronDown, ChevronRight, FlaskConical, Pill, Search, X } from "lucide-react"
import { Button } from "@/components/ui/button"
import { Input } from "@/components/ui/input"
import { Label } from "@/components/ui/label"
import { Textarea } from "@/components/ui/textarea"
import { LoadFailureNotice } from "@/components/ui/load-failure"
import {
  formatPrescriptionLine,
  isExamenLine,
  shortPrescriptionLabel,
  type PrescriptionLine,
} from "@/lib/documents"
import type { MedicationDto } from "@/lib/api/types"
import { EXAM_SUGGESTIONS } from "./exam-suggestions"

/**
 * One prescribed line inside the fiche's « Prescription » section.
 *
 * <p><b>At rest it is ONE row, and that is the whole density argument.</b> The row carries the exact sentence
 * that will be printed on the ordonnance — « Augmentin Comprimé 1 g, 3x par jour pendant 7 jours » — so three
 * prescriptions cost ~108 px in a dialog whose two heaviest blocks already run to 213 and 258 lines of markup.
 * It also removes the need for a separate « aperçu » field: the collapsed row <i>is</i> the proof-read.</p>
 *
 * <p>Only the line being typed opens. That is <b>the act card's gesture, one section down</b>
 * (`record/act-card.tsx`): a pile of records of which exactly one is armed. Re-deriving a different idiom for
 * a list two blocks below it is how one modal comes to have two ways of editing a row.</p>
 *
 * <p>⚠️ <b>The catalogue is an inline column, never a Popover</b> — `record/act-catalog-picker.tsx` records
 * why in as many words: nesting a searchable list inside a Popover inside this Dialog gives three components a
 * claim on Enter. The ordonnance editor uses a Popover for the same catalogue and is right to: it is a page,
 * not a dialog.</p>
 *
 * <p>⚠️ <b>An examen has ONE field.</b> No posologie, no dosage, no catalogue — a bilan or a radio is a
 * sentence, and its value is printed verbatim as its own line of the ordonnance. That is what keeps every one
 * of the three line formatters (C# for the PDF, TS for the preview, TS for the Word export) untouched by this
 * feature.</p>
 */
interface PrescriptionLineRowProps {
  line: PrescriptionLine
  /** True when this is the line being typed. Exactly one line in the section is armed. */
  armed: boolean
  onArm: () => void
  onChange: (line: PrescriptionLine) => void
  onRemove: () => void
  /** The active medication catalogue. Empty is a legitimate state — free text always works. */
  catalog: MedicationDto[]
  /** The catalogue READ failed, which is not the same fact as an empty catalogue. */
  catalogFailed: boolean
  onRetryCatalog: () => void
  disabled?: boolean
}

/** Printed/displayed label for a catalogue entry: « Marque Dosage Forme », empty parts dropped. */
const catalogLabel = (m: MedicationDto) => [m.brandName, m.strength, m.form].filter(Boolean).join(" ")

/** Accent-insensitive, like the act catalogue's own filter: « amoxicilline » must reach « Amoxicillinè ». */
const norm = (value: string) =>
  value.normalize("NFD").replace(/[\u0300-\u036f]/g, "").toLowerCase()

export function PrescriptionLineRow({
  line,
  armed,
  onArm,
  onChange,
  onRemove,
  catalog,
  catalogFailed,
  onRetryCatalog,
  disabled,
}: PrescriptionLineRowProps) {
  const examen = isExamenLine(line.kind)
  const [detailsOpen, setDetailsOpen] = useState(false)
  const [catalogOpen, setCatalogOpen] = useState(false)
  const [query, setQuery] = useState("")

  const matches = useMemo(() => {
    const q = norm(query.trim())
    if (!q) return catalog.slice(0, 40)
    // The DCIs are searchable too: prescribers look a drug up by its molecule as often as by its brand.
    return catalog
      .filter((m) => norm(`${m.brandName} ${m.strength} ${m.form} ${m.dcis.join(" ")}`).includes(q))
      .slice(0, 40)
  }, [catalog, query])

  const removeLabel = line.name?.trim()
    ? `Retirer ${line.name.trim()} de l'ordonnance`
    : examen
      ? "Retirer cet examen de l'ordonnance"
      : "Retirer ce médicament de l'ordonnance"

  const accent = examen ? "var(--chart-2)" : "var(--chart-1)"

  // ── At rest ───────────────────────────────────────────────────────────────────────────────────────────────
  if (!armed) {
    const summary = line.name?.trim()
      ? formatPrescriptionLine(line)
      : examen
        ? "Examen à préciser"
        : "Médicament à préciser"

    return (
      <div
        className="flex min-w-0 items-center gap-2 rounded-md border border-s-[3px] bg-card ps-2 pe-1"
        style={{ borderInlineStartColor: accent }}
      >
        <button
          type="button"
          onClick={onArm}
          disabled={disabled}
          // The floor is GROWN, not overlaid: these rows are 8 px apart and the later sibling paints last, so
          // a `.touch-target` here would send a thumb aimed at one prescription into the next.
          className="flex min-h-9 flex-1 items-center gap-2 rounded-md text-start coarse:min-h-11"
          title={summary}
        >
          {examen ? (
            <FlaskConical className="h-3.5 w-3.5 shrink-0" style={{ color: accent }} aria-hidden="true" />
          ) : (
            <Pill className="h-3.5 w-3.5 shrink-0" style={{ color: accent }} aria-hidden="true" />
          )}
          <span className="min-w-0 flex-1 truncate text-xs">{summary}</span>
        </button>
        <Button
          type="button"
          variant="ghost"
          size="sm"
          onClick={onRemove}
          disabled={disabled}
          className="h-8 w-8 shrink-0 p-0 coarse:size-11"
          aria-label={removeLabel}
        >
          <X className="h-3.5 w-3.5" />
        </Button>
      </div>
    )
  }

  // ── Being typed ───────────────────────────────────────────────────────────────────────────────────────────
  return (
    <div
      className="min-w-0 rounded-md border border-s-[3px] bg-card p-2.5"
      style={{ borderInlineStartColor: accent, boxShadow: `0 0 0 3px color-mix(in oklab, ${accent} 9%, transparent)` }}
    >
      <div className="grid min-w-0 gap-2.5">
        {examen ? (
          <div className="flex flex-col gap-1.5">
            <Label htmlFor={`exam-${lineKey(line)}`} className="text-xs text-muted-foreground">
              Examen demandé
            </Label>
            <div className="flex items-start gap-2">
              <Textarea
                id={`exam-${lineKey(line)}`}
                rows={2}
                placeholder="Ex : Radiographie panoramique dentaire"
                value={line.name}
                onChange={(e) => onChange({ ...line, name: e.target.value })}
                disabled={disabled}
                className="min-h-0 flex-1"
              />
              <Button
                type="button"
                variant="ghost"
                size="sm"
                onClick={onRemove}
                disabled={disabled}
                className="h-9 w-8 shrink-0 p-0 coarse:size-11"
                aria-label={removeLabel}
              >
                <X className="h-4 w-4" />
              </Button>
            </div>
            {/* Suggestions, not a closed list — see exam-suggestions.ts. */}
            <div className="flex flex-wrap gap-1.5">
              {EXAM_SUGGESTIONS.flatMap((group) => group.items)
                .slice(0, 5)
                .map((item) => (
                  <Button
                    key={item}
                    type="button"
                    variant="outline"
                    size="sm"
                    disabled={disabled}
                    onClick={() => onChange({ ...line, name: item })}
                    // Its own box rather than a touch-target overlay: these chips sit in a wrapping row.
                    className="h-auto min-h-8 whitespace-normal px-2 py-1 text-2xs coarse:min-h-11"
                  >
                    {item}
                  </Button>
                ))}
            </div>
            <p className="text-2xs text-muted-foreground">
              Écrivez ce que vous voulez : la ligne est imprimée telle quelle sur l&apos;ordonnance.
            </p>
          </div>
        ) : (
          <>
            <div className="flex flex-col gap-1.5">
              <div className="flex items-center gap-2">
                <Input
                  aria-label="Nom du médicament"
                  placeholder="Ex : Amoxicilline"
                  value={line.name}
                  onChange={(e) =>
                    // Typing by hand is a free-text entry: drop the catalogue link and the molecule snapshot,
                    // or the line would keep claiming a DCI for a drug it no longer names.
                    onChange({ ...line, name: e.target.value, medicationId: undefined, dci: [] })
                  }
                  disabled={disabled}
                  className="min-w-0 flex-1"
                />
                <Button
                  type="button"
                  variant="outline"
                  size="sm"
                  disabled={disabled}
                  onClick={() => setCatalogOpen((open) => !open)}
                  aria-expanded={catalogOpen}
                  className="h-9 w-9 shrink-0 p-0 coarse:size-11"
                  aria-label="Chercher dans le catalogue des médicaments"
                >
                  <Search className="h-4 w-4" />
                </Button>
                <Button
                  type="button"
                  variant="ghost"
                  size="sm"
                  onClick={onRemove}
                  disabled={disabled}
                  className="h-9 w-8 shrink-0 p-0 coarse:size-11"
                  aria-label={removeLabel}
                >
                  <X className="h-4 w-4" />
                </Button>
              </div>
              {(line.dci?.length ?? 0) > 0 && (
                <span className="text-2xs text-muted-foreground">DCI : {line.dci!.join(", ")}</span>
              )}
            </div>

            {catalogOpen && (
              <div className="rounded-md border bg-card">
                <div className="border-b p-2">
                  <Input
                    autoFocus
                    aria-label="Rechercher un médicament"
                    placeholder="Rechercher un médicament…"
                    value={query}
                    onChange={(e) => setQuery(e.target.value)}
                    onKeyDown={(e) => {
                      if (e.key === "Escape") {
                        e.preventDefault()
                        setCatalogOpen(false)
                      }
                    }}
                  />
                </div>
                {catalogFailed ? (
                  <div className="p-2">
                    <LoadFailureNotice
                      message="Le catalogue des médicaments n'a pas pu être chargé. Ce n'est pas un catalogue vide — la lecture a échoué. Réessayez avant de saisir à la main."
                      onRetry={onRetryCatalog}
                    />
                  </div>
                ) : matches.length === 0 ? (
                  <p className="p-3 text-xs text-muted-foreground">
                    Aucun médicament ne correspond. Écrivez le nom : il sera prescrit tel quel.
                  </p>
                ) : (
                  <ul className="max-h-56 overflow-y-auto py-1">
                    {matches.map((m) => (
                      <li key={m.id}>
                        <button
                          type="button"
                          onClick={() => {
                            // Name = brand + form; the strength goes to « Dosage » rather than being crammed
                            // into the name. Same split as the ordonnance editor, so one document opened in
                            // both places reads identically.
                            onChange({
                              ...line,
                              name: [m.brandName, m.form].filter(Boolean).join(" "),
                              dosage: m.strength,
                              medicationId: m.id,
                              dci: m.dcis,
                            })
                            setCatalogOpen(false)
                            setQuery("")
                          }}
                          className="flex min-h-9 w-full flex-col items-start gap-0.5 px-3 py-1.5 text-start hover:bg-accent coarse:min-h-11"
                        >
                          <span className="text-xs font-medium">{catalogLabel(m)}</span>
                          <span className="text-2xs text-muted-foreground">
                            {m.dcis.join(", ")}
                            {m.isProvisional ? " · à vérifier" : ""}
                          </span>
                        </button>
                      </li>
                    ))}
                  </ul>
                )}
              </div>
            )}

            <div className="flex flex-col gap-1.5">
              <Label htmlFor={`dosage-${lineKey(line)}`} className="text-xs text-muted-foreground">
                Dosage
              </Label>
              <Input
                id={`dosage-${lineKey(line)}`}
                placeholder="Ex : 500 mg"
                value={line.dosage}
                onChange={(e) => onChange({ ...line, dosage: e.target.value })}
                disabled={disabled}
              />
            </div>

            {/*
              Two columns at every width, deliberately. § 10's rule targets a 2-up grid of FIELDS; these two
              hold one or two digits and their labels are short by design (« Fois/jour » is ~54 px at 12 px),
              so at 320 px each cell is ~120 px and neither label wraps — which is the failure the ordonnance
              editor already met with « Voie d'administration ». Stacking them costs 62 px for nothing.
            */}
            <div className="grid grid-cols-2 gap-2">
              <div className="flex flex-col gap-1.5">
                <Label htmlFor={`times-${lineKey(line)}`} className="text-xs text-muted-foreground">
                  Fois/jour
                </Label>
                <Input
                  id={`times-${lineKey(line)}`}
                  type="number"
                  min="1"
                  placeholder="Ex : 3"
                  value={line.timesPerDay}
                  onChange={(e) => onChange({ ...line, timesPerDay: e.target.value })}
                  disabled={disabled}
                />
              </div>
              <div className="flex flex-col gap-1.5">
                <Label htmlFor={`days-${lineKey(line)}`} className="text-xs text-muted-foreground">
                  Jours
                </Label>
                <Input
                  id={`days-${lineKey(line)}`}
                  type="number"
                  min="1"
                  placeholder="Ex : 7"
                  value={line.duration}
                  onChange={(e) => onChange({ ...line, duration: e.target.value })}
                  disabled={disabled}
                />
              </div>
            </div>

            {/*
              Voie + quantité behind a fold — the act card's own « Détails » gesture, not a new one. Both are
              required of a dispensable prescription by R.5132-3 and both are optional in practice, so folding
              them is what buys the density; nothing is removed (§ 0).
            */}
            <div className="grid gap-2">
              <button
                type="button"
                onClick={() => setDetailsOpen((open) => !open)}
                aria-expanded={detailsOpen}
                className="flex min-h-8 items-center gap-1.5 text-start coarse:min-h-11"
              >
                {detailsOpen ? (
                  <ChevronDown className="h-3.5 w-3.5 shrink-0 text-muted-foreground" aria-hidden="true" />
                ) : (
                  <ChevronRight className="h-3.5 w-3.5 shrink-0 text-muted-foreground" aria-hidden="true" />
                )}
                <span className="text-2xs text-muted-foreground">
                  {detailsOpen ? "Détails" : "Détails — voie d'administration, quantité"}
                </span>
              </button>
              {detailsOpen && (
                <div className="grid gap-2 sm:grid-cols-2">
                  <div className="flex flex-col gap-1.5">
                    <Label htmlFor={`route-${lineKey(line)}`} className="text-xs text-muted-foreground">
                      Voie d&apos;administration
                    </Label>
                    <Input
                      id={`route-${lineKey(line)}`}
                      placeholder="Ex : par voie orale"
                      value={line.route ?? ""}
                      onChange={(e) => onChange({ ...line, route: e.target.value })}
                      disabled={disabled}
                    />
                  </div>
                  <div className="flex flex-col gap-1.5">
                    <Label htmlFor={`qty-${lineKey(line)}`} className="text-xs text-muted-foreground">
                      Quantité
                    </Label>
                    <Input
                      id={`qty-${lineKey(line)}`}
                      placeholder="Ex : 1 boîte"
                      value={line.quantity ?? ""}
                      onChange={(e) => onChange({ ...line, quantity: e.target.value })}
                      disabled={disabled}
                    />
                  </div>
                </div>
              )}
            </div>
          </>
        )}
      </div>
    </div>
  )
}

/**
 * A stable-enough id fragment for the `htmlFor` pairs. The lines have no server ids until the ordonnance is
 * saved, and the label only has to reach the input beside it — so the line's own name is enough, and a blank
 * one falls back to a constant rather than to an index that would change as lines are removed.
 */
const lineKey = (line: PrescriptionLine) =>
  (shortPrescriptionLabel(line) || "nouvelle").replace(/[^a-zA-Z0-9]+/g, "-").toLowerCase()

export { lineKey as prescriptionLineKey }
