import React, { createContext, useContext, useState, useEffect, useCallback } from 'react';

const AuthContext = createContext(null);

const STORAGE_KEY = 'shophub_user';

export const AuthProvider = ({ children }) => {
  const [user, setUser] = useState(() => {
    try {
      const stored = localStorage.getItem(STORAGE_KEY);
      return stored ? JSON.parse(stored) : null;
    } catch {
      return null;
    }
  });

  useEffect(() => {
    if (user) localStorage.setItem(STORAGE_KEY, JSON.stringify(user));
    else localStorage.removeItem(STORAGE_KEY);
  }, [user]);

  // Đăng nhập/đăng ký giả lập bằng localStorage (không cần backend)
  const login = useCallback((username) => {
    setUser({ name: username, joinedAt: '2024' });
  }, []);

  const logout = useCallback(() => setUser(null), []);

  return (
    <AuthContext.Provider value={{ user, isLoggedIn: Boolean(user), login, logout }}>
      {children}
    </AuthContext.Provider>
  );
};

export const useAuth = () => {
  const ctx = useContext(AuthContext);
  if (!ctx) throw new Error('useAuth must be used within AuthProvider');
  return ctx;
};
