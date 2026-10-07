import { create } from 'zustand';

export type ToastKind = 'success' | 'error';

export interface Toast {
  id: number;
  kind: ToastKind;
  text: string;
}

interface ToastState {
  items: Toast[];
  push: (kind: ToastKind, text: string) => number;
  dismiss: (id: number) => void;
}

let nextId = 1;
const MAX_VISIBLE = 4;

/** The one toast system of the buyer site (spec 6.5, F4); <Toaster /> shows it. */
export const useToasts = create<ToastState>()((set) => ({
  items: [],
  push: (kind, text) => {
    const id = nextId++;
    set((s) => ({ items: [...s.items, { id, kind, text }].slice(-MAX_VISIBLE) }));
    return id;
  },
  dismiss: (id) => set((s) => ({ items: s.items.filter((t) => t.id !== id) })),
}));

export const toast = {
  success: (text: string) => useToasts.getState().push('success', text),
  error: (text: string) => useToasts.getState().push('error', text),
};

// POSTs that only read or record in the background, and sign-in forms (they show their own errors inline)
const SILENT = [/^\/checkout\/quote$/, /^\/products\/[^/]+\/views/, /^\/auth\//, /^\/chat\/conversations\/[^/]+\/(read|messages)$/,
  /^\/notifications\/[^/]+\/read$/, /^\/cart\/selection$/];

/** What a finished request says to the buyer: writes report how they went; reads and silent writes say nothing. */
export const toastFor = (method: string, path: string, ok: boolean, message: string): { kind: ToastKind; text: string } | null => {
  if (method === 'GET' || SILENT.some((r) => r.test(path))) return null;
  if (!message.trim()) return null;
  return { kind: ok ? 'success' : 'error', text: message };
};
