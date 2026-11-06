import { ApiError, apiGet, apiPost } from './client';

/** One line of a news, as the console wrote it. */
export interface NewsItemDto {
  emoji: string;
  text: string;
}

export interface NewsEntryDto {
  id: string;
  subject: string;
  /** UTC instant of the send. */
  sentAt: string;
  /** The Tunisian day it was sent (`yyyy-MM-dd`), what the page prints — never re-derived from `sentAt`. */
  sentOn: string;
  isUnread: boolean;
  items: NewsItemDto[];
}

export interface NewsFeedDto {
  unread: number;
  entries: NewsEntryDto[];
}

export interface NewsSummaryDto {
  unread: number;
}

/** `null` when this installation has no news at all (the server's 404 — a cabinet's own PC). */
async function readOrAbsent<T>(endpoint: string): Promise<T | null> {
  try {
    return await apiGet<T>(endpoint);
  } catch (err) {
    if (err instanceof ApiError && err.status === 404) return null;
    throw err;
  }
}

export const newsApi = {
  feed: () => readOrAbsent<NewsFeedDto>('/news'),
  summary: () => readOrAbsent<NewsSummaryDto>('/news/summary'),
  markRead: () => apiPost<void>('/news/read', {}),
};
