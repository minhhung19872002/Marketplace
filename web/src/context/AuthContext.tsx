import { useCallback, useEffect, type ReactNode } from 'react';
import { useQueryClient } from '@tanstack/react-query';
import { authApi } from '../api/auth';
import { refreshSession } from '../api/http';
import { useAuthStore, type AuthResult, type AuthUser } from '../stores/auth';

/** Restores the session from the refresh cookie once, when the app loads. */
export const AuthProvider = ({ children }: { children: ReactNode }) => {
  useEffect(() => {
    void refreshSession();
  }, []);
  return <>{children}</>;
};

interface AuthValue {
  user: AuthUser | null;
  isLoggedIn: boolean;
  isChecking: boolean;
  signIn: (result: AuthResult) => void;
  logout: () => Promise<void>;
}

export const useAuth = (): AuthValue => {
  const queryClient = useQueryClient();
  const { user, status, setSession, clear } = useAuthStore();

  const signIn = useCallback(
    (result: AuthResult) => {
      setSession(result);
      queryClient.clear();
    },
    [setSession, queryClient],
  );

  const logout = useCallback(async () => {
    try {
      await authApi.logout();
    } finally {
      clear();
      queryClient.clear();
    }
  }, [clear, queryClient]);

  return { user, isLoggedIn: status === 'authenticated', isChecking: status === 'checking', signIn, logout };
};
