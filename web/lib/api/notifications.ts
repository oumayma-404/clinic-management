import { apiGet, apiPut } from './client';
import type { NotificationDto, PendingReviewDto } from './types';

/**
 * In-app staff notification feed. All endpoints are scoped server-side to the caller's clinic and
 * identity. The unread count is a lightweight aggregate independent of the 50-row list window.
 */
export const notificationsApi = {
  list: async (): Promise<NotificationDto[]> => {
    return apiGet<NotificationDto[]>('/notifications');
  },

  unreadCount: async (): Promise<{ unreadCount: number }> => {
    return apiGet<{ unreadCount: number }>('/notifications/unread-count');
  },

  pendingReviews: async (): Promise<PendingReviewDto[]> => {
    return apiGet<PendingReviewDto[]>('/notifications/pending-reviews');
  },

  markRead: async (id: string): Promise<void> => {
    return apiPut<void>(`/notifications/${id}/read`, {});
  },

  markAllRead: async (): Promise<void> => {
    return apiPut<void>('/notifications/read-all', {});
  },
};
