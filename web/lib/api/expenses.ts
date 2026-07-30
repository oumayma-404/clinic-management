import { apiGet, apiPost, apiPut, apiDelete } from './client';
import type { ExpenseDto, CaisseSummaryDto, CaisseLedgerDto } from './types';
import { unwrapPaged, type PagedResponse, type PageParams } from './paging';

export interface ExpensePayload {
  expenseDate: string;
  category: string;
  amount: number;
  method: string; // Cash | Cheque | Card | Transfer
  description?: string | null;
}

export const expensesApi = {
  list: async (from?: string, to?: string): Promise<ExpenseDto[]> =>
    unwrapPaged(await apiGet<PagedResponse<ExpenseDto>>('/expenses', { from, to })),

  /** One page of expenses. `search` matches catégorie / description server-side over the whole window. */
  listPaged: async (
    params: PageParams & { from?: string; to?: string },
  ): Promise<PagedResponse<ExpenseDto>> =>
    apiGet<PagedResponse<ExpenseDto>>('/expenses', params),

  create: async (data: ExpensePayload): Promise<ExpenseDto> => apiPost<ExpenseDto>('/expenses', data),

  update: async (id: string, data: ExpensePayload): Promise<ExpenseDto> => apiPut<ExpenseDto>(`/expenses/${id}`, data),

  delete: async (id: string): Promise<void> => apiDelete<void>(`/expenses/${id}`),

  // Caisse (daily cash): net = cashIn − refunds − cashOut over the window (defaults to the clinic-local day
  // server-side). `cashIn` is gross; refunds are their own figure.
  caisseSummary: async (from?: string, to?: string): Promise<CaisseSummaryDto> =>
    apiGet<CaisseSummaryDto>('/billing/caisse', { from, to }),

  // The « extrait de caisse » — every movement behind those totals, oldest first, with a running period balance.
  // Same window as `caisseSummary`, so the lines and the totals always describe the same period.
  /**
   * The « extrait de caisse ». Paging and `search` apply to the MOVEMENTS; the window (`from`/`to`) and each row's
   * `runningBalance` always describe the whole period, so « Solde de la période » keeps meaning the same thing on
   * page 3 as on page 1.
   */
  caisseLedger: async (
    params: PageParams & { from?: string; to?: string } = {},
  ): Promise<CaisseLedgerDto> =>
    apiGet<CaisseLedgerDto>('/billing/caisse/ledger', params),
};
