"use client"

import { useId } from "react"
import { ChevronLeft, ChevronRight, ChevronsLeft, ChevronsRight } from "lucide-react"

import { Button } from "@/components/ui/button"
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select"
import { PAGE_SIZE_OPTIONS, type PagedResponse } from "@/lib/api/paging"

interface DataTablePaginationProps {
  /** The page currently rendered. Pass the response straight through — the counts come from the server. */
  page: Pick<PagedResponse<unknown>, "page" | "pageSize" | "totalCount" | "totalPages">
  onPageChange: (page: number) => void
  onPageSizeChange?: (pageSize: number) => void
  /** Dims and disables the controls while a fetch is in flight, so a double-click can't skip a page. */
  loading?: boolean
  /** French singular/plural noun for the count line, e.g. `["patient", "patients"]`. */
  label?: readonly [string, string]
}

/**
 * The single pager for every table in the app.
 *
 * <p><b>Server-driven, deliberately.</b> It takes the counts from the response rather than deriving them from the
 * rows it can see, because it cannot see them — that is the whole point of paging. A pager that inferred
 * « il y a plus de pages » from « j'ai reçu une page pleine » is wrong exactly when the total is an exact multiple
 * of the page size.</p>
 *
 * <p>Renders nothing when there is a single page AND no page-size selector to offer: an empty table should not
 * carry « page 1 sur 1 » underneath it. It still renders for a single page when the size selector is available,
 * since that is the control someone uses to see more at once.</p>
 */
export function DataTablePagination({
  page,
  onPageChange,
  onPageSizeChange,
  loading = false,
  label = ["résultat", "résultats"],
}: DataTablePaginationProps) {
  const pageSizeId = useId()
  const { page: current, pageSize, totalCount, totalPages } = page

  if (totalPages <= 1 && !onPageSizeChange) {
    return null
  }

  const [singular, plural] = label
  const noun = totalCount === 1 ? singular : plural

  // The range this page covers. Clamped to the total so the last page reads « 41–47 sur 47 », not « 41–50 sur 47 ».
  const firstRow = totalCount === 0 ? 0 : (current - 1) * pageSize + 1
  const lastRow = Math.min(current * pageSize, totalCount)

  const canPrevious = current > 1 && !loading
  const canNext = current < totalPages && !loading

  return (
    <div
      className="flex flex-col gap-3 border-t px-2 py-3 sm:flex-row sm:items-center sm:justify-between"
      data-loading={loading ? "true" : undefined}
    >
      {/* role="status" so a screen reader hears the new range after paging, which is otherwise a silent change. */}
      <p className="text-sm text-muted-foreground" role="status">
        {totalCount === 0
          ? `Aucun ${singular}`
          : `${firstRow}–${lastRow} sur ${totalCount} ${noun}`}
      </p>

      {/*
        `flex-wrap` here is what stops nine pages scrolling sideways.

        Neither inner group can shrink — « Par page » and « Page 1 sur 3 » are non-wrapping text and the four
        buttons are `shrink-0` — so the row's minimum is ~374 px. Inside a `CardContent`'s `px-6` on a 390 px
        phone there are only ~310 px, and because `<main>` is `overflow-y-auto` its `overflow-x` computes to
        `auto`: the whole page content area picked up a horizontal scroll and « Dernière page » sat off-screen.
        This pager renders on 16 surfaces, so it was the single most widespread source of sideways scroll.
      */}
      <div className="flex flex-wrap items-center gap-x-4 gap-y-2">
        {onPageSizeChange && (
          <div className="flex items-center gap-2">
            {/* A generated id, not the literal "page-size": a page may now render two pagers (the card list
                and the table are two trees), and duplicate ids make `htmlFor` point at whichever the browser
                happens to find first. */}
            <label className="text-sm text-muted-foreground" htmlFor={pageSizeId}>
              Par page
            </label>
            <Select
              value={String(pageSize)}
              onValueChange={(value) => onPageSizeChange(Number(value))}
              disabled={loading}
            >
              <SelectTrigger id={pageSizeId} className="h-8 w-[72px]">
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                {PAGE_SIZE_OPTIONS.map((size) => (
                  <SelectItem key={size} value={String(size)}>
                    {size}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          </div>
        )}

        {totalPages > 1 && (
          /*
           * `coarse:gap-3` is a wrong-action fix, not spacing taste.
           *
           * These four buttons are painted 32 px (`h-8 w-8`) but `buttonVariants` gives every button
           * `touch-target`, whose overlay is a minimum of 44 px — so each hit area extends 6 px past its own
           * edge on both sides, while `gap-1` leaves only 4 px between them. Neighbours therefore overlap by
           * 8 px, both overlays are `position: relative` with `z-index: auto`, and **the later sibling paints
           * last and wins the hit test**. Tapping the right edge of « Page précédente » fires « Page suivante ».
           *
           * That is a wrong-action bug on every paged list in the app, and it is the same failure `ui/select.tsx`
           * documents and avoids by growing `SelectItem` instead of overlaying it. 12 px of gap clears the two
           * 6 px overhangs exactly, so no overlay reaches its neighbour. On a mouse this emits nothing.
           */
          <div className="flex items-center gap-1 coarse:gap-3">
            <span className="mr-2 text-sm text-muted-foreground">
              Page {current} sur {totalPages}
            </span>
            <Button
              variant="outline"
              size="icon"
              className="h-8 w-8"
              onClick={() => onPageChange(1)}
              disabled={!canPrevious}
              aria-label="Première page"
            >
              <ChevronsLeft className="h-4 w-4" />
            </Button>
            <Button
              variant="outline"
              size="icon"
              className="h-8 w-8"
              onClick={() => onPageChange(current - 1)}
              disabled={!canPrevious}
              aria-label="Page précédente"
            >
              <ChevronLeft className="h-4 w-4" />
            </Button>
            <Button
              variant="outline"
              size="icon"
              className="h-8 w-8"
              onClick={() => onPageChange(current + 1)}
              disabled={!canNext}
              aria-label="Page suivante"
            >
              <ChevronRight className="h-4 w-4" />
            </Button>
            <Button
              variant="outline"
              size="icon"
              className="h-8 w-8"
              onClick={() => onPageChange(totalPages)}
              disabled={!canNext}
              aria-label="Dernière page"
            >
              <ChevronsRight className="h-4 w-4" />
            </Button>
          </div>
        )}
      </div>
    </div>
  )
}
