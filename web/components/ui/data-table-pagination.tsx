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

      <div className="flex items-center gap-4">
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
          <div className="flex items-center gap-1">
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
