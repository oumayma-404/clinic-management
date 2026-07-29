import { apiGet, apiPost, apiPut, apiDelete } from './client';
import type { ExpenseDto, CaisseSummaryDto, CaisseLedgerDto } from './types';

export interface ExpensePayload {
  expenseDate: string;
  category: string;
  amount: number;
  method: string; // Cash | Cheque | Card | Transfer
  description?: string | null;
}

export const expensesApi = {
  list: async (from?: string, to?: string): Promise<ExpenseDto[]> =>
    apiGet<ExpenseDto[]>('/expenses', { from, to }),

  create: async (data: ExpensePayload): Promise<ExpenseDto> => apiPost<ExpenseDto>('/expenses', data),

  update: async (id: string, data: ExpensePayload): Promise<ExpenseDto> => apiPut<ExpenseDto>(`/expenses/${id}`, data),

  delete: async (id: string): Promise<void> => apiDelete<void>(`/expenses/${id}`),

  // Caisse (daily cash): net = cashIn − refunds − cashOut over the window (defaults to the clinic-local day
  // server-side). `cashIn` is gross; refunds are their own figure.
  caisseSummary: async (from?: string, to?: string): Promise<CaisseSummaryDto> =>
    apiGet<CaisseSummaryDto>('/billing/caisse', { from, to }),

  // The « extrait de caisse » — every movement behind those totals, oldest first, with a running period balance.
  // Same window as `caisseSummary`, so the lines and the totals always describe the same period.
  caisseLedger: async (from?: string, to?: string): Promise<CaisseLedgerDto> =>
    apiGet<CaisseLedgerDto>('/billing/caisse/ledger', { from, to }),
};
