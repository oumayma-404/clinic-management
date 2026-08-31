"use client"

import { useEffect, useMemo, useRef, useState } from "react"
import { Search } from "lucide-react"
import { Button } from "@/components/ui/button"
import { cn } from "@/lib/utils"
import { formatDT } from "@/lib/format"
import { conditionStyle } from "@/components/odontogram-conditions"
import { groupProceduresByCategory } from "@/components/procedure-categories"
import { COARSE_POINTER_QUERY } from "@/lib/hooks/use-media-query"
import type { ProcedureTypeDto } from "@/lib/api/types"

/**
 * `normalize("NFD")` splits « è » into « e » plus a combining accent; U+0300–U+036F is that combining block.
 * Built from an escaped string rather than a regex literal so the range stays readable in an editor.
 */
const COMBINING_MARKS = new RegExp("[\\u0300-\\u036f]", "g")

/** Accent- and case-insensitive contains — « prothese » must find « Prothèse ». */
function fold(value: string): string {
  return value.normalize("NFD").replace(COMBINING_MARKS, "").toLowerCase()
}

interface ActCatalogPickerProps {
  procedureTypes: ProcedureTypeDto[]
  /** Chosen from the catalogue — carries name, tarif, couleur and état résultant. */
  onPick: (procedure: ProcedureTypeDto) => void
  /** Committed as free text, for an act the catalogue does not carry. */
  onFreeText: (name: string) => void
  /** Present only when there is already an act to fall back to, so the list can be dismissed. */
  onCancel?: () => void
  disabled?: boolean
  autoFocus?: boolean
}

/**
 * The catalogue as ONE DENSE COLUMN — a tile grid was unpickable: multi-column wrapping breaks vertical
 * scanning and a ~194px tile truncates names like « Extraction chirurgicale (sagesse / dent incluse) ».
 *
 * Two behaviours matter as much as the layout:
 *  - Group headings appear only while BROWSING. The seeded catalogue is 19 acts across 12 categories, so
 *    headings nearly outnumber rows; as soon as there is a query the list goes flat and ranked.
 *  - It renders INLINE, not in a Popover. Nesting Radix `Command` in a `Popover` inside a `Dialog` gave three
 *    components a claim on Enter; owning the key here removes that class of bug entirely.
 */
