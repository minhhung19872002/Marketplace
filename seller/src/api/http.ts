// The only module that talks HTTP. Every response follows { success, data, message, errors }.
import { useAuthStore, type AuthResult } from '../stores/auth'

const BASE = '/api'

export interface FieldError {
  field: string
  message: string
}

interface Envelope<T> {
  success: boolean
  data: T
  message: string
  errors: FieldError[]
}

export class ApiError extends Error {
  readonly status: number
  readonly fieldErrors: FieldError[]

  constructor(status: number, message: string, fieldErrors: FieldError[] = []) {
    super(message)
    this.status = status
    this.fieldErrors = fieldErrors
  }
}

type Method = 'GET' | 'POST' | 'PUT' | 'DELETE'

interface RequestOptions {
  method?: Method
  body?: unknown
  auth?: boolean
}

async function send<T>(path: string, { method = 'GET', body, auth = true }: RequestOptions, retried: boolean): Promise<Envelope<T>> {
  const headers: Record<string, string> = { Accept: 'application/json' }
  if (body !== undefined) headers['Content-Type'] = 'application/json'
  const token = useAuthStore.getState().accessToken
  if (auth && token) headers.Authorization = `Bearer ${token}`

  let response: Response
  try {
    response = await fetch(`${BASE}${path}`, {
      method,
      headers,
      body: body === undefined ? undefined : JSON.stringify(body),
      credentials: 'include',
    })
  } catch {
    throw new ApiError(0, 'Không kết nối được máy chủ.')
  }

  if (response.status === 401 && auth && token && !retried) {
    if (await refreshSession()) return send<T>(path, { method, body, auth }, true)
  }

  const envelope = (await response.json().catch(() => null)) as Envelope<T> | null
  if (!response.ok || !envelope?.success) {
    throw new ApiError(response.status, envelope?.message || 'Yêu cầu không thành công.', envelope?.errors ?? [])
  }
  return envelope
}

export async function apiRequest<T>(path: string, options: RequestOptions = {}): Promise<T> {
  return (await send<T>(path, options, false)).data
}

export async function apiCommand<T = null>(path: string, options: RequestOptions = {}): Promise<{ data: T; message: string }> {
  const envelope = await send<T>(path, options, false)
  return { data: envelope.data, message: envelope.message }
}

let refreshing: Promise<boolean> | null = null

// One shared rotation at a time — parallel rotations of one token look like theft to the server
export function refreshSession(): Promise<boolean> {
  refreshing ??= (async () => {
    try {
      useAuthStore.getState().setSession(await apiRequest<AuthResult>('/auth/refresh', { method: 'POST', body: {}, auth: false }))
      return true
    } catch {
      useAuthStore.getState().clear()
      return false
    } finally {
      refreshing = null
    }
  })()
  return refreshing
}

/** Liveness of the API (served at /health, outside /api). */
export async function fetchHealth(): Promise<'Healthy' | 'Unreachable' | 'Unhealthy'> {
  try {
    const res = await fetch('/health')
    return res.ok ? 'Healthy' : 'Unhealthy'
  } catch {
    return 'Unreachable'
  }
}
