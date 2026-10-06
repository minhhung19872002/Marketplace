import React, { createContext, useContext, useState, useEffect, useCallback } from 'react';

const CartContext = createContext(null);

const STORAGE_KEY = 'shophub_cart';

// Khóa dòng giỏ hàng: cùng SP khác phân loại -> 2 dòng riêng (như Shopee)
const makeKey = (product) => `${product.id}::${product.selectedVariant || ''}`;

// Số lượng hợp lệ: tối thiểu 1, tối đa bằng tồn kho (nếu có)
const clampQty = (item, quantity) => Math.max(1, Math.min(item.stock ?? Infinity, quantity));

export const CartProvider = ({ children }) => {
  const [items, setItems] = useState(() => {
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

  const addToCart = useCallback((product, quantity = 1) => {
    const key = makeKey(product);
    setItems((prev) => {
      const existing = prev.find((it) => it.cartKey === key);
      if (existing) {
        return prev.map((it) =>
          it.cartKey === key ? { ...it, quantity: clampQty(it, it.quantity + quantity) } : it
        );
      }
      return [...prev, { ...product, cartKey: key, quantity: clampQty(product, quantity) }];
    });
  }, []);

  const removeFromCart = useCallback((cartKey) => {
    setItems((prev) => prev.filter((it) => it.cartKey !== cartKey));
  }, []);

  const updateQuantity = useCallback((cartKey, quantity) => {
    setItems((prev) =>
      prev.map((it) => (it.cartKey === cartKey ? { ...it, quantity: clampQty(it, quantity) } : it))
    );
  }, []);

  const removeMany = useCallback((keys) => {
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

export const useCart = () => {
  const ctx = useContext(CartContext);
  if (!ctx) throw new Error('useCart must be used within CartProvider');
  return ctx;
};
