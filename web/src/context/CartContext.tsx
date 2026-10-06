import { createContext, useContext, useState, useEffect, useCallback, type ReactNode } from 'react';
import type { CartLine, Product } from '../types';

// Product as passed to addToCart (variant chosen on the detail page)
type CartInput = Product & { selectedVariant?: string };

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

const STORAGE_KEY = 'shophub_cart';

// Khóa dòng giỏ hàng: cùng SP khác phân loại -> 2 dòng riêng
const makeKey = (product: CartInput): string => `${product.id}::${product.selectedVariant || ''}`;

// Số lượng hợp lệ: tối thiểu 1, tối đa bằng tồn kho (nếu có)
const clampQty = (item: Pick<Product, 'stock'>, quantity: number): number => Math.max(1, Math.min(item.stock ?? Infinity, quantity));

export const CartProvider = ({ children }: { children: ReactNode }) => {
  const [items, setItems] = useState<CartLine[]>(() => {
    try {
      const stored = localStorage.getItem(STORAGE_KEY);
      return stored ? JSON.parse(stored) : [];
    } catch {
      return [];
    }
  });

  useEffect(() => {
    localStorage.setItem(STORAGE_KEY, JSON.stringify(items));
  }, [items]);

  const addToCart = useCallback((product: CartInput, quantity = 1) => {
    const key = makeKey(product);
    setItems((prev) => {
      const existing = prev.find((it) => it.cartKey === key);
      if (existing) {
        return prev.map((it) =>
          it.cartKey === key ? { ...it, quantity: clampQty(it, it.quantity + quantity) } : it
        );
      }
      return [
        ...prev,
        { ...product, selectedVariant: product.selectedVariant || '', cartKey: key, quantity: clampQty(product, quantity) },
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
