import { useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { z } from 'zod'
import { useNavigate } from 'react-router-dom'
import { toast } from 'sonner'
import { useAuthStore } from '@/stores/authStore'
import api from '@/lib/api'
import type { AuthResponse } from '@/types/api'

const schema = z.object({
  username: z.string().min(1, 'Username is required'),
  password: z.string().min(1, 'Password is required'),
})

type FormData = z.infer<typeof schema>

export default function LoginPage() {
  const navigate = useNavigate()
  const setAuth = useAuthStore((s) => s.setAuth)

  const {
    register,
    handleSubmit,
    formState: { errors, isSubmitting },
  } = useForm<FormData>({
    resolver: zodResolver(schema),
    defaultValues: { username: '', password: '' },
  })

  const onSubmit = async (data: FormData) => {
    try {
      const res = await api.post<AuthResponse>('/api/Auth/token', {
        username: data.username,
        password: data.password,
      })
      const { token, expiresAt } = res.data
      setAuth(token, expiresAt)
      toast.success('Logged in successfully')
      navigate('/', { replace: true })
    } catch (err: unknown) {
      const status = err && typeof err === 'object' && 'response' in err
        ? (err as { response?: { status?: number } }).response?.status
        : null
      if (status === 401) {
        toast.error('Invalid username or password')
      } else {
        toast.error('Login failed. Please try again.')
      }
    }
  }

  return (
    <div className="min-h-screen flex flex-col items-center justify-center px-4 bg-[#2f3136]">
      <div className="w-full max-w-[400px]">
        <h1 className="text-2xl font-semibold text-white text-center mb-1">
          Welcome back!
        </h1>
        <p className="text-[#b9bbbe] text-sm text-center mb-8">
          We're so excited to see you again!
        </p>

        <form onSubmit={handleSubmit(onSubmit)} className="space-y-5">
          <div>
            <label className="block text-[#b9bbbe] text-xs font-medium uppercase tracking-wide mb-2">
              Username
            </label>
            <input
              {...register('username')}
              type="text"
              autoComplete="username"
              className="w-full h-10 px-3 rounded bg-[#202225] border-0 text-white placeholder-[#72767d] focus:outline-none focus:ring-0"
              placeholder=""
            />
            {errors.username && (
              <p className="mt-1 text-sm text-red-400">{errors.username.message}</p>
            )}
          </div>

          <div>
            <label className="block text-[#b9bbbe] text-xs font-medium uppercase tracking-wide mb-2">
              Password
            </label>
            <input
              {...register('password')}
              type="password"
              autoComplete="current-password"
              className="w-full h-10 px-3 rounded bg-[#202225] border-0 text-white placeholder-[#72767d] focus:outline-none focus:ring-0"
              placeholder=""
            />
            {errors.password && (
              <p className="mt-1 text-sm text-red-400">{errors.password.message}</p>
            )}
          </div>

          <button
            type="submit"
            disabled={isSubmitting}
            className="w-full h-10 rounded bg-[#5865f2] hover:bg-[#4752c4] text-white font-medium transition-colors disabled:opacity-50 disabled:cursor-not-allowed"
          >
            {isSubmitting ? 'Logging in...' : 'Log In'}
          </button>
        </form>
      </div>
    </div>
  )
}
