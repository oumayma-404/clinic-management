/**
 * The client half of the backend's `PagedResult<T>` (`api/ClinicManagement.Domain/Common/Paging.cs`).
 *
 * Every list endpoint returns this shape now, whether or not the caller asked for a page — a request with no
 * paging parameters comes back as one page containing everything, so consumers never have to handle two shapes.
 */
export interface PagedResponse<T> {
  items: T[];
  /** 1-based. Always 1 for an unpaged read. */
  page: number;
  /** Rows per page. Equals the row count for an unpaged read. */
  pageSize: number;
  /** Rows matching the filter across **every** page — not `items.length`. */
  totalCount: number;
  /** Never below 1: an empty list is « page 1 sur 1 », not « 1 sur 0 ». */
  totalPages: number;
  hasPreviousPage: boolean;
  hasNextPage: boolean;
}

/** Query parameters every paginated list endpoint accepts. */
export interface PageParams {
  page?: number;
  pageSize?: number;
  /**
   * Free-text filter. **Applied server-side, before the page is cut**, so it searches the whole clinic.
   *
   * This is the reason searching is not a client-side `.filter()` any more: with a page of 25 rows loaded, an
   * in-memory filter can only match what is already on screen, so a patient on page 7 reads as « aucun résultat ».
   * Never re-filter a paged list in the browser — pass the term down and refetch.
   */
  search?: string;
}

/** The default page size the tables ask for. Mirrors `PageRequest.DefaultPageSize`. */
export const DEFAULT_PAGE_SIZE = 25;

/** Page-size choices offered in the pager's selector. All within `PageRequest.MaxPageSize` (200). */
export const PAGE_SIZE_OPTIONS = [10, 25, 50, 100] as const;

/**
 * Wrap a plain array as a single full page.
 *
 * For the few places that hold a complete client-side list and still want to render the shared pager (and for
 * tests). Not a substitute for asking the server for a page.
 */
export function asSinglePage<T>(items: T[]): PagedResponse<T> {
  return {
    items,
    page: 1,
    pageSize: items.length,
    totalCount: items.length,
    totalPages: 1,
    hasPreviousPage: false,
    hasNextPage: false,
  };
}

/** An empty page — the initial state for a table before its first fetch resolves. */
export function emptyPage<T>(pageSize = DEFAULT_PAGE_SIZE): PagedResponse<T> {
  return {
    items: [],
    page: 1,
    pageSize,
    totalCount: 0,
    totalPages: 1,
    hasPreviousPage: false,
    hasNextPage: false,
  };
}

/**
 * Take just the rows, discarding the page metadata.
 *
 * Used by the per-resource `list()` helpers so the ~30 existing callers that genuinely want every row (form
 * pickers, the header lookup, `<Select>` option sources) keep their `Promise<T[]>` signature. Those callers send
 * no paging parameters, so the single page they unwrap really is everything.
 */
export function unwrapPaged<T>(response: PagedResponse<T>): T[] {
  return response.items;
}
