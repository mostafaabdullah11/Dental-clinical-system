import { createContext, useCallback, useContext, useEffect, useMemo, useState, type ReactNode } from 'react';
import { api, clearCsrf, refreshCsrf, setUnauthenticatedHandler } from '../api/client';
import type { Me } from '../api/types';

interface AuthState {
  me: Me | null;
  loading: boolean;
  login: (userName: string, password: string) => Promise<void>;
  logout: () => Promise<void>;
  /** Permission keys are for menu visibility only; the backend authorizes every request. */
  can: (permission: string) => boolean;
}

const AuthContext = createContext<AuthState | null>(null);

export function AuthProvider({ children }: { children: ReactNode }) {
  const [me, setMe] = useState<Me | null>(null);
  const [loading, setLoading] = useState(true);

  const loadMe = useCallback(async () => {
    try {
      setMe(await api.get<Me>('/api/me'));
    } catch {
      setMe(null);
    }
  }, []);

  useEffect(() => {
    // Any 401 clears in-memory state (shared-workstation rule: nothing is cached after a session ends).
    setUnauthenticatedHandler(() => {
      clearCsrf();
      setMe(null);
    });
    void loadMe().finally(() => setLoading(false));
  }, [loadMe]);

  const login = useCallback(async (userName: string, password: string) => {
    await refreshCsrf();
    await api.post('/api/auth/login', { userName, password });
    // Anti-forgery tokens are bound to the signed-in identity: fetch a fresh one.
    await refreshCsrf();
    await loadMe();
  }, [loadMe]);

  const logout = useCallback(async () => {
    try {
      await api.post('/api/auth/logout');
    } finally {
      clearCsrf();
      setMe(null);
    }
  }, []);

  const value = useMemo<AuthState>(() => ({
    me,
    loading,
    login,
    logout,
    can: (permission) => me?.permissions.includes(permission) ?? false,
  }), [me, loading, login, logout]);

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}

export function useAuth(): AuthState {
  const value = useContext(AuthContext);
  if (!value) {
    throw new Error('useAuth must be used inside AuthProvider');
  }
  return value;
}
