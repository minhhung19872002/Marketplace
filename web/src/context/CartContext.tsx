import { createContext, useContext, useState, useEffect, useCallback, type ReactNode } from 'react';
import type { CartLine } from '../types';

// A SKU as chosen on the product page (the cart key is the SKU id)
type CartInput = Omit<CartLine, 'cartKey' | 'quantity'>;

interface CartValue {
  items: CartLine[];
  addToCart: (product: CartInput, quantity?: number) => void;
  removeFromCart: (cartKey: string) => void;
  updateQuantity: (cartKey: string, quantity: number) => void;
  removeMany: (keys: string[]) => void;
  clearCart: () => void;
  totalItems: number;
  totalPrice: number;
}

const CartContext = createContext<CartValue | null>(null);

// v2: lines are SKUs from the API (v1 held mock products with numeric ids)
const STORAGE_KEY = 'shophub_cart_v2';

// Quantity between 1 and what the SKU had available when it was added
const clampQty = (item: Pick<CartLine, 'available'>, quantity: number): number => Math.max(1, Math.min(item.available, quantity));

export const CartProvider = ({ children }: { children: ReactNode }) => {
  const [items, setItems] = useState<CartLine[]>(() => {
    try {
      localStorage.removeItem('shophub_cart');
      const stored = localStorage.getItem(STORAGE_KEY);
      return stored ? JSON.parse(stored) : [];
    } catch {
      return [];
    }
  });

  useEffect(() => {
    try {
      localStorage.setItem(STORAGE_KEY, JSON.stringify(items));
    } catch {
      // storage blocked: the cart lives for this tab only
    }
  }, [items]);

  const addToCart = useCallback((product: CartInput, quantity = 1) => {
    const key = product.skuId;
    setItems((prev) => {
      const existing = prev.find((it) => it.cartKey === key);
      if (existing) {
        return prev.map((it) =>
          it.cartKey === key ? { ...it, quantity: clampQty(it, it.quantity + quantity) } : it
        );
      }
      return [
        ...prev,
        { ...product, cartKey: key, quantity: clampQty(product, quantity) },
      ];
    });
  }, []);

  const removeFromCart = useCallback((cartKey: string) => {
    setItems((prev) => prev.filter((it) => it.cartKey !== cartKey));
  }, []);

  const updateQuantity = useCallback((cartKey: string, quantity: number) => {
    setItems((prev) =>
      prev.map((it) => (it.cartKey === cartKey ? { ...it, quantity: clampQty(it, quantity) } : it))
    );
  }, []);

  const removeMany = useCallback((keys: string[]) => {
    const set = new Set(keys);
    setItems((prev) => prev.filter((it) => !set.has(it.cartKey)));
  }, []);

  const clearCart = useCallback(() => setItems([]), []);

  const totalItems = items.reduce((sum, it) => sum + it.quantity, 0);
  const totalPrice = items.reduce((sum, it) => sum + it.price * it.quantity, 0);

  return (
    <CartContext.Provider
      value={{
        items,
        addToCart,
        removeFromCart,
        updateQuantity,
        removeMany,
        clearCart,
        totalItems,
        totalPrice,
      }}
    >
      {children}
    </CartContext.Provider>
  );
};

export const useCart = (): CartValue => {
  const ctx = useContext(CartContext);
  if (!ctx) throw new Error('useCart must be used within CartProvider');
  return ctx;
};
