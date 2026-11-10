import { ApiError, apiGet, apiPost } from './client';

/** One line of a news, as the console wrote it, with its optional guide (« Comment ça marche »). */
export interface NewsItemDto {
  emoji: string;
  text: string;
  /** One paragraph under the line, or null. Absent from an older server. */
  description?: string | null;
  /** The guide's numbered steps; empty — or absent from an older server — when it has none. */
  steps?: string[];
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
  /** `upTo` is the `sentAt` of the newest news the page showed: one sent since is not marked read unseen. */
  markRead: (upTo?: string) => apiPost<void>('/news/read', upTo ? { upTo } : {}),
};
