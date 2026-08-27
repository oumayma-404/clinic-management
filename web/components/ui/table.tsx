"use client"

import * as React from "react"

import { cn } from "@/lib/utils"

/**
 * ⚠️ The container paints **`bg-card`** — a table is always a white surface, never a hole onto the page.
 *
 * <p>It used to paint nothing, which was invisible while `--background` and `--card` were 1 % apart: a table inside
 * a `Card` looked identical to one inside a bare `rounded-md border` div. Tinting the page ground made the
 * difference real, and the six tables wrapped in a plain bordered div started showing the ground through — legible
 * enough to notice, not legible enough to read. Fixing it here rather than adding `bg-card` to each wrapper means a
 * seventh table cannot be added wrong.</p>
 *
 * <p>`rounded-[inherit]` takes the wrapper's own radius, so the white surface follows a rounded border instead of
 * squaring off its corners over it (and resolves to 0 when the wrapper has no radius).</p>
 */
function Table({
  className,
  containerClassName,
  ...props
}: React.ComponentProps<"table"> & {
  /**
   * Classes for the scroll container, which is otherwise unreachable from a call site.
   *
   * This exists for one reason: below `md:` the table must be **absent**, not merely narrow (AC-13/AC-14), and
   * the element that has to disappear is this wrapper — hiding the `<table>` alone would leave its scrolling
   * container behind. Pass `TABLE_ONLY` from `ui/card-list` alongside a `<CardList className={CARDS_ONLY}>`.
   */
  containerClassName?: string
}) {
  return (
    <div
      data-slot="table-container"
      /*
       * A right-edge fade, so a table that scrolls sideways SAYS it scrolls sideways.
       *
       * Since cells wrap (see `TableCell`) a table fits its container in almost every case, and this fade is
       * the last-resort cue for the ones that still cannot — a ten-column surface on a narrow tablet, where the
       * columns' *min-content* widths already exceed the box. It used to fire on nearly every table.
       *
       * `pointer-events-none` is load-bearing: the fade sits over the last column, and without it the overlay
       * would swallow taps on that column's row actions.
       */
      className={cn(
        // `@container/table-scroll` so `TableEmptyRow` can size itself to the VISIBLE width — see its own note.
        "@container/table-scroll relative w-full overflow-x-auto rounded-[inherit] bg-card",
        "after:pointer-events-none after:absolute after:inset-y-0 after:right-0 after:w-6 after:bg-gradient-to-l after:from-card after:to-transparent",
        containerClassName,
      )}
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
 *
 * <p>⚠️ **A label wraps**, and it must: uppercase at `tracking-[0.07em]` makes a header the widest thing in its
 * column far more often than the data is — « Date de naissance » measured 149 px over a 90 px column of
 * `dd/MM/yyyy`. Held on one line it sets a floor no value asked for. `align-bottom` sits a two-line label on the
 * same baseline as its one-line neighbours instead of centring it against them.</p>
 */
function TableHead({ className, ...props }: React.ComponentProps<"th">) {
  return (
    <th
      data-slot="table-head"
      className={cn(
        "h-9 px-3 text-left align-bottom",
        "font-mono text-2xs font-medium uppercase tracking-[0.07em] text-muted-foreground",
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
 * <p>Use `numeric` on any column of figures or dates — it right-aligns, applies `tabular-nums`, and keeps the
 * value on one line. That is the single highest-value change in this file: `/factures` shows three columns of
 * dinars, and with proportional digits their commas do not line up, so the amounts cannot be compared
 * vertically at all.</p>
 *
 * <p>⚠️ **A cell wraps by default, and that is what keeps a table inside the screen.** It was
 * `whitespace-nowrap`, so a table's intrinsic width was the sum of its longest *unbreakable* value and no column
 * could ever compress: one free-text column set the width of the whole surface and pushed the rest out of the
 * scrollport. Measured signed-in, `/caisse`'s ledger rendered 1563 px inside a 1084 px box on a 1440 px laptop —
 * « Mode », « Entrée », « Sortie » and « Solde » simply absent — because « Libellé » (495 px) and « Mode »
 * (323 px) hold sentences. `/stock` hid 254 px and `/waiting-list` 161 px at the same width. The reader's only
 * clue was a 24 px fade, so those columns read as non-existent rather than as off-screen.</p>
 *
 * <p>Wrapping shrinks a column to its longest *word* instead, which fits every table in the app at 1440 px and
 * nearly all at 820 px. `overflow-x-auto` on the container stays as the last resort. Four separate workarounds
 * existed for the old default — this container's fade, `TableEmptyRow`'s `sticky w-[100cqi]`, `CARDS_ONLY_LG`,
 * and a hand-added `whitespace-normal` in `reminder-log-table` — and all four were treating this symptom.</p>
 *
 * <p>Pass `whitespace-nowrap` at the call site for a value that must stay atomic — a `d MMM yyyy` date, a phone
 * number, a `F-2026-0142` reference — since those break at their spaces and hyphens like any other text.</p>
 *
 * <p>`clamp` caps a free-text column at two lines and puts the full value in the cell's `title`, so one long
 * note cannot make its row four lines tall. Opt-in, never the default: it needs `display: -webkit-box`, which
 * would flatten an action column's flex row of buttons.</p>
 */
function TableCell({
  className,
  numeric = false,
  clamp = false,
  title,
  children,
  ...props
}: React.ComponentProps<"td"> & { numeric?: boolean; clamp?: boolean }) {
  return (
    <td
      data-slot="table-cell"
      title={title ?? (clamp && typeof children === "string" ? children : undefined)}
      className={cn(
        "px-3 py-2.5 align-middle break-words",
        numeric && "text-right tabular-nums whitespace-nowrap",
        "[&:has([role=checkbox])]:pr-0 [&>[role=checkbox]]:translate-y-[2px]",
        className
      )}
      {...props}
    >
      {clamp ? <span className="line-clamp-2">{children}</span> : children}
    </td>
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
        "font-mono text-2xs text-muted-foreground",
        className
      )}
      {...props}
    >
      {children}
    </div>
  )
}

/**
 * The « there is nothing here » row of a table — the one place a `colSpan` cell holds a whole `EmptyState`.
 *
 * <p>⚠️ <b>It exists because a full-width cell is as wide as the TABLE, not as wide as the screen.</b> A table
 * whose columns cannot compress below the box still scrolls, which is right for rows — but an empty state
 * centred in that cell is centred on a width nobody can see: measured at 820 px, `/stock`'s invite sat in a
 * 752 px cell inside a 451 px viewport, so « Aucun article en sto… » and half of « Ajouter un article » were off
 * screen with only a horizontal scrollbar to say so. Four tables had it, and it is the one empty state a
 * first-run clinic always meets. Cells wrapping made this rarer, not impossible — the header row alone can still
 * outrun a narrow scrollport.</p>
 *
 * <p><b>`sticky left-0 w-[100cqi]` is the fix.</b> `cqi` reads the scroll container's own inline size — its
 * <i>visible</i> width, not its scroll width — so the block is exactly as wide as what the reader can see and
 * `EmptyState` (which is `items-center text-center`) centres inside that. `sticky left-0` keeps it aligned to
 * the scrollport however far the table is scrolled sideways.</p>
 *
 * <p>⚠️ A first pass used `w-fit`, which is visible but <b>left-aligned</b> — and on the ordinary case, a table
 * narrower than its container, that is simply an empty state that is not centred. Sizing to the container gets
 * both: centred when everything fits, whole when it does not.</p>
 */
function TableEmptyRow({
  colSpan,
  className,
  children,
}: {
  colSpan: number
  className?: string
  children: React.ReactNode
}) {
  return (
    <TableRow className="hover:bg-transparent">
      <TableCell colSpan={colSpan} className={cn("p-0", className)}>
        <div className="sticky left-0 w-[100cqi]">{children}</div>
      </TableCell>
    </TableRow>
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
  TableEmptyRow,
}
