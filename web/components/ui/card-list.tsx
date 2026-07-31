"use client"

import * as React from "react"
import Link from "next/link"
import { cn } from "@/lib/utils"

/**
 * `CardList` — what a table becomes below `md:` (AC-13, AC-14).
 *
 * ⚠️ It is a **replacement**, not a reflow. The obvious implementation — `display: block` on the rows and cells —
 * is the one thing that must not be done: it strips the implicit `row`/`cell` roles in every browser, so a screen
 * reader reads « Ben Salah 45,000 12/03 Payée » with no idea which number is the money and which is the date.
 * Across 22 surfaces, several of them clinical and financial, that is not an acceptable trade for a smaller DOM.
 * So the `<table>` is *absent* below `md:` and a real list stands in its place, the same two-trees pattern the
 * navigation rail already uses.
 *
 * Field labels survive because each card is a **description list**: `<dt>` carries the column name that the
 * `<th>` used to, `<dd>` the value. That is the markup screen readers already know how to pair.
 *
 * The card is one interactive element, not a clickable `<div>` full of buttons: the title is the button or link,
 * stretched over the whole card by a pseudo-element, and the action menu sits above it. Nesting a menu inside a
 * button would be invalid, and making the container itself clickable would swallow the menu's taps.
 */

export interface CardListField {
  label: string
  value: React.ReactNode
}

/** A field the caller wants dropped entirely — see the AC-17 note on `fields`. */
type MaybeField = CardListField | null | undefined | false

interface CardListProps<T> {
  items: T[]
  getKey: (item: T) => string

  /** The row's identity. Truncates to one line — the full value stays reachable through the card's own detail. */
  title: (item: T) => React.ReactNode
  /** A second identifying line, when the title alone repeats across the list (lab orders' « Travail »). */
  subtitle?: (item: T) => React.ReactNode
  /** Badges. Rendered beside the title, because a status is read with the identity, not among the fields. */
  status?: (item: T) => React.ReactNode

  /**
   * The remaining columns, in the plan's priority order (money, then date, then the rest).
   *
   * ⚠️ AC-17: a field with **no value is omitted, not rendered as « — »**. A dash is a value a reader has to
   * decode; absence is self-explanatory and saves a line on a screen that has few. Return `null`/`false` for a
   * field that does not apply, and an empty/`null` `value` is dropped for you.
   */
  fields: (item: T) => MaybeField[]

  /** One menu per card (AC-15). `treatment-plans-table` is the template the other surfaces follow. */
  actions?: (item: T) => React.ReactNode

  /**
   * A control that belongs to the **row itself** rather than to its action menu — a selection checkbox, the
   * reorder arrows. Rendered before the title and, like `actions`, above the stretched-title overlay.
   *
   * ⚠️ Deliberately distinct from `actions`: the plan's acts are ticked to be grouped into one séance and moved
   * up and down in the clinical order, and folding either into a menu would hide the current state (is this act
   * ticked?) behind a tap — the same reason `lab-orders` keeps its `<select>` as a field rather than a menu item.
   */
  leading?: (item: T) => React.ReactNode

  /** A left accent bar — for the per-procedure colour, the reminder status stripe, the category swatch. */
  accent?: (item: T) => string | undefined
  /** Dims the card without hiding it — a cancelled invoice, a voided movement, an inactive catalog entry. */
  muted?: (item: T) => boolean

  /** Makes the title a button. Mutually exclusive with `href`. */
  onSelect?: (item: T) => void
  /**
   * Makes the title a link. Use for a row whose only behaviour is navigation.
   *
   * ⚠️ Returns `string | undefined` on purpose: a list can be *partly* navigable. The caisse statement is the
   * case — a payment, an avoir and an échéance each open their own record, but an expense has no page to open,
   * so its card is plain text rather than a link to nowhere.
   */
  href?: (item: T) => string | undefined

  /** For a row the page scrolls to — `stock-table`'s low-stock deep link keeps its target this way. */
  itemRef?: (item: T) => React.Ref<HTMLLIElement> | undefined

  /**
   * Three states, not two (AC-18). `loading` wins; then `empty` when there is nothing; then the list. The caller
   * passes the *right* empty message — « aucun résultat pour ce filtre » and « la liste est vide » are different
   * facts and eight of these surfaces already tell them apart.
   */
  loading?: boolean
  skeletonRows?: number
  empty?: React.ReactNode

  className?: string
  /** Names the list for assistive tech — « Factures », « Patients ». Required: an unnamed list is a pile. */
  ariaLabel: string
}