export function ActCatalogPicker({
  procedureTypes,
  onPick,
  onFreeText,
  onCancel,
  disabled,
  autoFocus,
}: ActCatalogPickerProps) {
  const [query, setQuery] = useState("")
  const [cursor, setCursor] = useState(0)
  const listRef = useRef<HTMLDivElement>(null)
  const inputRef = useRef<HTMLInputElement>(null)

  /**
   * Focus the search field — **on a mouse only**.
   *
   * <p>On a finger this raised the on-screen keyboard the instant « Ajouter une fiche médicale » opened, over the
   * very list the field filters, and the browser scrolled the sheet's own body to bring the focused field up.
   * `stock-table.tsx` states the rule for the rest of the app (`ui/dialog.tsx` sends the opening focus to the
   * title so a sheet does not raise a keyboard over its content); this picker had opted itself back out. Typing
   * still focuses it — a tap on the field is a request for the keyboard, which is a different thing.</p>
   */
  useEffect(() => {
    if (!autoFocus) return
    if (window.matchMedia?.(COARSE_POINTER_QUERY).matches) return
    inputRef.current?.focus()
  }, [autoFocus])

  const trimmed = query.trim()
  const searching = trimmed !== ""

  // Browsing only: the same list, bucketed for headings — canonical disciplines in clinical order, the clinic's
  // own after them, unfiled acts last. Grouping moved to a shared helper so this picker and the agenda's cannot
  // disagree about where an act lives, and it now reads the real `category` field: it used to bucket on
  // `description`, which was where the catalog seed had been smuggling the category for want of a column.
  const groups = useMemo(
    () => (searching ? [] : groupProceduresByCategory(procedureTypes)),
    [procedureTypes, searching],
  )

  /**
   * Flat, ranked match list — also the keyboard's index space, so browsing and searching share one cursor.
   *
   * <p>⚠️ While BROWSING that index space must be the **grouped** order, not `procedureTypes`' own. The rows are
   * rendered group by group and look their own index up with `matches.indexOf(pt)`, so indexing the ungrouped
   * array handed the cursor to whatever act the API returned first — measured as the 9th row down. The mount
   * effect below then scrolled the list to it, so on a phone the picker opened mid-catalogue with
   * « Consultation » out of sight above, and ↓ moved the highlight to an unrelated row further down the page
   * rather than to the next one. `groupProceduresByCategory` partitions, so the `n/n` count is unaffected.</p>
   */
  const matches = useMemo(() => {
    if (!searching) return groups.flatMap((group) => group.items)
    const needle = fold(trimmed)
    return procedureTypes.filter(
      (pt) =>
        fold(pt.name).includes(needle) ||
        // The discipline is searchable, because it is how staff name a group of acts out loud: « endo » must
        // reach « Traitement de canal », whose own name contains none of those letters.
        fold(pt.category ?? "").includes(needle) ||
        fold(pt.description ?? "").includes(needle),
    )
  }, [procedureTypes, searching, trimmed, groups])

  const clampedCursor = Math.min(cursor, Math.max(0, matches.length - 1))
  const exactMatch = matches.some((pt) => fold(pt.name) === fold(trimmed))

  /**
   * Keep the highlighted row in view when arrowing past the visible window.
   *
   * <p>⚠️ Scrolls **this container and nothing else**, and only once a key has actually moved the cursor.
   * `scrollIntoView` walks up and scrolls *every* scrollable ancestor, so inside the fiche's mobile sheet it
   * moved the `DialogBody` too: the picker opened with the form pushed 86 px up (the Patient field gone) and its
   * own list 266 px down, which on a phone reads as a list that cannot be scrolled to its top. Nothing needs
   * bringing into view before the first ↑/↓ — the catalogue belongs at row one — so the ref gates the effect
   * instead of the effect running on mount.</p>
   */
  const cursorMoved = useRef(false)
  useEffect(() => {
    const list = listRef.current
    const row = list?.querySelector<HTMLElement>('[data-cursor="true"]')
    if (!cursorMoved.current || !list || !row) return
    // Rect deltas rather than `offsetTop`: the rows sit inside per-group wrappers and the list is not a
    // positioned ancestor, so `offsetTop` is measured against the dialog rather than against this box.
    const listBox = list.getBoundingClientRect()
    const rowBox = row.getBoundingClientRect()
    if (rowBox.top < listBox.top) list.scrollTop += rowBox.top - listBox.top
    else if (rowBox.bottom > listBox.bottom) list.scrollTop += rowBox.bottom - listBox.bottom
  }, [clampedCursor, matches.length])

  const handleKeyDown = (e: React.KeyboardEvent<HTMLInputElement>) => {
    if (e.key === "ArrowDown") {
      e.preventDefault()
      cursorMoved.current = true
      setCursor((c) => Math.min(c + 1, matches.length - 1))
    } else if (e.key === "ArrowUp") {
      e.preventDefault()
      cursorMoved.current = true
      setCursor((c) => Math.max(c - 1, 0))
    } else if (e.key === "Enter") {
      // Owned here so it never reaches the Dialog — see the component note above.
      e.preventDefault()
      e.stopPropagation()
      const hit = matches[clampedCursor]
      if (hit) onPick(hit)
      else if (trimmed) onFreeText(trimmed)
    } else if (e.key === "Escape" && onCancel) {
      e.preventDefault()
      e.stopPropagation()
      onCancel()
    }
  }

  const renderRow = (pt: ProcedureTypeDto, index: number) => {
    const style = pt.resultingCondition ? conditionStyle(pt.resultingCondition) : null
    return (
      <button
        key={pt.id}
        type="button"
        role="option"
        aria-selected={index === clampedCursor}
        data-cursor={index === clampedCursor}
        disabled={disabled}
        onClick={() => onPick(pt)}
        onMouseEnter={() => setCursor(index)}
        /*
         * ⚠️ `min-h-11` is what does the work; `touch-target` is here for consistency with the rest of the app
         * and is a no-op once the paint already clears 44px.
         *
         * These rows were 33px, stacked with no gap. `touch-target` ALONE would have been actively harmful:
         * a 44px overlay centred on a 33px row reaches 5.5px into the rows above and below, and the later
         * sibling wins — so the bottom of every act selected the act underneath it. This is the fiche's primary
         * control and it prices the visit, so picking the neighbouring act is a billing error, not a nuisance.
         * Painting the floor keeps the hit area and the row the same rectangle.
         */
        className={cn(
          "touch-target flex min-h-11 w-full items-center gap-2.5 px-3 py-1.5 text-left transition-colors",
          index === clampedCursor ? "bg-accent" : "hover:bg-muted",
        )}
      >
        {/* The procedure's own palette colour — the same stripe the calendar uses for its slots. */}
        <span
          className="h-5 w-1 shrink-0 rounded-full"
          style={{ backgroundColor: pt.colorHex || "var(--border)" }}
          aria-hidden="true"
        />
        <span
          className={cn("min-w-0 flex-1 truncate text-sm", index === clampedCursor && "font-medium")}
          title={pt.name}
        >
          {pt.name}
        </span>
        {/*
          The discipline, on the row, but ONLY while searching.
          Searching flattens the list and drops the group headings, so without this the one piece of context that
          says « this is the endodontic one » disappears at exactly the moment two similarly-named acts from
          different disciplines end up next to each other. While browsing it would be pure repetition — the
          heading two rows up already says it.
        */}
        {searching && pt.category && (
          <span className="max-w-[7.5rem] shrink-0 truncate rounded bg-muted px-1.5 py-0.5 text-2xs text-muted-foreground">
            {pt.category}
          </span>
        )}
        {style && <span className="shrink-0 text-2xs text-muted-foreground">{style.label}</span>}
        <span className="w-[86px] shrink-0 text-right text-xs tabular-nums text-muted-foreground">
          {pt.defaultCost != null && pt.defaultCost > 0 ? formatDT(pt.defaultCost) : "—"}
        </span>
      </button>
    )
  }

  return (
    <div className="grid">
      <div className="flex items-center gap-2 border-b bg-card px-3 py-2">
        <Search className="h-4 w-4 shrink-0 text-muted-foreground" aria-hidden="true" />
        <input
          ref={inputRef}
          value={query}
          onChange={(e) => {
            setQuery(e.target.value)
            setCursor(0)
          }}
          onKeyDown={handleKeyDown}
          disabled={disabled}
          placeholder="Rechercher un acte…"
          aria-label="Rechercher un acte au catalogue"
          autoComplete="off"
          className="h-7 min-w-0 flex-1 bg-transparent text-sm outline-none placeholder:text-muted-foreground"
        />
        <span className="shrink-0 text-2xs tabular-nums text-muted-foreground">
          {matches.length}/{procedureTypes.length}
        </span>
      </div>

      {/*
        `scrollbar-thin` because this list scrolls *inside* a dialog body that is itself scrolling.

        « Ajouter une fiche médicale » opens with no act picked, so the picker is what the slot renders (see
        `act-slot.tsx`) and the modal lands in this state every single time. The body needs 965 px in 599 and the
        list 1156 px in 290, so both drew a full-width gutter and the dialog showed **two parallel scrollbars
        about 15 px apart** — nowhere else in the app does that, and it reads as a rendering fault rather than as
        two scrollable regions.

        The inner one is the one to quieten: it is subordinate, and the list genuinely needs its own scroller —
        it is a `listbox` with ↑/↓ cursor navigation that scrolls the active row into view, which only works
        against a box it owns. Same treatment as the rail's nav, the other nested scroller in this product.
      */}
      <div
        ref={listRef}
        role="listbox"
        aria-label="Actes du catalogue"
        className="scrollbar-thin max-h-[290px] overflow-y-auto py-1"
      >
        {matches.length === 0 ? (
          <p className="px-3 py-4 text-sm text-muted-foreground">
            {procedureTypes.length === 0 ? "Catalogue vide." : "Aucun acte ne correspond."}
          </p>
        ) : searching ? (
          matches.map(renderRow)
        ) : (
          groups.map(({ label, items }) => (
            <div key={label}>
              <p className="px-3 pb-0.5 pt-2 text-2xs font-medium uppercase tracking-[0.12em] text-muted-foreground">
                {label}
              </p>
              {items.map((pt) => renderRow(pt, matches.indexOf(pt)))}
            </div>
          ))
        )}
      </div>

      {searching && !exactMatch && (
        <button
          type="button"
          disabled={disabled}
          onClick={() => onFreeText(trimmed)}
          // Same 44px floor as the catalogue rows above: this is the escape hatch for an act the catalogue does
          // not carry, and it must not be the hardest row in the list to hit.
          className="touch-target flex min-h-11 w-full items-center gap-2 border-t border-dashed px-3 py-2 text-left text-xs hover:bg-muted"
        >
          <span className="shrink-0 text-muted-foreground">+</span>
          <span className="min-w-0 flex-1 truncate">
            Enregistrer «&nbsp;<span className="font-medium">{trimmed}</span>&nbsp;» comme acte libre
          </span>
          <span className="shrink-0 text-2xs text-muted-foreground">sans tarif catalogue</span>
        </button>
      )}

      <div className="flex flex-wrap items-center justify-between gap-2 border-t bg-card px-3 py-1.5 text-2xs text-muted-foreground">
        <span className="flex items-center gap-1">
          <kbd className="rounded border border-b-2 px-1 text-2xs">↑</kbd>
          <kbd className="rounded border border-b-2 px-1 text-2xs">↓</kbd>
          parcourir
          <span className="mx-0.5 opacity-50">·</span>
          <kbd className="rounded border border-b-2 px-1 text-2xs">↵</kbd>
          choisir
          {onCancel && (
            <>
              <span className="mx-0.5 opacity-50">·</span>
              <kbd className="rounded border border-b-2 px-1 text-2xs">esc</kbd>
              annuler
            </>
          )}
        </span>
        {/* The only way back to a proposed act once the catalogue is open, and it was a bare `<button>` with no
            padding — a ~16px target on the footer of the fiche's primary control. A real `Button` carries the
            44px floor from `buttonVariants` and looks like something you can press. */}
        {onCancel && (
          <Button
            type="button"
            variant="ghost"
            size="sm"
            onClick={onCancel}
            disabled={disabled}
            className="-my-1 text-2xs"
          >
            Garder l&apos;acte actuel
          </Button>
        )}
      </div>
    </div>
  )
}
