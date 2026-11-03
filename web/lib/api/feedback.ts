import { ApiError, apiGet, apiPost } from './client';
import type { FeedbackArea, FeedbackKind, FeedbackSource } from '../feedback';

export interface FeedbackPromptDto {
  show: boolean;
}

export interface SubmitFeedbackBody {
  kind?: FeedbackKind;
  source: FeedbackSource;
  helpRating?: number | null;
  easeRating?: number | null;
  likedAreas?: FeedbackArea[];
  hardAreas?: FeedbackArea[];
  message?: string | null;
  fromScreen?: string | null;
  mayContact?: boolean;
}

/** The two refusals `SubmitFeedbackCommand` names — branch on these, never on the sentence. */
export const FeedbackRefusalCode = {
  Empty: 'feedback_empty',
  Invalid: 'feedback_invalid',
} as const;

/**
 * `null` means « no feedback on this deployment »: the server answers 404 where there is no vendor console to read
 * it (a clinic's own PC), and that absence is what hides the card and the « Aide » row. Any other failure throws.
 */
async function readPrompt(): Promise<FeedbackPromptDto | null> {
  try {
    return await apiGet<FeedbackPromptDto>('/feedback/prompt');
  } catch (err) {
    if (err instanceof ApiError && err.status === 404) return null;
    throw err;
  }
}

/**
 * One read per page load, shared by the header's « Aide » row and the week-one card — both ask the same question
 * (« is feedback served here? »), and a second request per screen for a fact that changes once a week is waste.
 */
let promptRead: Promise<FeedbackPromptDto | null> | null = null;

export const feedbackApi = {
  prompt(): Promise<FeedbackPromptDto | null> {
    if (!promptRead) {
      promptRead = readPrompt().catch((err) => {
        promptRead = null; // a failed read may be retried by the next caller
        throw err;
      });
    }
    return promptRead;
  },

  /**
   * The question is settled for this page load — answered, postponed or refused. Synchronous, so a screen change
   * that remounts the header a moment later cannot read the old « show » and put the card back up.
   */
  settle(): void {
    promptRead = Promise.resolve({ show: false });
  },

  /** The card appeared (counted once per showing, server-side), « Plus tard », or « Ne plus demander ». */
  async setPrompt(action: 'shown' | 'later' | 'never'): Promise<void> {
    if (action !== 'shown') feedbackApi.settle();
    await apiPost<void>('/feedback/prompt', { action });
  },

  async submit(body: SubmitFeedbackBody): Promise<{ id: string }> {
    if (body.source === 'semaine-1') feedbackApi.settle();
    return apiPost<{ id: string }>('/feedback', body);
  },
};
