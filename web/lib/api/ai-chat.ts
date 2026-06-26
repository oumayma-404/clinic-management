import { apiPost } from './client';

export interface ChatMessage {
  role: 'user' | 'assistant';
  content: string;
}

export interface ChatRequest {
  messages: ChatMessage[];
  context?: {
    patientId?: string;
    appointmentId?: string;
    doctorId?: string;
  };
}

export interface ChatResponse {
  message: string;
  usage?: {
    promptTokens?: number;
    completionTokens?: number;
    totalTokens?: number;
  };
}

export const aiChatApi = {
  chat: async (request: ChatRequest): Promise<ChatResponse> => {
    return apiPost<ChatResponse>('/ai/chat', request);
  },
};

