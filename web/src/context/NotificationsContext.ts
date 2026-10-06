import { useQuery, useQueryClient } from '@tanstack/react-query';
import { notificationsApi } from '../api/commerce';
import { useRealtimeEvent } from '../lib/realtime';

export const UNREAD_KEY = ['notifications', 'unread'] as const;

/** Unread counter for the header bell: pushed live over SignalR, re-read every few minutes as a safety net. */
export const useUnreadNotifications = (enabled: boolean) => {
  const queryClient = useQueryClient();
  useRealtimeEvent('notification', () => {
    void queryClient.invalidateQueries({ queryKey: ['notifications'] });
  }, enabled);
  return useQuery({ queryKey: UNREAD_KEY, queryFn: notificationsApi.unread, enabled, refetchInterval: 300_000, staleTime: 30_000 });
};