export function CardList<T>({
  items,
  getKey,
  title,
  subtitle,
  status,
  fields,
  actions,
  leading,
  accent,
  muted,
  onSelect,
  href,
  itemRef,
  loading = false,
  skeletonRows = 4,
  empty,
  className,
  ariaLabel,
}: CardListProps<T>) {
  if (loading) {
    return (
      <div
        role="status"
        aria-label="Chargement…"
        aria-busy="true"
        className={cn("space-y-2 p-3", className)}
      >
        {Array.from({ length: skeletonRows }).map((_, i) => (
          <div key={i} className="space-y-2 rounded-lg border bg-card p-3">
            <div className="h-4 w-2/5 animate-pulse rounded bg-muted" />
            <div className="h-3 w-3/5 animate-pulse rounded bg-muted" />
            <div className="h-3 w-1/3 animate-pulse rounded bg-muted" />
          </div>
        ))}
      </div>
    )
  }

  if (items.length === 0) {
    return empty ? (
      <div className={cn("px-3 py-10 text-center text-sm text-muted-foreground", className)}>{empty}</div>
    ) : null
  }

  return (
    <ul aria-label={ariaLabel} className={cn("space-y-2 p-3", className)}>
      {items.map((item) => {
        const accentColour = accent?.(item)
        const rowActions = actions?.(item)
        const rowLeading = leading?.(item)
        const rowStatus = status?.(item)
        const rowSubtitle = subtitle?.(item)
        const rowHref = href?.(item)

        // An empty value is dropped here rather than at 22 call sites (AC-17).
        const rowFields = fields(item).filter(
          (f): f is CardListField =>
            Boolean(f) && f !== null && f !== undefined && f !== false && !isEmptyValue((f as CardListField).value)
        )

        const heading = title(item)
        const interactive = Boolean(rowHref || onSelect)

        return (
          <li
            key={getKey(item)}
            ref={itemRef?.(item)}
            className={cn(
              "relative overflow-hidden rounded-lg border bg-card p-3",
              // `focus-within` rather than `focus`: the ring belongs to the card, but the focusable element is
              // the stretched title inside it.
              "focus-within:ring-2 focus-within:ring-ring focus-within:ring-offset-2",
              muted?.(item) && "opacity-60"
            )}
          >
            {accentColour && (
              <span
                aria-hidden="true"
                className="absolute inset-y-0 left-0 w-1"
                style={{ backgroundColor: accentColour }}
              />
            )}

            <div className={cn("flex items-start justify-between gap-2", accentColour && "ps-2")}>
              {rowLeading && <div className="relative z-10 shrink-0">{rowLeading}</div>}

              <div className="min-w-0 flex-1">
                <div className="flex flex-wrap items-center gap-x-2 gap-y-1">
                  {/*
                    `after:absolute after:inset-0` stretches the title over the whole card, so the card is one big
                    tap target while remaining a single real button/link in the accessibility tree. The action
                    menu below carries `relative z-10` to sit above that overlay.
                  */}
                  {rowHref ? (
                    <Link
                      href={rowHref}
                      className="truncate font-medium text-foreground outline-none after:absolute after:inset-0"
                    >
                      {heading}
                    </Link>
                  ) : onSelect ? (
                    <button
                      type="button"
                      onClick={() => onSelect(item)}
                      className="truncate text-start font-medium text-foreground outline-none after:absolute after:inset-0"
                    >
                      {heading}
                    </button>
                  ) : (
                    <span className="truncate font-medium text-foreground">{heading}</span>
                  )}
                  {rowStatus}
                </div>
                {rowSubtitle && (
                  <p className={cn("mt-0.5 truncate text-sm text-muted-foreground", interactive && "pe-2")}>
                    {rowSubtitle}
                  </p>
                )}
              </div>

              {rowActions && <div className="relative z-10 shrink-0">{rowActions}</div>}
            </div>

            {rowFields.length > 0 && (
              <dl className={cn("mt-2 space-y-1", accentColour && "ps-2")}>
                {rowFields.map((f) => (
                  <div key={f.label} className="flex items-baseline justify-between gap-3">
                    <dt className="shrink-0 font-mono text-2xs uppercase tracking-[0.07em] text-muted-foreground">
                      {f.label}
                    </dt>
                    <dd className="min-w-0 text-end text-sm text-foreground">{f.value}</dd>
                  </div>
                ))}
              </dl>
            )}
          </li>
        )
      })}
    </ul>
  )
}

/**
 * "No value" for AC-17's purposes. Deliberately narrow: `0` and `false` are values a clinic cares about (a zero
 * balance is a fact), so only nullish and blank strings are dropped.
 */
function isEmptyValue(value: React.ReactNode): boolean {
  return value === null || value === undefined || (typeof value === "string" && value.trim() === "")
}

/**
 * The two halves of a converted surface, so a call site reads as one decision rather than two class strings.
 *
 * Below `md:` the list shows and the table is gone; at `md:` and up the reverse. Callers pass
 * `containerClassName={TABLE_ONLY}` to `<Table>` and `className={CARDS_ONLY}` to `<CardList>`.
 */
export const CARDS_ONLY = "md:hidden"
export const TABLE_ONLY = "hidden md:block"
