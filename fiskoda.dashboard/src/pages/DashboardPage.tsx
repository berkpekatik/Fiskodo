import { useEffect, useState } from 'react'
import { motion } from 'framer-motion'
import api from '@/lib/api'
import { formatUptime, formatLatency } from '@/lib/format'
import type { BotStatusDto } from '@/types/api'
import StatCard from '@/components/StatCard'
import StatCardSkeleton from '@/components/StatCardSkeleton'
import VoiceSessionCard from '@/components/VoiceSessionCard'
import VoiceSessionSkeleton from '@/components/VoiceSessionSkeleton'
import { toast } from 'sonner'

const icons = {
  guilds: (
    <svg className="w-5 h-5" fill="currentColor" viewBox="0 0 20 20" aria-hidden>
      <path d="M5 3a2 2 0 00-2 2v2a2 2 0 002 2h2a2 2 0 002-2V5a2 2 0 00-2-2H5zM5 11a2 2 0 00-2 2v2a2 2 0 002 2h2a2 2 0 002-2v-2a2 2 0 00-2-2H5zM11 5a2 2 0 012-2h2a2 2 0 012 2v2a2 2 0 01-2 2h-2a2 2 0 01-2-2V5zM11 13a2 2 0 012-2h2a2 2 0 012 2v2a2 2 0 01-2 2h-2a2 2 0 01-2-2v-2z" />
    </svg>
  ),
  voice: (
    <svg className="w-5 h-5" fill="currentColor" viewBox="0 0 20 20" aria-hidden>
      <path fillRule="evenodd" d="M7 4a3 3 0 016 0v4a3 3 0 11-6 0V4zm4 10.93A7.001 7.001 0 0017 8a1 1 0 10-2 0A5 5 0 015 8a1 1 0 00-2 0 7.001 7.001 0 006 6.93V17H6a1 1 0 100 2h8a1 1 0 100-2h-3v-2.07z" clipRule="evenodd" />
    </svg>
  ),
  uptime: (
    <svg className="w-5 h-5" fill="currentColor" viewBox="0 0 20 20" aria-hidden>
      <path fillRule="evenodd" d="M10 18a8 8 0 100-16 8 8 0 000 16zm1-12a1 1 0 10-2 0v4a1 1 0 00.293.707l2.828 2.829a1 1 0 101.415-1.415L11 9.586V6z" clipRule="evenodd" />
    </svg>
  ),
  latency: (
    <svg className="w-5 h-5" fill="currentColor" viewBox="0 0 20 20" aria-hidden>
      <path fillRule="evenodd" d="M2 5a2 2 0 012-2h12a2 2 0 012 2v2a2 2 0 01-2 2H4a2 2 0 01-2-2V5zm14 1a1 1 0 11-2 0 1 1 0 012 0zM2 13a2 2 0 012-2h12a2 2 0 012 2v2a2 2 0 01-2 2H4a2 2 0 01-2-2v-2zm14 1a1 1 0 11-2 0 1 1 0 012 0z" clipRule="evenodd" />
    </svg>
  ),
}

export default function DashboardPage() {
  const [data, setData] = useState<BotStatusDto | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    let cancelled = false

    async function fetchStatus() {
      try {
        const res = await api.get<BotStatusDto>('/api/BotStatus')
        if (!cancelled) setData(res.data)
      } catch (e) {
        if (!cancelled) {
          setError('Failed to load bot status')
          toast.error('Failed to load bot status')
        }
      } finally {
        if (!cancelled) setLoading(false)
      }
    }

    fetchStatus()
    const intervalId = setInterval(fetchStatus, 10_000)
    return () => {
      cancelled = true
      clearInterval(intervalId)
    }
  }, [])

  if (error) {
    return (
      <div className="min-h-screen bg-[#2c2f33] flex items-center justify-center">
        <p className="text-red-400">{error}</p>
      </div>
    )
  }

  return (
    <motion.div
      initial={{ opacity: 0 }}
      animate={{ opacity: 1 }}
      transition={{ duration: 0.2 }}
      className="min-h-screen bg-[#2c2f33] text-white p-6 md:p-8"
    >
      <div className="max-w-4xl mx-auto">
        <div className="text-center mb-8">
          <h1 className="text-2xl font-bold">Bot Status</h1>
          {!loading && data && (
            <p className="mt-1.5 text-sm text-[#b9bbbe]">
              {data.shardCount > 1 && data.shardId != null
                ? `Shard ${data.shardId + 1}/${data.shardCount}`
                : 'Single shard'}
              {' · '}
              <span className="text-[#72767d]">ID: {data.botUserId}</span>
            </p>
          )}
        </div>

        {/* Stats grid */}
        <div className="grid grid-cols-1 sm:grid-cols-2 gap-4 mb-10">
          {loading ? (
            <>
              <StatCardSkeleton />
              <StatCardSkeleton />
              <StatCardSkeleton />
              <StatCardSkeleton />
            </>
          ) : data ? (
            <>
              <StatCard
                label="Guilds"
                value={data.connectedGuilds}
                icon={icons.guilds}
                iconColor="text-[#9b59b6]"
              />
              <StatCard
                label="Voice"
                value={data.activeVoiceConnections}
                icon={icons.voice}
                iconColor="text-green-500"
              />
              <StatCard
                label="Uptime"
                value={formatUptime(data.uptime)}
                icon={icons.uptime}
                iconColor="text-orange-400"
              />
              <StatCard
                label="Latency"
                value={formatLatency(data.latency)}
                icon={icons.latency}
                iconColor="text-blue-400"
              />
            </>
          ) : null}
        </div>

        {/* Active Voice Sessions */}
        <div className="mb-4 flex items-center justify-between">
          <h2 className="text-xl font-bold">Active Voice Sessions</h2>
          {!loading && (
            <span className="text-xs font-medium px-2.5 py-1 rounded-full bg-green-500/20 text-green-400 border border-green-500/30">
              LIVE
            </span>
          )}
        </div>

        <div className="space-y-4">
          {loading ? (
            <>
              <VoiceSessionSkeleton />
              <div className="rounded-xl border-2 border-dashed border-[#40444b] bg-[#36393f]/50 p-8 flex items-center gap-4">
                <div className="w-12 h-12 rounded-full bg-[#40444b] animate-pulse" />
                <div className="space-y-2">
                  <div className="h-4 w-48 bg-[#40444b] rounded animate-pulse" />
                  <div className="h-3 w-64 bg-[#40444b] rounded animate-pulse" />
                </div>
              </div>
            </>
          ) : (
            <>
              {data?.voiceConnections?.map((conn) => (
                <VoiceSessionCard key={conn.guildId} connection={conn} />
              ))}
              <div className="rounded-xl border-2 border-dashed border-[#40444b] bg-[#36393f]/50 p-8 flex items-center gap-4 text-[#b9bbbe]">
                <span className="text-3xl font-light">+</span>
                <div>
                  <p className="font-medium text-[#b9bbbe]">No other active sessions</p>
                  <p className="text-sm text-[#72767d]">Join a voice channel to start tracking</p>
                </div>
              </div>
            </>
          )}
        </div>
      </div>
    </motion.div>
  )
}
