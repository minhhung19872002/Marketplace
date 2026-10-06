import { createContext, useContext, useState, useEffect, useCallback, type ReactNode } from 'react';

interface WishlistValue {
  ids: number[];
  toggle: (id: number) => void;
  has: (id: number) => boolean;
  count: number;
}

const WishlistContext = createContext<WishlistValue | null>(null);

const STORAGE_KEY = 'shophub_wishlist';

export const WishlistProvider = ({ children }: { children: ReactNode }) => {
  const [ids, setIds] = useState<number[]>(() => {
    try {
      const stored = localStorage.getItem(STORAGE_KEY);
      return stored ? JSON.parse(stored) : [];
    } catch {
      return [];
    }
  });

  useEffect(() => {
    localStorage.setItem(STORAGE_KEY, JSON.stringify(ids));
  }, [ids]);

  const toggle = useCallback((id: number) => {
    setIds((prev) => (prev.includes(id) ? prev.filter((x) => x !== id) : [...prev, id]));
  }, []);

  const has = useCallback((id: number) => ids.includes(id), [ids]);

  return (
    <WishlistContext.Provider value={{ ids, toggle, has, count: ids.length }}>
      {children}
    </WishlistContext.Provider>
  );
};

export const useWishlist = (): WishlistValue => {
  const ctx = useContext(WishlistContext);
  if (!ctx) throw new Error('useWishlist must be used within WishlistProvider');
  return ctx;
};
