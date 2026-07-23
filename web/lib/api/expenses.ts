import { apiGet, apiPost, apiPut, apiDelete } from './client';
import type { ExpenseDto, CaisseSummaryDto } from './types';

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

  // Caisse (daily cash) net = encaissements − dépenses over [from, to) (defaults to today server-side).
  caisseSummary: async (from?: string, to?: string): Promise<CaisseSummaryDto> =>
    apiGet<CaisseSummaryDto>('/billing/caisse', { from, to }),
};
