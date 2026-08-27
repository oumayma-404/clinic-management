"use client"

import { useCallback, useEffect, useRef, useState } from "react"

import { DEFAULT_PAGE_SIZE, emptyPage, type PagedResponse } from "@/lib/api/paging"

/** How long to wait after the last keystroke before asking the server. */
const SEARCH_DEBOUNCE_MS = 300

interface UsePagedListOptions<T> {
  /**
   * Fetches one page. Must be stable across renders (wrap it in `useCallback` with the filters it closes over) —
   * the hook refetches whenever it changes, so an inline arrow would loop forever.
   */
  fetchPage: (params: { page: number; pageSize: number; search?: string }) => Promise<PagedResponse<T>>
  /** The free-text term as typed. Debounced here; do not debounce it again upstream. */
  search?: string
  /**
   * Every **other** filter narrowing this list — a statut, a category, a date bound, a praticien (AC-22).
   * Changing any of them returns to page 1, for the same reason a new search does: page 4 of a 2-page result is
   * an empty table over data that matched.
   *
   * ⚠️ **Values must be JSON-serialisable primitives** (string / number / boolean / null / undefined, or arrays of
   * those). The reset keys on `JSON.stringify`, **never** on the array's identity: callers pass an inline literal,
   * which is a new reference on every render — an identity-keyed effect would fire constantly and `setPage(1)`
   * would undo the user's own page click, breaking paging on every list instead of fixing their filters.
   */
  filters?: readonly unknown[]
  pageSize?: number
  /** Bump to force a refetch of the current page (after a create/delete, or on a realtime signal). */
  refreshKey?: unknown
  onError?: (error: unknown) => void
}

/**
 * Page state + fetching for a server-paginated table.
 *
 * Three behaviours here are the ones that make paging feel right, and each fixes a specific way it feels wrong:
 *
 * 1. **A new search resets to page 1.** Keeping the page number across a search change lands the user on page 4 of
 *    a 2-page result — an empty table over data that matched.
 * 2. **A page past the end walks back.** Deleting the last row of the last page, or a peer's delete arriving over
 *    realtime, leaves the current page empty while rows still exist. The effect notices `page > totalPages` and
 *    steps back rather than showing nothing.
 * 3. **Refetching keeps the old rows on screen** (`refreshing`, distinct from `loading`). Blanking the table on
 *    every keystroke makes a debounced search strobe; the same split `useDashboard` already uses.
 */
export function usePagedList<T>({
  fetchPage,
  search = "",
  filters,
  pageSize: initialPageSize = DEFAULT_PAGE_SIZE,
  refreshKey,
  onError,
}: UsePagedListOptions<T>) {
  const [page, setPage] = useState(1)
  const [pageSize, setPageSize] = useState(initialPageSize)
  const [data, setData] = useState<PagedResponse<T>>(() => emptyPage<T>(initialPageSize))
  const [loading, setLoading] = useState(true)
  const [refreshing, setRefreshing] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const [debouncedSearch, setDebouncedSearch] = useState(search)

  // `loading` (first paint, show a skeleton) vs `refreshing` (we already have rows, keep them) — tracked in a ref
  // so deciding which one to set does not itself depend on state that has already been read this render.
  const hasLoadedRef = useRef(false)
  // Guards against a slow earlier request resolving after a faster later one and overwriting the newer page.
  const requestIdRef = useRef(0)

  useEffect(() => {
    const trimmed = search.trim()
    if (trimmed === debouncedSearch) return

    const timer = setTimeout(() => setDebouncedSearch(trimmed), SEARCH_DEBOUNCE_MS)
    return () => clearTimeout(timer)
  }, [search, debouncedSearch])

  // Reset to the first page whenever the term changes. Deliberately keyed on the DEBOUNCED value: resetting per
  // keystroke would fight the user if they paged before the debounce settled.
  useEffect(() => {
    setPage(1)
  }, [debouncedSearch])

  // Same reset for every other filter (AC-22). `filters` is stringified on purpose — see the option's doc comment:
  // an inline array literal is a new reference each render, so keying on identity would call setPage(1) forever.
  // Only the search term is debounced; a statut or a date is chosen in one gesture and should act immediately.
  const filterSignature = JSON.stringify(filters ?? [])
  const hasAppliedFiltersRef = useRef(false)
  useEffect(() => {
    // Skip the first run: the list already opens on page 1, and resetting here would fight a caller that
    // legitimately restores a page from the URL on mount.
    if (!hasAppliedFiltersRef.current) {
      hasAppliedFiltersRef.current = true
      return
    }
    setPage(1)
  }, [filterSignature])

  useEffect(() => {
    const requestId = ++requestIdRef.current
    if (hasLoadedRef.current) setRefreshing(true)
    else setLoading(true)

    let cancelled = false

    fetchPage({ page, pageSize, search: debouncedSearch || undefined })
      .then((result) => {
        if (cancelled || requestId !== requestIdRef.current) return
        setData(result)
        setError(null)
        hasLoadedRef.current = true
      })
      .catch((err) => {
        if (cancelled || requestId !== requestIdRef.current) return
        setError(err instanceof Error ? err.message : "Erreur de chargement")
        onError?.(err)
      })
      .finally(() => {
        if (cancelled || requestId !== requestIdRef.current) return
        setLoading(false)
        setRefreshing(false)
      })

    return () => {
      cancelled = true
    }
    // `onError` is intentionally excluded: callers pass an inline `showErrorToast` wrapper, and depending on it
    // would refetch on every render.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [fetchPage, page, pageSize, debouncedSearch, refreshKey])

  // Behaviour 2. Only steps BACK, never forward, and only once the fetch has settled — otherwise it would fight
  // the in-flight request it is reacting to.
  useEffect(() => {
    if (!loading && !refreshing && page > data.totalPages) {
      setPage(data.totalPages)
    }
  }, [loading, refreshing, page, data.totalPages])

  const changePageSize = useCallback((next: number) => {
    setPageSize(next)
    // Row 60 of page 3 at 25/page is not row 60 of page 3 at 100/page, so there is no honest way to preserve the
    // position. Going back to page 1 is at least predictable.
    setPage(1)
  }, [])

  return {
    /** The rows to render — this page only. */
    items: data.items,
    /** Pass straight to `<DataTablePagination page={...} />`. */
    page: data,
    /** True only before the first successful load; render a skeleton. */
    loading,
    /** True while re-fetching with rows already on screen; dim them, do not blank them. */
    refreshing,
    error,
    setPage,
    setPageSize: changePageSize,
    /** Whether a search term is currently narrowing the list — for the « aucun résultat » wording. */
    isSearching: debouncedSearch.length > 0,
  }
}
