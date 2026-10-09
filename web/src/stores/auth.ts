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

// Non-sensitive hint that this browser holds a refresh cookie (its expiry only, never a token). Without it the page
// does not call /auth/refresh at all, so an anonymous visit has no failed request in the console (G3 A2). The key is
// shared by web / seller / admin: they share the origin and the cookie.
const SESSION_HINT = 'sh_session_until';
const writeHint = (until: string | null) => {
  try {
    if (until) localStorage.setItem(SESSION_HINT, until);
    else localStorage.removeItem(SESSION_HINT);
  } catch {
    // Storage blocked: the hint simply stays unknown
  }
};

/** False only when this browser surely has no live session; unreadable storage keeps the old behaviour (try). */
export const mayHaveSession = (): boolean => {
  try {
    const until = localStorage.getItem(SESSION_HINT);
    return until !== null && Date.parse(until) > Date.now();
  } catch {
    return true;
  }
}

export const useAuthStore = create<AuthState>((set) => ({
  accessToken: null,
  user: null,
  status: 'checking',
  setSession: (result) => {
    writeHint(result.refreshTokenExpiresAt);
    set({ accessToken: result.accessToken, user: result.user, status: 'authenticated' });
  },
  setUser: (user) => set({ user }),
  clear: () => {
    writeHint(null);
    set({ accessToken: null, user: null, status: 'anonymous' });
  },
}));
