// The only module that talks HTTP. Every response follows { success, data, message, errors }.
import { useAuthStore, type AuthResult } from '../stores/auth';

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

  constructor(status: number, message: string, fieldErrors: FieldError[] = []) {
    super(message);
    this.status = status;
    this.fieldErrors = fieldErrors;
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
}

async function send<T>(path: string, { method = 'GET', body, auth = true }: RequestOptions, retried: boolean): Promise<Envelope<T>> {
  const headers: Record<string, string> = { Accept: 'application/json' };
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
    throw new ApiError(0, 'Không kết nối được máy chủ. Vui lòng kiểm tra mạng và thử lại.');
  }

  if (response.status === 401 && auth && token && !retried) {
    if (await refreshSession()) return send<T>(path, { method, body, auth }, true);
  }

  const envelope = (await response.json().catch(() => null)) as Envelope<T> | null;
  if (!response.ok || !envelope?.success) {
    throw new ApiError(response.status, envelope?.message || 'Yêu cầu không thành công.', envelope?.errors ?? []);
  }
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
      const result = await apiRequest<AuthResult>('/auth/refresh', { method: 'POST', body: {}, auth: false });
      useAuthStore.getState().setSession(result);
      return true;
    } catch {
      useAuthStore.getState().clear();
      return false;
    } finally {
      refreshing = null;
    }
  })();
  return refreshing;
}
