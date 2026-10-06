import { useQuery } from '@tanstack/react-query';
import { notificationsApi } from '../api/commerce';

export const UNREAD_KEY = ['notifications', 'unread'] as const;

/** Unread counter for the header bell (refreshed every minute; real-time push arrives with SignalR in Phase 10). */
export const useUnreadNotifications = (enabled: boolean) =>
  useQuery({ queryKey: UNREAD_KEY, queryFn: notificationsApi.unread, enabled, refetchInterval: 60_000, staleTime: 30_000 });
