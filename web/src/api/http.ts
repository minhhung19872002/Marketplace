// The only module that talks HTTP. Every response follows { success, data, message, errors }.
import { mayHaveSession, useAuthStore, type AuthResult } from '../stores/auth';
import { toast, toastFor } from '../lib/toast';

const BASE = '/api';

export interface FieldError {
  field: string;
  message: string;
}

interface Envelope<T> {
  success: boolean;
  data: T;
  message: string;
  errors: FieldError[];
}

export class ApiError extends Error {
  readonly status: number;
  readonly fieldErrors: FieldError[];
  // Extra payload on some errors (e.g. 409 PRICE_CHANGED carries the re-priced checkout)
  readonly data: unknown;

  constructor(status: number, message: string, fieldErrors: FieldError[] = [], data: unknown = null) {
    super(message);
    this.status = status;
    this.fieldErrors = fieldErrors;
    this.data = data;
  }

  /** Message for one form field, if the server flagged it. */
  field(name: string): string | undefined {
    return this.fieldErrors.find((e) => e.field === name)?.message;
  }
}

type Method = 'GET' | 'POST' | 'PUT' | 'DELETE';

interface RequestOptions {
  method?: Method;
  body?: unknown;
  // Send the access token and retry once after a silent refresh on 401
  auth?: boolean;
  headers?: Record<string, string>;
  // false = this write reports in its own way (no toast)
  toast?: boolean;
}

/** Writes report how they went in the shared toast (F4); reads and silent writes never do. */
function report(method: Method, path: string, ok: boolean, message: string, options: RequestOptions) {
  if (options.toast === false) return;
  const shown = toastFor(method, path, ok, message);
  if (shown) toast[shown.kind](shown.text);
}

async function send<T>(path: string, options: RequestOptions, retried: boolean): Promise<Envelope<T>> {
  const { method = 'GET', body, auth = true } = options;
  const headers: Record<string, string> = { Accept: 'application/json', ...options.headers };
  if (body !== undefined) headers['Content-Type'] = 'application/json';
  const token = useAuthStore.getState().accessToken;
  if (auth && token) headers.Authorization = `Bearer ${token}`;

  let response: Response;
  try {
    response = await fetch(`${BASE}${path}`, {
      method,
      headers,
      body: body === undefined ? undefined : JSON.stringify(body),
      // The refresh token travels as an httpOnly cookie scoped to /api/auth
      credentials: 'include',
    });
  } catch {
    const message = 'Không kết nối được máy chủ. Vui lòng kiểm tra mạng và thử lại.';
    report(method, path, false, message, options);
    throw new ApiError(0, message);
  }

  if (response.status === 401 && auth && token && !retried) {
    if (await refreshSession()) return send<T>(path, options, true);
  }

  const envelope = (await response.json().catch(() => null)) as Envelope<T> | null;
  if (!response.ok || !envelope?.success) {
    const message = envelope?.message || 'Yêu cầu không thành công.';
    report(method, path, false, message, options);
    throw new ApiError(response.status, message, envelope?.errors ?? [], envelope?.data ?? null);
  }
  report(method, path, true, envelope.message ?? '', options);
  return envelope;
}

export async function apiRequest<T>(path: string, options: RequestOptions = {}): Promise<T> {
  return (await send<T>(path, options, false)).data;
}

/** Same as apiRequest but also returns the server's message (e.g. "Đã lưu hồ sơ."). */
export async function apiCommand<T = null>(path: string, options: RequestOptions = {}): Promise<{ data: T; message: string }> {
  const envelope = await send<T>(path, options, false);
  return { data: envelope.data, message: envelope.message };
}

let refreshing: Promise<boolean> | null = null;

/**
 * Rotate the refresh cookie for a new access token. Concurrent callers share one request — two parallel
 * rotations of the same token would look like token theft to the server and end the session.
 */
export function refreshSession(): Promise<boolean> {
  refreshing ??= (async () => {
    try {
      // Never signed in here (or the session has expired): nothing to rotate, no request
      if (!mayHaveSession()) throw new Error('no session');
      const result = await apiRequest<AuthResult>('/auth/refresh', { method: 'POST', body: {}, auth: false });
      useAuthStore.getState().setSession(result);
      return true;
    } catch (e) {
      // Only a refusal of the token forgets the hint; a network / server failure or 429 (rate limit) keeps it, the
      // cookie may still be good next time (L170)
      if (e instanceof ApiError && [400, 401, 403].includes(e.status)) useAuthStore.getState().clear();
      else useAuthStore.setState({ accessToken: null, user: null, status: 'anonymous' });
      return false;
    } finally {
      refreshing = null;
    }
  })();
  return refreshing;
}
