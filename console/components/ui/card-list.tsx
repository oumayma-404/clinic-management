import * as React from "react";

import { cn } from "@/lib/utils";

/**
 * What a table becomes below `lg:` — adapted from `web/components/ui/card-list.tsx`, and adapted rather than
 * copied because that version carries a per-row action menu and a primary-action slot this part has no actions
 * to put in.
 *
 * ⚠️ **It is a replacement, not a reflow, and that is the load-bearing decision.** The obvious implementation —
 * `display: block` on the rows and cells — strips the implicit `row`/`cell` roles in every browser, so a screen
 * reader reads « Cabinet Ben Ali 412 96 14 320,000 » with no idea which number is the money and which is the
 * patient count. The `<table>` is therefore **absent** below the breakpoint and a real list stands in its place.
 *
 * Field labels survive because each card is a **description list**: `<dt>` carries the column name the `<th>`
 * used to, `<dd>` the value. That is markup screen readers already know how to pair.
 */

export interface CardListField {
  label: string;
  value: React.ReactNode;
}

/** A field the caller wants dropped entirely — see the note on `fields`. */
type MaybeField = CardListField | null | undefined | false;

interface CardListProps<T> {
  items: T[];
  getKey: (item: T) => string;
  /** The row's identity — the cabinet's name. */
  title: (item: T) => React.ReactNode;
  /** A second identifying line: the city, and « jamais mesuré » where that applies. */
  subtitle?: (item: T) => React.ReactNode;
  /** Rendered beside the title, because a state is read with the identity rather than among the figures. */
  status?: (item: T) => React.ReactNode;
  /**
   * The remaining columns, in priority order.
   *
   * ⚠️ A field with **no value is omitted, not rendered as « — »**: a dash is a value a reader has to decode,
   * absence is self-explanatory, and it saves a line on the screen that has fewest. Return `null`/`false` for a
   * field that does not apply.
   */
  fields: (item: T) => MaybeField[];
  className?: string;
}

export function CardList<T>({ items, getKey, title, subtitle, status, fields, className }: CardListProps<T>) {
  return (
    <ul className={cn("flex flex-col gap-3", className)}>
      {items.map((item) => {
        const shown = fields(item).filter((f): f is CardListField => Boolean(f) && !isEmpty((f as CardListField).value));

        return (
          <li key={getKey(item)} className="rounded-lg border border-border bg-card p-4">
            <div className="flex flex-wrap items-start justify-between gap-2">
              <div className="min-w-0">
                <p className="truncate font-medium">{title(item)}</p>
                {subtitle ? <p className="mt-0.5 truncate text-sm text-muted-foreground">{subtitle(item)}</p> : null}
              </div>
              {status ? <div className="shrink-0">{status(item)}</div> : null}
            </div>

            {shown.length > 0 ? (
              // Two columns from 380 px, one below it: at 320 px a label and its value need the full width, and
              // a two-column grid there wraps every figure onto its own line anyway — with the labels misaligned.
              <dl className="mt-3 grid grid-cols-1 gap-x-4 gap-y-2 min-[380px]:grid-cols-2">
                {shown.map((field) => (
                  <div key={field.label} className="min-w-0">
                    <dt className="text-xs text-muted-foreground">{field.label}</dt>
                    <dd className="truncate text-sm">{field.value}</dd>
                  </div>
                ))}
              </dl>
            ) : null}
          </li>
        );
      })}
    </ul>
  );
}

function isEmpty(value: React.ReactNode): boolean {
  return value === null || value === undefined || value === "";
}
