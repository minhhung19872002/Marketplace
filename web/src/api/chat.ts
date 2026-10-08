// Chat with sellers (spec II.11): conversations, messages, read receipts, block / report.
import { apiCommand, apiRequest } from './http';
import type { PagedResult } from '../types';

export type MessageType = 'Text' | 'Image' | 'Product' | 'Order' | 'Voucher';

export interface ChatMessage {
  id: string;
  conversationId: string;
  senderRole: 'Buyer' | 'Shop' | 'System';
  senderId: string | null;
  senderName: string | null;
  type: MessageType;
  body: string;
  payload: Record<string, unknown> | null;
  flagged: boolean;
  createdAt: string;
  readAt: string | null;
}

export interface Conversation {
  id: string;
  shopId: string;
  shopName: string;
  shopLogoUrl: string | null;
  shopSlug: string;
  buyerId: string;
  buyerName: string;
  lastMessagePreview: string | null;
  lastMessageAt: string;
  unread: number;
  blockedByBuyer: boolean;
}

export interface SendInput {
  type: MessageType;
  text?: string;
  productId?: string;
  orderCode?: string;
  voucherId?: string;
  imageAssetId?: string;
}

export const chatApi = {
  start: (shopId: string) => apiRequest<Conversation>('/chat/conversations', { method: 'POST', body: { shopId } }),
  list: (q: string) => apiRequest<PagedResult<Conversation>>(`/chat/conversations?q=${encodeURIComponent(q)}&pageSize=50`),
  unread: () => apiRequest<number>('/chat/unread'),
  messages: (id: string, before?: string) =>
    apiRequest<ChatMessage[]>(`/chat/conversations/${id}/messages?limit=30${before ? `&before=${encodeURIComponent(before)}` : ''}`),
  send: (id: string, input: SendInput) => apiRequest<ChatMessage>(`/chat/conversations/${id}/messages`, { method: 'POST', body: input }),
  read: (id: string) => apiCommand(`/chat/conversations/${id}/read`, { method: 'POST' }),
  block: (id: string, blocked: boolean) => apiCommand(`/chat/conversations/${id}/block`, { method: 'POST', body: { blocked } }),
  report: (id: string, reason: string) => apiCommand(`/chat/conversations/${id}/report`, { method: 'POST', body: { reason } }),
  stats: (shopId: string) =>
    apiRequest<{ responseRatePercent: number; responseTime: string; lastActiveAt: string | null; conversations: number }>(`/shops/${shopId}/chat-stats`, { auth: false }),
};

export type NotificationCategory = 'Order' | 'Promotion' | 'Wallet' | 'Activity';
export type NotificationChannel = 'InApp' | 'Email' | 'Sms' | 'Push';

export interface NotificationPref {
  category: NotificationCategory;
  channel: NotificationChannel;
  enabled: boolean;
  locked: boolean;
}

export const notificationPrefsApi = {
  get: () => apiRequest<{ prefs: NotificationPref[]; hasEmail: boolean; hasPhone: boolean }>('/notifications/prefs'),
  save: (prefs: Pick<NotificationPref, 'category' | 'channel' | 'enabled'>[]) =>
    apiCommand('/notifications/prefs', { method: 'PUT', body: { prefs } }),
};
