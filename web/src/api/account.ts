import { apiCommand, apiRequest } from './http';
import type { OtpIssued } from './auth';

export type Gender = 'Male' | 'Female' | 'Other';
export type AddressType = 'Home' | 'Office';

export interface Me {
  id: string;
  fullName: string;
  phone: string | null;
  email: string | null;
  username: string | null;
  avatarUrl: string | null;
  gender: Gender | null;
  dateOfBirth: string | null;
  mustChangePassword: boolean;
  roles: string[];
  permissions: string[];
  createdAt: string;
}

export interface Session {
  id: string;
  device: string | null;
  ip: string | null;
  signedInAt: string;
  lastActiveAt: string;
  isCurrent: boolean;
}

export interface Address {
  id: string;
  receiverName: string;
  phone: string;
  provinceCode: string;
  provinceName: string;
  districtCode: string;
  districtName: string;
  wardCode: string;
  wardName: string;
  street: string;
  type: AddressType;
  isDefault: boolean;
  lat: number | null;
  lng: number | null;
}

export interface AddressInput {
  receiverName: string;
  phone: string;
  provinceCode: string;
  districtCode: string;
  wardCode: string;
  street: string;
  type: AddressType;
  isDefault: boolean;
  // Ghim bản đồ (optional, E7)
  lat: number | null;
  lng: number | null;
}

export interface AdminDivision {
  code: string;
  name: string;
  level: number;
  parentCode: string | null;
}

/** "Tải dữ liệu của tôi" — kept loose: it is saved as a file, the page only reads exportedAt */
export interface MyDataExport {
  exportedAt: string;
  [section: string]: unknown;
}

export const accountApi = {
  me: () => apiRequest<Me>('/account/me'),

  setAvatar: (assetId: string) => apiCommand<string>('/account/avatar', { method: 'PUT', body: { assetId } }),

  exportData: () => apiRequest<MyDataExport>('/account/export'),

  deleteAccount: (password: string) => apiCommand('/account/delete', { method: 'POST', body: { password } }),

  updateProfile: (body: { fullName: string; gender: Gender | null; dateOfBirth: string | null }) =>
    apiCommand('/account/profile', { method: 'PUT', body }),

  changePassword: (currentPassword: string, newPassword: string) =>
    apiCommand('/account/password', { method: 'PUT', body: { currentPassword, newPassword } }),

  sessions: () => apiRequest<Session[]>('/account/sessions'),

  revokeSession: (id: string) => apiCommand(`/account/sessions/${id}`, { method: 'DELETE' }),

  requestContactChange: (newValue: string) =>
    apiRequest<OtpIssued>('/account/contact/otp', { method: 'POST', body: { newValue } }),

  confirmContactChange: (newValue: string, code: string) =>
    apiCommand('/account/contact', { method: 'PUT', body: { newValue, code } }),

  addresses: () => apiRequest<Address[]>('/account/addresses'),

  createAddress: (body: AddressInput) => apiCommand<Address>('/account/addresses', { method: 'POST', body }),

  updateAddress: (id: string, body: AddressInput) => apiCommand<Address>(`/account/addresses/${id}`, { method: 'PUT', body }),

  setDefaultAddress: (id: string) => apiCommand(`/account/addresses/${id}/default`, { method: 'POST' }),

  deleteAddress: (id: string) => apiCommand(`/account/addresses/${id}`, { method: 'DELETE' }),

  divisions: (parent?: string) =>
    apiRequest<AdminDivision[]>(parent ? `/admin-divisions?parent=${encodeURIComponent(parent)}` : '/admin-divisions', { auth: false }),
};
