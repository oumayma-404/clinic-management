"use client"

import * as React from "react"

import { cn } from "@/lib/utils"

/**
 * ⚠️ The container paints **`bg-card`** — a table is always a white surface, never a hole onto the page.
 *
 * <p>It used to paint nothing, which was invisible while `--background` and `--card` were 1 % apart: a table inside
 * a `Card` looked identical to one inside a bare `rounded-md border` div. Tinting the page ground made the
 * difference real, and the six tables wrapped in a plain bordered div started showing the teal through — legible
 * enough to notice, not legible enough to read. Fixing it here rather than adding `bg-card` to each wrapper means a
 * seventh table cannot be added wrong.</p>
 *
 * <p>`rounded-[inherit]` takes the wrapper's own radius, so the white surface follows a rounded border instead of
 * squaring off its corners over it (and resolves to 0 when the wrapper has no radius).</p>
 */
function Table({ className, ...props }: React.ComponentProps<"table">) {
  return (
    <div
      data-slot="table-container"
      className="relative w-full overflow-x-auto rounded-[inherit] bg-card"
    >
      <table
        data-slot="table"
        className={cn("w-full caption-bottom text-sm", className)}
        {...props}
      />
    </div>
  )
}

/**
 * `sticky` keeps the column heads visible while the body scrolls — pass it on lists long enough to lose them
 * (invoices, patients, appointments). Deliberately opt-in: on a five-row table a sticky header is a shadow that
 * never earns itself, and it needs a scroll container to be sticky *inside*.
 */
function TableHeader({
  className,
  sticky = false,
  ...props
}: React.ComponentProps<"thead"> & { sticky?: boolean }) {
  return (
    <thead
      data-slot="table-header"
      className={cn(
        "[&_tr]:border-b",
        sticky && "sticky top-0 z-10 bg-card [&_tr]:border-b",
        className
      )}
      {...props}
    />
  )
}

function TableBody({ className, ...props }: React.ComponentProps<"tbody">) {
  return (
    <tbody
      data-slot="table-body"
      className={cn("[&_tr:last-child]:border-0", className)}
      {...props}
    />
  )
}

function TableFooter({ className, ...props }: React.ComponentProps<"tfoot">) {
  return (
    <tfoot
      data-slot="table-footer"
      className={cn(
        "bg-muted/50 border-t font-medium [&>tr]:last:border-b-0",
        className
      )}
      {...props}
    />
  )
}

/**
 * `muted` dims a row whose record no longer counts — a cancelled invoice, a voided payment.
 *
 * <p>It exists because those rows were rendering in full-strength ink next to a red « Annulée » pill, so the row
 * that matters least was as loud as the ones that matter. The badge says what happened; the row's weight should
 * agree with it.</p>
 */
function TableRow({
  className,
  muted = false,
  ...props
}: React.ComponentProps<"tr"> & { muted?: boolean }) {
  return (
    <tr
      data-slot="table-row"
      className={cn(
        // Hairline between rows + an accent-tinted hover, matching every other hoverable surface in the app.
        "border-b border-border/60 transition-colors last:border-0",
        "hover:bg-accent/40 data-[state=selected]:bg-accent",
        muted && "text-muted-foreground",
        className
      )}
      {...props}
    />
  )
}

/**
 * A column head, **one level below the data**.
 *
 * <p>It was `text-foreground` at the same size as the cells, so the labels were exactly as black as the values and
 * the eye had to work out which of the two was the content. Monospace uppercase at `muted-foreground` settles that
 * instantly and matches the section eyebrows on the dashboard, so the whole app labels data the same way.</p>
 */
function TableHead({ className, ...props }: React.ComponentProps<"th">) {
  return (
    <th
      data-slot="table-head"
      className={cn(
        "h-9 px-3 text-left align-middle whitespace-nowrap",
        "font-mono text-[10.5px] font-medium uppercase tracking-[0.07em] text-muted-foreground",
        "[&:has([role=checkbox])]:pr-0 [&>[role=checkbox]]:translate-y-[2px]",
        className
      )}
      {...props}
    />
  )
}

/**
 * A cell. `px-3 py-2.5` rather than a flat `p-2`: rows need vertical air to be scannable, columns do not need as
 * much horizontal padding as they were given, and the two were the same number for no reason.
 *
 * <p>Use `numeric` on any column of figures or dates — it right-aligns and applies `tabular-nums`. That is the
 * single highest-value change in this file: `/factures` shows three columns of dinars, and with proportional
 * digits their commas do not line up, so the amounts cannot be compared vertically at all.</p>
 */
function TableCell({
  className,
  numeric = false,
  ...props
}: React.ComponentProps<"td"> & { numeric?: boolean }) {
  return (
    <td
      data-slot="table-cell"
      className={cn(
        "px-3 py-2.5 align-middle whitespace-nowrap",
        numeric && "text-right tabular-nums",
        "[&:has([role=checkbox])]:pr-0 [&>[role=checkbox]]:translate-y-[2px]",
        className
      )}
      {...props}
    />
  )
}

function TableCaption({
  className,
  ...props
}: React.ComponentProps<"caption">) {
  return (
    <caption
      data-slot="table-caption"
      className={cn("text-muted-foreground mt-4 text-sm", className)}
      {...props}
    />
  )
}

/**
 * The line under a table: how many rows of how many, and the unit **once**.
 *
 * <p>« DT » printed on every cell of three money columns is fifteen repetitions of one fact, and it pushes the
 * digits away from the right edge so the alignment `numeric` just bought is spent again. Stated here, the column
 * is pure number.</p>
 */
function TableMeta({ className, children, ...props }: React.ComponentProps<"div">) {
  return (
    <div
      data-slot="table-meta"
      className={cn(
        "flex flex-wrap items-center justify-between gap-2 border-t px-3 py-2",
        "font-mono text-[11px] text-muted-foreground",
        className
      )}
      {...props}
    >
      {children}
    </div>
  )
}

export {
  Table,
  TableMeta,
  TableHeader,
  TableBody,
  TableFooter,
  TableHead,
  TableRow,
  TableCell,
  TableCaption,
}
