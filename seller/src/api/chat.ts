// Shop inbox (spec II.11): conversations, assignment, quick replies, auto-reply settings.
import { apiCommand, apiRequest } from './http'
import type { PagedResult } from './seller'

export type MessageType = 'Text' | 'Image' | 'Product' | 'Order' | 'Voucher'
export type InboxFilter = 'All' | 'Unread' | 'Mine' | 'Unassigned'

export interface ChatMessage {
  id: string
  conversationId: string
  senderRole: 'Buyer' | 'Shop' | 'System'
  senderId: string | null
  senderName: string | null
  type: MessageType
  body: string
  payload: Record<string, unknown> | null
  flagged: boolean
  createdAt: string
  readAt: string | null
}

export interface Conversation {
  id: string
  shopId: string
  buyerId: string
  buyerName: string
  lastMessagePreview: string | null
  lastMessageAt: string
  unread: number
  assignedTo: string | null
  assignedName: string | null
  blockedByBuyer: boolean
}

export interface SendInput {
  type: MessageType
  text?: string
  productId?: string
  orderCode?: string
  voucherId?: string
  imageAssetId?: string
}

export interface QuickReply {
  id: string
  shortcut: string
  content: string
}

export interface ChatSettings {
  autoReplyEnabled: boolean
  autoReplyText: string
  openFrom: string
  openTo: string
}

const base = (shopId: string) => `/seller/shops/${shopId}/chat`

export const chatApi = {
  inbox: (shopId: string, filter: InboxFilter, q: string) =>
    apiRequest<PagedResult<Conversation>>(`${base(shopId)}/conversations?filter=${filter}&q=${encodeURIComponent(q)}&pageSize=50`),
  messages: (shopId: string, id: string, before?: string) =>
    apiRequest<ChatMessage[]>(`${base(shopId)}/conversations/${id}/messages?limit=30${before ? `&before=${encodeURIComponent(before)}` : ''}`),
  send: (shopId: string, id: string, input: SendInput) =>
    apiRequest<ChatMessage>(`${base(shopId)}/conversations/${id}/messages`, { method: 'POST', body: input }),
  read: (shopId: string, id: string) => apiCommand(`${base(shopId)}/conversations/${id}/read`, { method: 'POST' }),
  assign: (shopId: string, id: string, staffUserId: string | null) =>
    apiCommand(`${base(shopId)}/conversations/${id}/assign`, { method: 'POST', body: { staffUserId } }),
  staff: (shopId: string) => apiRequest<{ userId: string; name: string }[]>(`${base(shopId)}/staff`),
  quickReplies: (shopId: string) => apiRequest<QuickReply[]>(`${base(shopId)}/quick-replies`),
  saveQuickReply: (shopId: string, input: { id?: string; shortcut: string; content: string }) =>
    apiCommand(`${base(shopId)}/quick-replies`, { method: 'POST', body: input }),
  deleteQuickReply: (shopId: string, id: string) => apiCommand(`${base(shopId)}/quick-replies/${id}`, { method: 'DELETE' }),
  settings: (shopId: string) => apiRequest<ChatSettings>(`${base(shopId)}/settings`),
  saveSettings: (shopId: string, input: ChatSettings) => apiCommand(`${base(shopId)}/settings`, { method: 'PUT', body: input }),
}
