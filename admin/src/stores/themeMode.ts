import { create } from 'zustand'
import type { ThemeMode } from '../theme'

const KEY = 'sh_admin_theme'

const read = (): ThemeMode => {
  try {
    return localStorage.getItem(KEY) === 'dark' ? 'dark' : 'light'
  } catch {
    return 'light'
  }
}

interface ThemeModeState {
  // Remembered per browser (a convenience only — light when storage is blocked)
  mode: ThemeMode
  toggle: () => void
}

export const useThemeMode = create<ThemeModeState>((set, get) => ({
  mode: read(),
  toggle: () => {
    const mode: ThemeMode = get().mode === 'dark' ? 'light' : 'dark'
    try {
      localStorage.setItem(KEY, mode)
    } catch {
      // storage blocked: the choice lasts for this tab only
    }
    set({ mode })
  },
}))
