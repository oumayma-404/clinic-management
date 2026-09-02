import { apiGet, apiPost, apiPut, apiDelete } from './client';
import type { ExpenseDto, RecurringExpenseDto, CaisseSummaryDto, CaisseLedgerDto } from './types';
import { unwrapPaged, type PagedResponse, type PageParams } from './paging';

export interface ExpensePayload {
  /**
   * The day in the CABINET's calendar, as a bare `yyyy-MM-dd`.
   *
   * ⚠️ **Never an instant.** An ISO timestamp is midnight in whatever zone the browser is in, so the same form
   * filed a dépense on two different days depending on the workstation. The server resolves a bare day against
   * Tunisia (`ExpenseDay`), which is the only zone a caisse period is ever expressed in. Required — an absent
   * value is a 400 carrying `expense_date_required`, not a row dated `-infinity`.
   */
  expenseDate: string;
  category: string;
  amount: number;
  method: string; // Cash | Cheque | Card | Transfer
  description?: string | null;
  /**
   * The version read from the server, on an update. Absent on create; omitted (or 0) the server skips the
   * concurrency check — see `PatientDto.version`.
   *
   * ⚠️ Its absence meant two tabs editing the same dépense both answered 200 with « Dépense mise à jour » while
   * one amount silently replaced the other. This is money.
   */
  version?: number;
  /**
   * « Répéter chaque mois ». Creates the monthly series alongside the dépense being recorded, in one call —
   * that row is the series' first occurrence, so its day becomes the series' day of the month and its month the
   * marker the posting pass starts after. Meaningless on an update; only `create` reads it.
   */
  repeatMonthly?: boolean;
}

/** The editable half of a monthly series. No date: a series has a day of the month, not a day. */
export interface RecurringExpensePayload {
  category: string;
  amount: number;
  method: string;
  description?: string | null;
  dayOfMonth: number;
  version?: number;
}

export const expensesApi = {
  list: async (from?: string, to?: string): Promise<ExpenseDto[]> =>
    unwrapPaged(await apiGet<PagedResponse<ExpenseDto>>('/expenses', { from, to })),

  /**
   * One page of expenses. `search` matches catégorie / description server-side over the whole window.
   *
   * @param params.fromDay Bare `YYYY-MM-DD` clinic-local days — the form la caisse sends so its dépenses table
   *   covers the same **Tunisian** window as the totals above it (AC-6). Omit every date for the whole list;
   *   unlike the caisse reads, « no window » here means « toutes les dépenses », not « aujourd'hui ».
   */
  listPaged: async (
    params: PageParams & { fromDay?: string; toDay?: string; from?: string; to?: string },
  ): Promise<PagedResponse<ExpenseDto>> =>
    apiGet<PagedResponse<ExpenseDto>>('/expenses', params),

  create: async (data: ExpensePayload): Promise<ExpenseDto> => apiPost<ExpenseDto>('/expenses', data),

  update: async (id: string, data: ExpensePayload): Promise<ExpenseDto> => apiPut<ExpenseDto>(`/expenses/${id}`, data),

  delete: async (id: string): Promise<void> => apiDelete<void>(`/expenses/${id}`),

  /**
   * « Dépenses mensuelles » — the clinic's ACTIVE series, unpaged and with no window.
   *
   * ⚠️ Deliberately not period-scoped, unlike every other read on la caisse: a standing commitment has no date,
   * so passing the screen's `fromDay`/`toDay` would answer a question nobody asked. A stopped series is absent.
   */
  listRecurring: async (): Promise<RecurringExpenseDto[]> =>
    apiGet<RecurringExpenseDto[]>('/expenses/recurring'),

  /** Future months only — the occurrences already in la caisse keep the figure they were recorded with. */
  updateRecurring: async (id: string, data: RecurringExpensePayload): Promise<RecurringExpenseDto> =>
    apiPut<RecurringExpenseDto>(`/expenses/recurring/${id}`, data),

  /**
   * « Arrêter » — the credit is paid off. NOT a delete: nothing already posted moves, so no caisse figure the
   * practice has already read changes. Idempotent server-side.
   */
  stopRecurring: async (id: string): Promise<void> =>
    apiPost<void>(`/expenses/recurring/${id}/stop`, {}),

  // Caisse (daily cash): net = cashIn − refunds − cashOut over the window (defaults to the clinic-local day
  // server-side). `cashIn` is gross; refunds are their own figure.
  /**
   * @param fromDay The window's first day as a bare `YYYY-MM-DD`, resolved server-side as a **clinic-local**
   *   calendar day. ⚠️ Never compose an instant here: `new Date(day).toISOString()` is midnight in the
   *   *workstation's* timezone, which is how « la caisse du 3 août » used to cover a window offset by hours from
   *   the Tunisian day. `toDay` defaults to it.
   */
  caisseSummary: async (fromDay?: string, toDay?: string): Promise<CaisseSummaryDto> =>
    apiGet<CaisseSummaryDto>('/billing/caisse', { fromDay, toDay }),

  // The « extrait de caisse » — every movement behind those totals, oldest first, with a running period balance.
  // Same window as `caisseSummary`, so the lines and the totals always describe the same period.
  /**
   * The « extrait de caisse ». Paging and `search` apply to the MOVEMENTS; the window (`from`/`to`) and each row's
   * `runningBalance` always describe the whole period, so « Solde de la période » keeps meaning the same thing on
   * page 3 as on page 1.
   */
  /**
   * @param params.method Optional `PaymentMethod` storage key (`Cash`/`Cheque`/`Card`/`Transfer`) — L8 slice B's
   *   « ne montre que les chèques ». ⚠️ Applied server-side **after** the running balance, for the same reason as
   *   `search`. A movement with no method at all (a legacy avoir) leaves the list under any filter.
   */
  caisseLedger: async (
    params: PageParams & { fromDay?: string; toDay?: string; method?: string } = {},
  ): Promise<CaisseLedgerDto> =>
    apiGet<CaisseLedgerDto>('/billing/caisse/ledger', params),
};
