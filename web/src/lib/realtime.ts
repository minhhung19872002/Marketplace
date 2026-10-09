// One SignalR connection per signed-in tab (spec 1: chat, notifications live). Events: chat.message, chat.read,
// chat.typing, notification. Reconnects by itself; the access token is read fresh on every (re)connect.
// SignalR is loaded on first use: anonymous visitors never download it (mobile LCP, G3)
import type { HubConnection } from '@microsoft/signalr';
import { useEffect, useRef } from 'react';
import { refreshSession } from '../api/http';
import { useAuthStore } from '../stores/auth';

let connection: HubConnection | null = null;
let starting: Promise<void> | null = null;

const build = async () => {
  const { HubConnectionBuilder, LogLevel } = await import('@microsoft/signalr');
  return new HubConnectionBuilder()
    .withUrl('/hubs/realtime', {
      accessTokenFactory: async () => {
        // A long-lived tab may hold an expired token: refresh before (re)connecting
        if (!useAuthStore.getState().accessToken) await refreshSession();
        return useAuthStore.getState().accessToken ?? '';
      },
    })
    .withAutomaticReconnect([0, 2_000, 5_000, 10_000, 30_000])
    .configureLogging(LogLevel.None)
    .build();
};
let building: Promise<HubConnection> | null = null;

/** The shared connection, started on first use (signed-in users only). */
export const realtime = async (): Promise<HubConnection | null> => {
  if (!useAuthStore.getState().accessToken) return null;
  if (!connection) {
    building ??= build();
    const built = await building;
    building = null;
    // Signed out (stopRealtime) while the library was loading
    if (!useAuthStore.getState().accessToken) return null;
    connection ??= built;
  }
  const c = connection;
  // HubConnectionState.Disconnected, compared as its string value to keep the enum out of the entry chunk
  if (c.state === 'Disconnected') {
    starting ??= c.start().catch(() => undefined).finally(() => {
      starting = null;
    });
    await starting;
  }
  return c;
};

export const stopRealtime = async () => {
  const c = connection;
  connection = null;
  if (c) await c.stop().catch(() => undefined);
};

/** Subscribe to one event while the component is mounted; the latest handler is always called. */
export const useRealtimeEvent = <T>(event: string, handler: (data: T) => void, enabled = true) => {
  const latest = useRef(handler);
  latest.current = handler;
  useEffect(() => {
    if (!enabled) return undefined;
    let hub: HubConnection | null = null;
    let cancelled = false;
    const listener = (data: T) => latest.current(data);
    void realtime().then((c) => {
      if (cancelled || !c) return;
      hub = c;
      c.on(event, listener);
    });
    return () => {
      cancelled = true;
      hub?.off(event, listener);
    };
  }, [event, enabled]);
};
