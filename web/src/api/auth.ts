import { apiCommand, apiRequest } from './http';
import type { AuthResult } from '../stores/auth';

export type OtpPurpose = 'Register' | 'Login' | 'ResetPassword';

export interface OtpIssued {
  expiresInSeconds: number;
  resendAfterSeconds: number;
}

export const authApi = {
  sendOtp: (target: string, purpose: OtpPurpose) =>
    apiRequest<OtpIssued>('/auth/otp/send', { method: 'POST', body: { target, purpose }, auth: false }),

  verifyOtp: (target: string, purpose: Exclude<OtpPurpose, 'Login'>, code: string) =>
    apiRequest<{ ticket: string }>('/auth/otp/verify', { method: 'POST', body: { target, purpose, code }, auth: false }),

  register: (body: { target: string; ticket: string; password: string; fullName: string; acceptTerms: boolean }) =>
    apiRequest<AuthResult>('/auth/register', { method: 'POST', body, auth: false }),

  login: (identifier: string, password: string) =>
    apiRequest<AuthResult>('/auth/login', { method: 'POST', body: { identifier, password }, auth: false }),

  loginWithOtp: (phone: string, code: string) =>
    apiRequest<AuthResult>('/auth/login-otp', { method: 'POST', body: { phone, code }, auth: false }),

  logout: () => apiCommand('/auth/logout', { method: 'POST', body: {}, auth: false }),

  forgotPassword: (target: string) =>
    apiRequest<OtpIssued>('/auth/forgot-password', { method: 'POST', body: { target }, auth: false }),

  resetPassword: (target: string, ticket: string, newPassword: string) =>
    apiCommand('/auth/reset-password', { method: 'POST', body: { target, ticket, newPassword }, auth: false }),
};
