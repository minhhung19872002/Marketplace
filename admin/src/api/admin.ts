import { apiCommand, apiRequest } from './http'
import type { AuthResult } from '../stores/auth'

export interface Me {
  id: string
  fullName: string
  username: string | null
  mustChangePassword: boolean
  roles: string[]
  permissions: string[]
}

export interface PagedResult<T> {
  items: T[]
  totalCount: number
  page: number
  pageSize: number
}

export type UserStatus = 'Active' | 'Locked' | 'Deleted'

export interface AdminUser {
  id: string
  fullName: string
  phone: string | null
  email: string | null
  username: string | null
  status: UserStatus
  lockReason: string | null
  roles: string[]
  createdAt: string
  lastLoginAt: string | null
}

export interface Permission {
  code: string
  module: string
  name: string
}

export interface Role {
  id: string
  code: string
  name: string
  description: string
  isSystem: boolean
  permissions: string[]
  userCount: number
}

export interface SystemParameter {
  key: string
  value: string
  dataType: string
  group: string
  name: string
  description: string
  version: number
  updatedAt: string | null
}

export interface AuditLog {
  id: string
  userId: string | null
  userName: string | null
  ip: string | null
  action: 'CREATE' | 'UPDATE' | 'DELETE'
  entity: string
  entityId: string | null
  oldValue: string | null
  newValue: string | null
  occurredAt: string
}

const query = (params: Record<string, string | number | boolean | undefined | null>) => {
  const qs = new URLSearchParams()
  for (const [k, v] of Object.entries(params)) if (v !== undefined && v !== null && v !== '') qs.set(k, String(v))
  const s = qs.toString()
  return s ? `?${s}` : ''
}

export const adminApi = {
  login: (identifier: string, password: string) =>
    apiRequest<AuthResult>('/auth/login', { method: 'POST', body: { identifier, password }, auth: false }),
  logout: () => apiCommand('/auth/logout', { method: 'POST', body: {}, auth: false }),
  me: () => apiRequest<Me>('/account/me'),
  changePassword: (currentPassword: string, newPassword: string) =>
    apiCommand('/account/password', { method: 'PUT', body: { currentPassword, newPassword } }),

  users: (p: { page: number; pageSize: number; q?: string; status?: UserStatus; adminsOnly?: boolean }) =>
    apiRequest<PagedResult<AdminUser>>(`/admin/users${query(p)}`),
  lockUser: (id: string, reason: string) => apiCommand(`/admin/users/${id}/lock`, { method: 'POST', body: { reason } }),
  unlockUser: (id: string) => apiCommand(`/admin/users/${id}/unlock`, { method: 'POST' }),
  setUserRoles: (id: string, roleIds: string[]) => apiCommand(`/admin/users/${id}/roles`, { method: 'PUT', body: { roleIds } }),

  permissions: () => apiRequest<Permission[]>('/admin/permissions'),
  roles: () => apiRequest<Role[]>('/admin/roles'),
  createRole: (body: { code: string; name: string; description: string; permissions: string[] }) =>
    apiCommand<string>('/admin/roles', { method: 'POST', body }),
  updateRole: (id: string, body: { code: string; name: string; description: string; permissions: string[] }) =>
    apiCommand<string>(`/admin/roles/${id}`, { method: 'PUT', body }),
  deleteRole: (id: string) => apiCommand(`/admin/roles/${id}`, { method: 'DELETE' }),

  parameters: (group?: string) => apiRequest<SystemParameter[]>(`/admin/system-parameters${query({ group })}`),
  updateParameter: (key: string, value: string, version: number) =>
    apiCommand<SystemParameter>(`/admin/system-parameters/${encodeURIComponent(key)}`, { method: 'PUT', body: { value, version } }),

  auditLogs: (p: { page: number; pageSize: number; action?: string; entity?: string; from?: string; to?: string }) =>
    apiRequest<PagedResult<AuditLog>>(`/admin/audit-logs${query(p)}`),
}
