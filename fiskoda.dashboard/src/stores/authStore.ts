import { create } from 'zustand'
import { persist } from 'zustand/middleware'

type AuthState = {
  token: string | null
  expiresAt: string | null
  setAuth: (token: string, expiresAt: string) => void
  logout: () => void
}

export const useAuthStore = create<AuthState>()(
  persist(
    (set) => ({
      token: null,
      expiresAt: null,
      setAuth: (token, expiresAt) => set({ token, expiresAt }),
      logout: () => set({ token: null, expiresAt: null }),
    }),
    {
      name: 'fiskoda-auth',
      partialize: (s) => ({ token: s.token, expiresAt: s.expiresAt }),
    }
  )
)
