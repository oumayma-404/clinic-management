import { apiDelete, apiGet, apiPut } from './client';
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

  /**
   * Removes one notification from the caller's own bell.
   *
   * ⚠️ It clears it for **this user only** — the server writes a per-user dismissal rather than deleting the row,
   * because a notification with no named target is one row shown to every colleague. So this is not an undo of
   * whatever produced it, and a colleague's bell is untouched.
   */
  dismiss: async (id: string): Promise<void> => {
    return apiDelete<void>(`/notifications/${id}`);
  },

  /** Empties the caller's own bell — everything currently visible to them, not merely the unread rows. */
  dismissAll: async (): Promise<void> => {
    return apiDelete<void>('/notifications');
  },
};
