"use client"

import { Banknote, CreditCard, Landmark, ReceiptText, type LucideIcon } from "lucide-react"
import type { CaisseMethodTotalDto } from "@/lib/api/types"
import { formatDT } from "@/lib/format"
import { cn } from "@/lib/utils"

/**
 * « dont » — la caisse's encaissements broken down by payment method (L8 slice B).
 *
 * <p>Before it, the till showed one « Encaissements » figure summed across every method, so the owner closing the
 * drawer could not tell the notes in it from a post-dated cheque nobody has banked. That is the one distinction a
 * cash count is *made against*.</p>
 *
 * <p>⚠️ **Each figure is also the filter for the movements behind it.** The extrait below already lists every
 * movement; making « Chèque · 450,000 » the control that narrows it to those cheques is the same
 * figure-links-to-its-records rule the dashboard follows, and it is the reason this is a row of buttons rather
 * than four more read-only cells. A second, separate « Mode » Select would put the number and the way to inspect
 * it in different places.</p>
 *
 * <p>Sums to « Encaissements » exactly — the server computes it as a `GROUP BY` sibling of that very total — so
 * the row can sit directly beneath it without a reconciliation footnote.</p>
 */
export function CashInByMethod({
  totals,
  selected,
  onSelect,
}: {
  totals: CaisseMethodTotalDto[]
  /** The method currently filtering the extrait, or null for « tous les modes ». */
  selected: string | null
  onSelect: (method: string | null) => void
}) {
  // Nothing collected in the window: four « 0,000 » buttons that filter an empty statement is noise, and the
  // « Encaissements 0,000 » above already says it. A zero *within* a non-empty window is kept, because « Espèces
  // 0,000 » on a day of cheques is exactly the fact somebody is looking for.
  if (totals.length === 0 || totals.every((t) => t.amount === 0)) return null

  return (
    <div className="flex flex-wrap items-center gap-2" role="group" aria-label="Encaissements par mode de paiement">
      <span className="text-xs font-medium uppercase tracking-wide text-muted-foreground">dont</span>
      {totals.map((total) => {
        const Icon = METHOD_ICONS[total.method] ?? ReceiptText
        const active = selected === total.method
        return (
          <button
            key={total.method}
            type="button"
            onClick={() => onSelect(active ? null : total.method)}
            aria-pressed={active}
            // Adjacent siblings, so they GROW their own box (`coarse:py-2.5`) rather than wearing
            // `.touch-target` — an overlaid 44px hit area on a wrapped row of chips overhangs its neighbours,
            // and the later sibling paints last, so it would steal their taps (§ 2).
            className={cn(
              // `py-3`, not `py-2.5`: measured 42 px on a coarse pointer, two short of the floor.
              "inline-flex items-center gap-1.5 rounded-full border px-3 py-1.5 text-sm transition-colors coarse:py-3",
              "focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring",
              active
                ? "border-primary bg-primary/10 text-primary"
                : "border-border bg-card text-muted-foreground hover:bg-muted/60 hover:text-foreground",
            )}
            title={
              active
                ? `Afficher tous les modes dans l'extrait`
                : `Ne montrer que les mouvements en ${total.label.toLowerCase()} dans l'extrait`
            }
          >
            <Icon className="size-3.5 shrink-0" strokeWidth={1.75} aria-hidden="true" />
            <span className="min-w-0 truncate">{total.label}</span>
            <span className={cn("font-semibold tabular-nums", active ? "text-primary" : "text-foreground")}>
              {formatDT(total.amount)}
            </span>
          </button>
        )
      })}
    </div>
  )
}

/**
 * Icon per storage key. Not exhaustive over a closed enum on purpose — the server enumerates `PaymentMethod`
 * itself, so a method added there arrives here before this map knows about it, and falling back to a generic
 * receipt glyph is better than a `tsc` error blocking a value the API already returns.
 */
const METHOD_ICONS: Record<string, LucideIcon> = {
  Cash: Banknote,
  Cheque: ReceiptText,
  Card: CreditCard,
  Transfer: Landmark,
}
