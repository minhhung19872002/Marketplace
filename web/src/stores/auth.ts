import { create } from 'zustand';

export interface AuthUser {
  id: string;
  fullName: string;
  phone: string | null;
  email: string | null;
  avatarUrl: string | null;
  mustChangePassword: boolean;
}

export interface AuthResult {
  accessToken: string;
  accessTokenExpiresAt: string;
  refreshToken: string;
  refreshTokenExpiresAt: string;
  user: AuthUser;
}

export type AuthStatus = 'checking' | 'authenticated' | 'anonymous';

interface AuthState {
  // Access token lives in memory only; the refresh token is an httpOnly cookie the page cannot read
  accessToken: string | null;
  user: AuthUser | null;
  status: AuthStatus;
  setSession: (result: AuthResult) => void;
  setUser: (user: AuthUser) => void;
  clear: () => void;
}

export const useAuthStore = create<AuthState>((set) => ({
  accessToken: null,
  user: null,
  status: 'checking',
  setSession: (result) => set({ accessToken: result.accessToken, user: result.user, status: 'authenticated' }),
  setUser: (user) => set({ user }),
  clear: () => set({ accessToken: null, user: null, status: 'anonymous' }),
}));
